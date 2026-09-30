using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using VoidUI.Models;

namespace VoidUI.Services
{
    public class CombatService
    {
        private readonly SteamService _steamService;
        private CancellationTokenSource _monitorCts;
        private readonly HashSet<string> _processedDeaths = new();
        private readonly List<ServerInfo> _servers = new();
        private readonly Dictionary<string, string> _serverNameCache = new();
        private readonly Dictionary<string, string> _avatarCache = new();
        private string _currentServerIp = "";
        private long _lastReadPosition;
        private static readonly HttpClient _http = new();

        public event Action<List<ServerInfo>> OnDataUpdated;
        public event Action<string> OnStatusChanged;

        public CombatService(SteamService steamService)
        {
            _steamService = steamService;
        }

        public void StartMonitoring()
        {
            if (_monitorCts != null) return;
            _lastReadPosition = 0;
            _monitorCts = new CancellationTokenSource();
            Task.Run(() => MonitorLoop(_monitorCts.Token));
            OnStatusChanged?.Invoke("Monitoring");
            OnDataUpdated?.Invoke(_servers);
        }

        public void StopMonitoring()
        {
            _monitorCts?.Cancel();
            _monitorCts = null;
            OnStatusChanged?.Invoke("Stopped");
            OnDataUpdated?.Invoke(_servers);
        }

