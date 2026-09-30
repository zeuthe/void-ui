using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using VoidUI.Models;

namespace VoidUI.Services
{
    public class SteamService
    {
        public SteamDetectionResult DetectSteam()
        {
            var result = new SteamDetectionResult();
            var steamRoot = GetSteamPathFromRegistry();

            if (!string.IsNullOrEmpty(steamRoot) && Directory.Exists(steamRoot))
            {
                result.SteamRoot = steamRoot;
            }
            else
            {
                var defaultPaths = new[]
                {
                    @"C:\Program Files (x86)\Steam",
                    @"C:\Program Files\Steam",
                    @"C:\Steam",
                    @"D:\Steam"
                };

                foreach (var path in defaultPaths)
                {
                    if (Directory.Exists(path))
                    {
                        result.SteamRoot = path;
                        break;
                    }
                }
            }

            if (string.IsNullOrEmpty(result.SteamRoot))
                return result;

            var libraryFoldersPath = Path.Combine(result.SteamRoot, "steamapps", "libraryfolders.vdf");
            if (File.Exists(libraryFoldersPath))
            {
                try
                {
                    var vdfContent = File.ReadAllText(libraryFoldersPath);
                    result.Libraries.AddRange(ParseLibraryFolders(vdfContent));
                }
                catch { }
            }

            var defaultLib = Path.Combine(result.SteamRoot, "steamapps");
            if (!result.Libraries.Contains(defaultLib))
                result.Libraries.Insert(0, defaultLib);

            foreach (var lib in result.Libraries)
            {
                var manifestPath = Path.Combine(lib, "appmanifest_252490.acf");
                if (!File.Exists(manifestPath)) continue;

                try
                {
                    var manifest = File.ReadAllText(manifestPath);
                    var match = Regex.Match(manifest, "\"installdir\"\\s*\"([^\"]+)\"");
                    if (match.Success)
                    {
                        var installDir = match.Groups[1].Value;
                        result.RustPath = Path.Combine(lib, "common", installDir);
                        result.CfgPath = Path.Combine(result.RustPath, "cfg");
                        result.ClientCfg = Path.Combine(result.CfgPath, "client.cfg");
                        result.KeysCfg = Path.Combine(result.CfgPath, "keys.cfg");
                        result.Success = true;
                        result.Status = "ok";
                        return result;
                    }
                }
                catch { }
            }

            result.Status = "partial";
            return result;
        }

        public string GetRustLogPath()
        {
            var detection = DetectSteam();
            if (!detection.Success || string.IsNullOrEmpty(detection.RustPath))
                return null;

            var possiblePaths = new[]
            {
                Path.Combine(detection.RustPath, "output_log.txt"),
                Path.Combine(detection.RustPath, "Rust_Data", "output_log.txt"),
                Path.Combine(detection.RustPath, "Rust_Data", "log.txt"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Rust", "output_log.txt")
            };

            foreach (var p in possiblePaths)
            {
                if (File.Exists(p)) return p;
            }
            return null;
        }

        public void LaunchGame()
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "steam://rungameid/252490",
                    UseShellExecute = true
                });
            }
            catch { }
        }

        private string GetSteamPathFromRegistry()
        {
            var keys = new[]
            {
                @"SOFTWARE\WOW6432Node\Valve\Steam",
                @"SOFTWARE\Valve\Steam"
            };

            foreach (var keyPath in keys)
            {
                try
                {
                    using var key = Registry.LocalMachine.OpenSubKey(keyPath);
                    if (key?.GetValue("InstallPath") is string path && Directory.Exists(path))
                        return path;
                }
                catch { }
            }

            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
                if (key?.GetValue("SteamPath") is string path && Directory.Exists(path))
                    return path;
            }
            catch { }

            return null;
        }

        private List<string> ParseLibraryFolders(string content)
        {
            var libraries = new List<string>();
            foreach (var line in content.Split('\n'))
            {
                var match = Regex.Match(line.Trim(), @"^\s*""\d+""\s+""(.+)"".*");
                if (match.Success)
                {
                    var libPath = match.Groups[1].Value.Replace("\\\\", "\\");
                    if (!libPath.Contains("steamapps"))
                        libPath = Path.Combine(libPath, "steamapps");
                    libraries.Add(libPath);
                }
            }
            return libraries;
        }
    }
}
