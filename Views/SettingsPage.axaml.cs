using System;
using System.Diagnostics;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using VoidUI.Services;

namespace VoidUI.Views;

public partial class SettingsPage : UserControl
{
    private readonly SettingsService _settings;
    private readonly string _currentTheme;
    private readonly Action<string> _applyTheme;
    private readonly Action _resetAll;

    private readonly (string Name, string Label, string Preview, string Accent)[] _themes = new[]
    {
        ("black", "Black", "#111114", "#7c5cfc"),
        ("glass-black", "Glass", "#0d0d11", "#60a5fa"),
        ("retro", "Retro", "#1a1a2e", "#fbbf24"),
        ("pink", "Pink", "#1e1520", "#f472b6"),
        ("slack", "Slack", "#1c1e20", "#94a3b8"),
        ("cyber", "Cyber", "#111620", "#34d399"),
        ("mono", "Mono", "#1a1a1a", "#a1a1aa"),
    };

    public SettingsPage(SettingsService settings, string currentTheme, Action<string> applyTheme, Action resetAll)
    {
        _settings = settings;
        _currentTheme = currentTheme;
        _applyTheme = applyTheme;
        _resetAll = resetAll;
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        SetupThemes();
        SetupActionButtons();
    }

    private void SetupThemes()
    {
        var wrap = this.FindControl<WrapPanel>("ThemeWrap");
        if (wrap == null) return;
        foreach (var (name, label, preview, accent) in _themes)
        {
            var isActive = name == _currentTheme;
            var btn = new Button { Content = label, Tag = name, MinWidth = 100, Height = 42 };
            btn.Classes.Add("theme-btn");
            btn.Background = new SolidColorBrush(Color.Parse(preview));
            btn.Foreground = new SolidColorBrush(Color.Parse("#ffffff"));
            btn.BorderBrush = isActive ? new SolidColorBrush(Color.Parse(accent)) : new SolidColorBrush(Color.Parse("#232329"));
            btn.BorderThickness = isActive ? new Thickness(2) : new Thickness(1);

            var accentBar = new Border
            {
                Height = 3, CornerRadius = new CornerRadius(2),
                Background = new SolidColorBrush(Color.Parse(accent)),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Opacity = isActive ? 1.0 : 0.0
            };

            var stack = new StackPanel { Spacing = 4 };
            stack.Children.Add(btn);
            stack.Children.Add(accentBar);

            var wrapper = new StackPanel();
            wrapper.Children.Add(stack);

            btn.Click += (_, _) =>
            {
                _applyTheme(name);
                foreach (var c in wrap.Children)
                {
                    if (c is StackPanel w && w.Children.Count > 0 && w.Children[0] is StackPanel s && s.Children.Count >= 2)
                    {
                        var b = s.Children[0] as Button;
                        var bar = s.Children[1] as Border;
                        if (b?.Tag?.ToString() == name)
                        {
                            b.BorderBrush = new SolidColorBrush(Color.Parse(accent));
                            b.BorderThickness = new Thickness(2);
                            if (bar != null) bar.Opacity = 1.0;
                        }
                        else
                        {
                            b.BorderBrush = new SolidColorBrush(Color.Parse("#232329"));
                            b.BorderThickness = new Thickness(1);
                            if (bar != null) bar.Opacity = 0.0;
                        }
                    }
                }
            };
            wrap.Children.Add(wrapper);
        }
    }

    private void SetupActionButtons()
    {
        var resetBtn = this.FindControl<Button>("ResetAllBtn");
        if (resetBtn != null) resetBtn.Click += (_, _) => _resetAll();
        var folderBtn = this.FindControl<Button>("OpenFolderBtn");
        if (folderBtn != null) folderBtn.Click += (_, _) => { var p = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VoidUI"); Process.Start(new ProcessStartInfo { FileName = p, UseShellExecute = true }); };
        var exportBtn = this.FindControl<Button>("ExportBtn");
        if (exportBtn != null) exportBtn.Click += (_, _) =>
        {
            var src = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VoidUI", "settings.json");
            if (File.Exists(src)) { var dst = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "void_settings_export.json"); File.Copy(src, dst, true); }
        };
    }
}