        private async Task MonitorLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    ProcessLog();
                    await Task.Delay(3000, token);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Combat monitoring error: {ex.Message}");
                }
            }
        }

        private void ProcessLog()
        {
            var logPath = _steamService.GetRustLogPath();
            if (string.IsNullOrEmpty(logPath) || !File.Exists(logPath)) return;

            try
            {
                using var stream = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                stream.Seek(_lastReadPosition, SeekOrigin.Begin);

                if (stream.Position >= stream.Length) return;

                using var reader = new StreamReader(stream);
                var content = reader.ReadToEnd();
                _lastReadPosition = stream.Position;

                if (string.IsNullOrEmpty(content)) return;

                ParseServerConnections(content);
                ParseDeathEvents(content);
                OnDataUpdated?.Invoke(_servers);
            }
            catch { }
        }

        private void ParseServerConnections(string content)
        {
            var connectRegex = new Regex(@"Connecting:\s*(\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}):(\d+)\s*\(Raknet\)", RegexOptions.Compiled);

            var connectMatches = connectRegex.Matches(content).Cast<Match>().ToList();

            foreach (var match in connectMatches)
            {
                var ip = match.Groups[1].Value;
                var port = match.Groups[2].Value;
                var serverIp = $"{ip}:{port}";
                var key = $"{ip}_{port}";

                _currentServerIp = serverIp;

                if (!string.IsNullOrEmpty(serverIp) && !_servers.Any(s => s.ServerIp == serverIp))
                {
                    var server = new ServerInfo
                    {
                        ServerName = _serverNameCache.TryGetValue(key, out var cached) ? cached : serverIp,
                        ServerIp = serverIp
                    };
                    _servers.Insert(0, server);

                    _ = ResolveServerNameAsync(ip, int.Parse(port), key, server);
                }
            }
        }

        private async Task ResolveServerNameAsync(string ip, int port, string key, ServerInfo server)
        {
            try
            {
                var url = $"https://api.steampowered.com/ISteamApps/GetServersAtAddress/v1/?format=json&addr={ip}:{port}";
                var response = await _http.GetStringAsync(url);
                var nameMatch = Regex.Match(response, "\"servername\"\\s*:\\s*\"([^\"]+)\"");
                if (nameMatch.Success)
                {
                    var name = nameMatch.Groups[1].Value;
                    _serverNameCache[key] = name;
                    server.ServerName = name;
                    OnDataUpdated?.Invoke(_servers);
                }
            }
            catch
            {
                server.ServerName = $"{ip}:{port}";
            }
        }

        private async Task FetchAvatarAsync(string steamId)
        {
            try
            {
                var url = $"https://steamcommunity.com/profiles/{steamId}?xml=1";
                var response = await _http.GetStringAsync(url);
                var avatarMatch = Regex.Match(response, @"<avatarFull><!\[CDATA\[(.*?)\]\]></avatarFull>");
                if (!avatarMatch.Success)
                    avatarMatch = Regex.Match(response, @"<avatarFull>(.*?)</avatarFull>");
                if (avatarMatch.Success)
                {
                    var avatarUrl = avatarMatch.Groups[1].Value.Trim();
                    if (!string.IsNullOrEmpty(avatarUrl))
                    {
                        _avatarCache[steamId] = avatarUrl;
                        foreach (var s in _servers)
                            foreach (var t in s.Targets)
                                if (t.SteamId == steamId) t.AvatarUrl = avatarUrl;
                        OnDataUpdated?.Invoke(_servers);
                    }
                }
            }
            catch { }
        }

        private void ParseDeathEvents(string content)
        {
            var deathRegex = new Regex(
                @"(\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d+Z)\|0x[0-9a-fA-F]+\|You died:\s*(.*)",
                RegexOptions.Compiled);

            var playerKillRegex = new Regex(
                @"^killed by\s+(.+?)\s*\((\d{17,})\)\s*$",
                RegexOptions.Compiled);

            var npcKillRegex = new Regex(
                @"^killed by\s+(.+?)\s*$",
                RegexOptions.Compiled);

            foreach (Match match in deathRegex.Matches(content))
            {
                var timestamp = match.Groups[1].Value;
                var deathType = match.Groups[2].Value.Trim();

                var deathKey = $"{timestamp}_{deathType}";
                if (_processedDeaths.Contains(deathKey)) continue;
                _processedDeaths.Add(deathKey);

                string killerName;
                string killerSteamId;
                bool isPlayer;

                var playerMatch = playerKillRegex.Match(deathType);
                if (playerMatch.Success)
                {
                    killerName = playerMatch.Groups[1].Value;
                    killerSteamId = playerMatch.Groups[2].Value;
                    isPlayer = true;
                }
                else
                {
                    var npcMatch = npcKillRegex.Match(deathType);
                    if (npcMatch.Success)
                    {
                        killerName = npcMatch.Groups[1].Value;
                    }
                    else
                    {
                        killerName = deathType;
                    }
                    killerSteamId = "";
                    isPlayer = false;
                }

                var isoTime = timestamp.Replace("Z", "+00:00");
                if (DateTimeOffset.TryParse(isoTime, out var dto))
                {
                    var localTime = dto.ToLocalTime();
                    timestamp = localTime.ToString("yyyy-MM-dd HH:mm:ss");
                }

                var server = _servers.FirstOrDefault(s => s.ServerIp == _currentServerIp);
                if (server == null)
                {
                    server = new ServerInfo
                    {
                        ServerName = _currentServerIp,
                        ServerIp = _currentServerIp
                    };
                    _servers.Add(server);
                }

                if (isPlayer && !string.IsNullOrEmpty(killerSteamId) && !_avatarCache.ContainsKey(killerSteamId))
                {
                    _ = FetchAvatarAsync(killerSteamId);
                }

                var existingTarget = server.Targets.FirstOrDefault(t =>
                    (isPlayer && t.SteamId == killerSteamId) ||
                    (!isPlayer && t.PlayerName == killerName && t.SteamId == ""));

                if (existingTarget != null)
                {
                    existingTarget.TimesKilledMe++;
                    existingTarget.LastDeathTime = timestamp;
                    if (isPlayer && _avatarCache.TryGetValue(killerSteamId, out var av))
                        existingTarget.AvatarUrl = av;
                    existingTarget.DeathHistory.Add(new DeathEvent
                    {
                        Timestamp = timestamp,
                        KillerName = killerName,
                        KillerSteamId = killerSteamId,
                        IsPlayer = isPlayer
                    });
                }
                else
                {
                    var avatarUrl = (isPlayer && _avatarCache.TryGetValue(killerSteamId, out var av)) ? av : "";
                    var newTarget = new TargetPlayer
                    {
                        PlayerName = killerName,
                        SteamId = killerSteamId,
                        AvatarUrl = avatarUrl,
                        LastDeathTime = timestamp,
                        TimesKilledMe = 1,
                        TimesIKilled = 0,
                        DeathHistory = new List<DeathEvent>
                        {
                            new()
                            {
                                Timestamp = timestamp,
                                KillerName = killerName,
                                KillerSteamId = killerSteamId,
                                IsPlayer = isPlayer
                            }
                        }
                    };
                    server.Targets.Add(newTarget);
                }
            }

            if (_processedDeaths.Count > 500)
            {
                var keep = _processedDeaths.TakeLast(250).ToList();
                _processedDeaths.Clear();
                foreach (var k in keep) _processedDeaths.Add(k);
            }
        }
    }
}
