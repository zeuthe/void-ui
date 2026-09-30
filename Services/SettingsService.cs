using System;
using System.IO;
using Newtonsoft.Json;
using VoidUI.Models;

namespace VoidUI.Services
{
    public class SettingsService
    {
        private readonly string _settingsPath;
        private AppSettings _settings;

        public SettingsService()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var voidUiPath = Path.Combine(appData, "VoidUI");
            Directory.CreateDirectory(voidUiPath);
            _settingsPath = Path.Combine(voidUiPath, "settings.json");
            _settings = Load();
        }

        public AppSettings GetSettings() => _settings;

        public AppSettings Load()
        {
            if (File.Exists(_settingsPath))
            {
                try
                {
                    var json = File.ReadAllText(_settingsPath);
                    _settings = JsonConvert.DeserializeObject<AppSettings>(json) ?? new AppSettings();
                }
                catch { _settings = new AppSettings(); }
            }
            else
            {
                _settings = new AppSettings();
            }
            return _settings;
        }

        public void Save(AppSettings settings)
        {
            _settings = settings;
            var json = JsonConvert.SerializeObject(settings, Formatting.Indented);
            File.WriteAllText(_settingsPath, json);
        }

        public void SaveCrosshair(CrosshairState crosshair)
        {
            _settings.Crosshair = crosshair;
            Save(_settings);
        }

        public void SetTheme(string theme)
        {
            _settings.Theme = theme;
            Save(_settings);
        }
    }
}
