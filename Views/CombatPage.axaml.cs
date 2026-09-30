using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using VoidUI.Models;
using VoidUI.Services;

namespace VoidUI.Views;

public partial class CombatPage : UserControl
{
    private readonly CombatService _combatService;
    private readonly SettingsService _settings;
    private readonly StringBuilder _logBuffer = new();
    private readonly List<TrackedSite> _customSites = new();
    private List<ServerInfo> _lastServers = new();
    private string? _lastFirstIp;
    private string _filter = "";
    private bool _isMonitoring;
    private bool _isAutoMode;

    public CombatPage(CombatService combatService, SettingsService settings)
    {
        _combatService = combatService;
        _settings = settings;
        InitializeComponent();
        _customSites.AddRange(settings.GetSettings().Combat.CustomSites ?? new List<TrackedSite>());
        Loaded += OnLoaded;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        _combatService.OnDataUpdated += OnDataUpdated;
        _combatService.OnStatusChanged += OnStatusChanged;

        var runBtn = this.FindControl<Button>("RunBtn");
        if (runBtn != null) runBtn.Click += (_, _) => ToggleMonitoring();

        var autoBtn = this.FindControl<Button>("AutoBtn");
        if (autoBtn != null) autoBtn.Click += (_, _) => ToggleAutoMode();

        var clearLogBtn = this.FindControl<Button>("ClearLogBtn");
        if (clearLogBtn != null) clearLogBtn.Click += (_, _) => { _logBuffer.Clear(); UpdateLog("[COMBAT] Log cleared"); };

        var exportBtn = this.FindControl<Button>("ExportLogBtn");
        if (exportBtn != null) exportBtn.Click += (_, _) => ExportLog();

        var bmBtn = this.FindControl<Button>("OpenBMBtn");
        if (bmBtn != null) bmBtn.Click += (_, _) => OpenBattleMetrics();

        var addSiteBtn = this.FindControl<Button>("AddSiteBtn");
        if (addSiteBtn != null) addSiteBtn.Click += (_, _) => ShowAddSiteDialog();

        var filterBox = this.FindControl<TextBox>("CombatFilter");
        if (filterBox != null) filterBox.TextChanged += (_, _) => ApplyFilter(filterBox.Text);

        RenderCustomSitesBar();
    }

    private void ApplyFilter(string? text)
    {
        _filter = text?.Trim().ToLowerInvariant() ?? "";
        RenderServers(_lastServers);
    }

    private string ResolveSiteUrl(TrackedSite site)
    {
        var ip = _lastServers.FirstOrDefault()?.ServerIp;
        return site.UrlTemplate.Replace("{ip}", ip ?? "");
    }

    private void RenderCustomSitesBar()
    {
        var bar = this.FindControl<StackPanel>("CustomSitesBar");
        if (bar == null) return;
        bar.Children.Clear();
        foreach (var site in _customSites)
        {
            var url = ResolveSiteUrl(site);
            bar.Children.Add(CreateSitePill(site.Name, url, () => OpenUrl(url), () => RemoveSite(site)));
        }
    }

    private void OpenBattleMetrics()
    {
        var ip = _lastServers.FirstOrDefault()?.ServerIp;
        var url = string.IsNullOrWhiteSpace(ip)
            ? "https://www.battlemetrics.com/"
            : $"https://www.battlemetrics.com/servers/search?q={Uri.EscapeDataString(ip)}";
        OpenUrl(url);
    }

    private void ToggleMonitoring()
    {
        if (_isMonitoring)
        {
            _combatService.StopMonitoring();
            _isMonitoring = false;
            UpdateRunButton(false);
        }
        else
        {
            _combatService.StartMonitoring();
            _isMonitoring = true;
            UpdateRunButton(true);
        }
    }

    private void UpdateRunButton(bool active)
    {
        var runBtn = this.FindControl<Button>("RunBtn");
        if (runBtn == null) return;

        if (active)
        {
            runBtn.Content = "Stop";
            runBtn.Classes.Add("active");
        }
        else
        {
            runBtn.Content = "Run";
            runBtn.Classes.Remove("active");
        }
    }

    private void ToggleAutoMode()
    {
        _isAutoMode = !_isAutoMode;
        var autoBtn = this.FindControl<Button>("AutoBtn");

        if (_isAutoMode)
        {
            if (autoBtn != null)
            {
                autoBtn.Content = "Auto ON";
                autoBtn.Classes.Add("active");
            }
            if (!_isMonitoring)
            {
                _combatService.StartMonitoring();
                _isMonitoring = true;
                UpdateRunButton(true);
            }
        }
        else
        {
            if (autoBtn != null)
            {
                autoBtn.Content = "Auto";
                autoBtn.Classes.Remove("active");
            }
        }
    }

    private void OnStatusChanged(string status)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var statusText = this.FindControl<TextBlock>("StatusText");
            if (statusText != null) statusText.Text = status;
        });
    }

    private void OnDataUpdated(List<ServerInfo> servers)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _lastServers = servers;
            var firstIp = servers.FirstOrDefault()?.ServerIp;
            if (firstIp != _lastFirstIp)
            {
                _lastFirstIp = firstIp;
                RenderCustomSitesBar();
            }
            RenderServers(servers);
        });
    }

    private void RenderServers(List<ServerInfo> servers)
    {
        var panel = this.FindControl<StackPanel>("ServersPanel");
        if (panel == null) return;
        panel.Children.Clear();

        bool hasFilter = _filter.Length > 0;
        int shownServers = 0;

        foreach (var server in servers)
        {
            bool serverMatches = !hasFilter
                || server.ServerName.ToLowerInvariant().Contains(_filter)
                || server.ServerIp.ToLowerInvariant().Contains(_filter);

            List<TargetPlayer> targets = (!hasFilter || serverMatches)
                ? server.Targets
                : server.Targets.Where(t => TargetMatches(t, _filter)).ToList();

            if (hasFilter && !serverMatches && targets.Count == 0) continue;
            shownServers++;

            var serverBorder = new Border
            {
                Background = new SolidColorBrush(Color.Parse("#111114")),
                BorderBrush = new SolidColorBrush(Color.Parse("#1a1a1f")),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(16, 10)
            };
            var sp = new StackPanel { Spacing = 4 };

            var serverRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            var serverInfo = new StackPanel { Orientation = Orientation.Vertical, VerticalAlignment = VerticalAlignment.Center };
            serverInfo.Children.Add(new TextBlock { Text = server.ServerName, Foreground = new SolidColorBrush(Color.Parse("#ededef")), FontSize = 13, FontWeight = FontWeight.SemiBold });
            serverInfo.Children.Add(new TextBlock { Text = server.ServerIp, Foreground = new SolidColorBrush(Color.Parse("#5c5c66")), FontSize = 10 });
serverRow.Children.Add(serverInfo);

            sp.Children.Add(serverRow);

            foreach (var target in targets.OrderByDescending(t => t.TimesKilledMe))
            {
                var targetSp = new StackPanel { Spacing = 2, Margin = new Thickness(12, 4, 0, 0) };

                var targetRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
                targetRow.Children.Add(new TextBlock
                {
                    Text = $"{target.PlayerName} — Killed me {target.TimesKilledMe}x, I killed {target.TimesIKilled}x",
                    Foreground = new SolidColorBrush(Color.Parse("#ededef")),
                    FontSize = 11
                });
                if (!string.IsNullOrEmpty(target.SteamId))
                {
                    targetRow.Children.Add(CreateCopyableId(target.SteamId));
                    targetRow.Children.Add(CreateSteamButton(target.PlayerName, target.SteamId));
                }
                targetSp.Children.Add(targetRow);

                foreach (var death in target.DeathHistory.TakeLast(3))
                {
                    var deathRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
                    deathRow.Children.Add(new TextBlock
                    {
                        Text = $"  [{death.Timestamp}] {death.KillerName}",
                        Foreground = new SolidColorBrush(Color.Parse("#5c5c66")),
                        FontSize = 10,
                        FontFamily = new FontFamily("Cascadia Code, Consolas, monospace")
                    });
                    if (death.IsPlayer && !string.IsNullOrEmpty(death.KillerSteamId))
                    {
                        deathRow.Children.Add(CreateCopyableId(death.KillerSteamId));
                        deathRow.Children.Add(CreateSteamButton(death.KillerName, death.KillerSteamId));
                    }
                    targetSp.Children.Add(deathRow);
                }
                sp.Children.Add(targetSp);
            }
            serverBorder.Child = sp;
            panel.Children.Add(serverBorder);
        }

        if (hasFilter && shownServers == 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = "No matches",
                Foreground = new SolidColorBrush(Color.Parse("#5c5c66")),
                FontSize = 12,
                Margin = new Thickness(16, 14)
            });
        }

        _logBuffer.Clear();
        foreach (var server in servers)
        {
            _logBuffer.AppendLine($"[SERVER] {server.ServerName} ({server.ServerIp})");
            foreach (var target in server.Targets.OrderByDescending(t => t.TimesKilledMe))
            {
                foreach (var death in target.DeathHistory.TakeLast(5))
                    _logBuffer.AppendLine($"  [{death.Timestamp}] Killed by {death.KillerName}{(death.IsPlayer ? $" ({death.KillerSteamId})" : "")}");
                _logBuffer.AppendLine($"  K/D: {target.TimesIKilled}/{target.TimesKilledMe}");
            }
        }
        if (_logBuffer.Length > 0) UpdateLog(_logBuffer.ToString());
    }

    private static bool TargetMatches(TargetPlayer target, string filter)
    {
        if (target.PlayerName.ToLowerInvariant().Contains(filter)) return true;
        if (!string.IsNullOrEmpty(target.SteamId) && target.SteamId.Contains(filter)) return true;
        return target.DeathHistory.Any(d =>
            (!string.IsNullOrEmpty(d.KillerName) && d.KillerName.ToLowerInvariant().Contains(filter)) ||
            (!string.IsNullOrEmpty(d.KillerSteamId) && d.KillerSteamId.Contains(filter)));
    }

    private Button CreateSiteButton(string label, string tooltip, Action onClick)
    {
        var btn = new Button
        {
            Content = label,
            Height = 22,
            MinWidth = 0,
            Padding = new Thickness(8, 0),
            FontSize = 9,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
            Classes = { "pill" }
        };
        ToolTip.SetTip(btn, tooltip);
        btn.Click += (_, _) => onClick();
        return btn;
    }

    private Border CreateSitePill(string name, string tooltip, Action onOpen, Action onRemove)
    {
        var label = new TextBlock
        {
            Text = name,
            FontSize = 9,
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(Color.Parse("#ededef")),
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand)
        };
        label.PointerPressed += (_, _) => onOpen();

        var removeBtn = new Button
        {
            Content = "✕",
            Width = 14,
            Height = 14,
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = new SolidColorBrush(Color.Parse("#5c5c66")),
            FontSize = 8,
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand)
        };
        removeBtn.Click += (_, _) => onRemove();

        var inner = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        inner.Children.Add(label);
        inner.Children.Add(removeBtn);

        var pill = new Border
        {
            Child = inner,
            Height = 22,
            Padding = new Thickness(8, 0),
            CornerRadius = new CornerRadius(9),
            Background = new SolidColorBrush(Color.Parse("#1e1e26")),
            BorderBrush = new SolidColorBrush(Color.Parse("#2a2a34")),
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Center
        };
        ToolTip.SetTip(pill, $"{tooltip} (click ✕ to remove)");
        return pill;
    }

    private void RemoveSite(TrackedSite site)
    {
        _customSites.Remove(site);
        var settings = _settings.GetSettings();
        settings.Combat.CustomSites = new List<TrackedSite>(_customSites);
        _settings.Save(settings);
        RenderCustomSitesBar();
    }

    private void OpenUrl(string url)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url)
            {
                UseShellExecute = true
            });
        }
        catch { }
    }

    private void ShowAddSiteDialog()
    {
        var win = new Window
        {
            Width = 360,
            Height = 236,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Brushes.Transparent,
            SystemDecorations = SystemDecorations.None,
            CanResize = false,
            ShowInTaskbar = false,
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent }
        };

        win.Resources["TextControlForegroundPlaceholder"] = new SolidColorBrush(Color.Parse("#5c5c66"));
        win.Resources["TextBoxForegroundPlaceholder"] = new SolidColorBrush(Color.Parse("#5c5c66"));

        var card = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#16161c")),
            BorderBrush = new SolidColorBrush(Color.Parse("#2a2a34")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(20),
            BoxShadow = BoxShadows.Parse("0 12 32 0 #55000000")
        };

        void StyleTextBox(TextBox tb)
        {
            tb.Background = new SolidColorBrush(Color.Parse("#111114"));
            tb.BorderBrush = new SolidColorBrush(Color.Parse("#2a2a34"));
            tb.BorderThickness = new Thickness(1);
            tb.CornerRadius = new CornerRadius(8);
            tb.Foreground = new SolidColorBrush(Color.Parse("#ededef"));
            tb.Padding = new Thickness(10, 0);
            tb.VerticalContentAlignment = VerticalAlignment.Center;
        }

        var nameBox = new TextBox { Watermark = "Site name (e.g. RustCheaters)", Height = 32, FontSize = 12 };
        var urlBox = new TextBox { Watermark = "URL — use {ip} for server IP", Height = 32, FontSize = 12, Text = "https://" };
        StyleTextBox(nameBox);
        StyleTextBox(urlBox);

        var title = new TextBlock
        {
            Text = "Add custom server tracker",
            Foreground = new SolidColorBrush(Color.Parse("#ededef")),
            FontSize = 14,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };

        var closeBtn = new Button
        {
            Content = "✕",
            Width = 26,
            Height = 26,
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = new SolidColorBrush(Color.Parse("#5c5c66")),
            CornerRadius = new CornerRadius(6),
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand)
        };
        closeBtn.Click += (_, _) => win.Close();

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        header.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        Grid.SetColumn(title, 0);
        Grid.SetColumn(closeBtn, 1);
        header.Children.Add(title);
        header.Children.Add(closeBtn);

        var addBtn = new Button
        {
            Content = "Add",
            Width = 100,
            Height = 32,
            Background = new SolidColorBrush(Color.Parse("#7c5cfc")),
            Foreground = new SolidColorBrush(Colors.White),
            CornerRadius = new CornerRadius(9),
            BorderThickness = new Thickness(0),
            FontSize = 11,
            FontWeight = FontWeight.SemiBold,
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand)
        };

        var cancelBtn = new Button
        {
            Content = "Cancel",
            Width = 100,
            Height = 32,
            Background = new SolidColorBrush(Color.Parse("#1e1e26")),
            Foreground = new SolidColorBrush(Color.Parse("#a5a5b0")),
            CornerRadius = new CornerRadius(9),
            BorderBrush = new SolidColorBrush(Color.Parse("#2a2a34")),
            BorderThickness = new Thickness(1),
            FontSize = 11,
            FontWeight = FontWeight.SemiBold,
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand)
        };

        var btnRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Right };
        btnRow.Children.Add(cancelBtn);
        btnRow.Children.Add(addBtn);

        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(header);
        panel.Children.Add(nameBox);
        panel.Children.Add(urlBox);
        panel.Children.Add(btnRow);
        card.Child = panel;
        win.Content = card;

        void Confirm()
        {
            AddSite(nameBox.Text?.Trim() ?? "", urlBox.Text?.Trim() ?? "");
            win.Close();
        }

        addBtn.Click += (_, _) => Confirm();
        cancelBtn.Click += (_, _) => win.Close();
        win.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) win.Close();
            else if (e.Key == Key.Enter) Confirm();
        };

        if (TopLevel.GetTopLevel(this) is Window owner)
            win.ShowDialog(owner);
        else
            win.Show();
    }

    private void AddSite(string name, string urlTemplate)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(urlTemplate)) return;
        _customSites.Add(new TrackedSite { Name = name, UrlTemplate = urlTemplate });

        var settings = _settings.GetSettings();
        settings.Combat.CustomSites = new List<TrackedSite>(_customSites);
        _settings.Save(settings);

        RenderCustomSitesBar();
    }

    private TextBlock CreateCopyableId(string steamId)
    {
        var tb = new TextBlock
        {
            Text = steamId,
            Foreground = new SolidColorBrush(Color.Parse("#7c5cfc")),
            FontSize = 10,
            FontFamily = new FontFamily("Cascadia Code, Consolas, monospace"),
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
            TextDecorations = TextDecorations.Underline,
            VerticalAlignment = VerticalAlignment.Center
        };
        ToolTip.SetTip(tb, $"Copy Steam ID: {steamId}");
        tb.PointerPressed += async (_, _) =>
        {
            var top = TopLevel.GetTopLevel(tb);
            if (top?.Clipboard == null) return;
            await top.Clipboard.SetTextAsync(steamId);
            tb.Text = "copied ✓";
            await Task.Delay(900);
            tb.Text = steamId;
        };
        return tb;
    }

    private Button CreateSteamButton(string playerName, string steamId)
    {
        var btn = new Button
        {
            Content = "⛨",
            Height = 16,
            MinWidth = 24,
            Padding = new Thickness(5, 0),
            FontSize = 9,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
            Classes = { "pill" },
            Opacity = 0.85
        };
        ToolTip.SetTip(btn, $"Open {playerName}'s Steam profile");
        btn.Click += (_, _) => OpenSteamProfile(steamId);
        return btn;
    }

    private void OpenSteamProfile(string steamId)
    {
        if (string.IsNullOrWhiteSpace(steamId)) return;
        var url = $"https://steamcommunity.com/profiles/{steamId}/";
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo($"steam://openurl/{url}")
            {
                UseShellExecute = true
            });
        }
        catch
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url)
            {
                UseShellExecute = true
            });
        }
    }

    private void UpdateLog(string text)
    {
        var logText = this.FindControl<TextBlock>("LogText");
        if (logText != null) logText.Text = text;
        var scroll = this.FindControl<ScrollViewer>("LogScroll");
        scroll?.ScrollToEnd();
    }

    private void ExportLog()
    {
        var path = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), $"combat_log_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
        System.IO.File.WriteAllText(path, _logBuffer.ToString());
    }
}
