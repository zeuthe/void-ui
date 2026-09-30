using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using VoidUI.Models;
using VoidUI.Services;

namespace VoidUI.Views;

public partial class CrosshairPage : UserControl
{
    private readonly SettingsService _settings;
    private readonly OverlayWindow? _overlayWindow;
    private readonly System.Windows.Threading.Dispatcher? _overlayDispatcher;

    private string _selectedPreset = "dot";
    private string _selectedAnim = "none";
    private bool _spinEnabled;
    private bool _orbitEnabled;
    private string _selectedColor = "#22c55e";
    private double _size = 16, _thickness = 2, _opacity = 100, _speed = 5, _rotation = 0, _posX = 0, _posY = 0;
    private double _spinSpeed = 5, _orbitRadius = 30, _orbitSpeed = 5;
    private string _spinDir = "normal", _orbitDir = "normal";
    private int _rainbowSpeed = 5;
    private string _rainbowTheme = "full";
    private string _bindKey = "F6";
    private string _customSymbol = "+";
    private bool _rainbowEnabled;
    private Canvas? _canvas;
    private bool _rainbowCardOpen;
    private DispatcherTimer? _rainbowAnimTimer;
    private double _rainbowExpandPhase;

    private readonly string[] _presetNames = { "dot", "cross", "circle", "bracket", "tactical", "custom" };
    private readonly string[] _presetLabels = { ".", "+", "o", "[ ]", "T", "S" };
    private readonly string[] _animNames = { "none", "pulse", "scale", "glow" };
    private readonly string[] _colorHexes =
    {
        "#22c55e", "#10b981", "#00ff00", "#00ff88", "#00ffff",
        "#0088ff", "#0044ff", "#0000ff", "#7c5cfc", "#8800ff",
        "#ff00ff", "#ff0088", "#ff0000", "#ff4444", "#ff8800",
        "#ffcc00", "#ffff00", "#88ff00", "#ffffff", "#888888"
    };

    private string[] _rainbowThemeNames = Array.Empty<string>();
    private string[] _rainbowThemeLabels = Array.Empty<string>();
    private string[][] _rainbowThemeColors = Array.Empty<string[]>();

    private DispatcherTimer? _previewTimer;
    private double _animPhase;
    private bool _suppressSliderEvents;

    public CrosshairPage(SettingsService settings, OverlayWindow? overlay, System.Windows.Threading.Dispatcher? overlayDispatcher)
    {
        _settings = settings;
        _overlayWindow = overlay;
        _overlayDispatcher = overlayDispatcher;
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        _canvas = this.FindControl<Canvas>("CrosshairCanvas");
        var ch = _settings.GetSettings().Crosshair ?? new CrosshairState();
        _selectedPreset = ch.Preset ?? "dot";
        _selectedAnim = ch.Animation ?? "none";
        _spinEnabled = ch.SpinEnabled;
        _orbitEnabled = ch.OrbitEnabled;
        _selectedColor = ch.Color ?? "#22c55e";
        _size = ch.Size; _thickness = ch.Thickness; _opacity = ch.Opacity;
        _speed = ch.Speed; _rotation = ch.Rotation; _posX = ch.PosX; _posY = ch.PosY;
        _spinSpeed = ch.SpinSpeed; _spinDir = ch.SpinDir ?? "normal";
        _orbitRadius = ch.OrbitRadius; _orbitSpeed = ch.OrbitSpeed; _orbitDir = ch.OrbitDir ?? "normal";
        _rainbowSpeed = ch.RainbowSpeed; _rainbowTheme = ch.RainbowTheme ?? "full";
        _bindKey = ch.BindKey ?? "F6";
        _customSymbol = ch.Symbol ?? "+";

        SetupButtonGroup("PresetGroup", _presetNames, _presetLabels, _selectedPreset, v => { _selectedPreset = v; ApplyLive(); });
        RebuildAnimGroup();

        var spinBtn = this.FindControl<Button>("SpinBtn");
        if (spinBtn != null)
        {
            UpdateMotionBtn(spinBtn, _spinEnabled);
            spinBtn.Click += (_, _) =>
            {
                _spinEnabled = !_spinEnabled;
                UpdateMotionBtn(spinBtn, _spinEnabled);
                SetVisible("SpinSpeedRow", _spinEnabled);
                SetVisible("SpinDirRow", _spinEnabled);
                ApplyLive();
            };
        }
        var orbitBtn = this.FindControl<Button>("OrbitBtn");
        if (orbitBtn != null)
        {
            UpdateMotionBtn(orbitBtn, _orbitEnabled);
            orbitBtn.Click += (_, _) =>
            {
                _orbitEnabled = !_orbitEnabled;
                UpdateMotionBtn(orbitBtn, _orbitEnabled);
                SetVisible("OrbitRow", _orbitEnabled);
                SetVisible("OrbitSpeedRow", _orbitEnabled);
                SetVisible("OrbitDirRow", _orbitEnabled);
                ApplyLive();
            };
        }

        SetupColorGroup();
        SetupRainbowCard();

        SetupSlider("SizeSlider", "SizeValue", _size, v => { _size = v; ApplyLive(); }, "px");
        SetupSlider("ThicknessSlider", "ThicknessValue", _thickness, v => { _thickness = v; ApplyLive(); }, "px");
        SetupSlider("OpacitySlider", "OpacityValue", _opacity, v => { _opacity = v; ApplyLive(); }, "%");
        SetupSlider("SpeedSlider", "SpeedValue", _speed, v => { _speed = v; ApplyLive(); }, "x");
        SetupSlider("RotationSlider", "RotationValue", _rotation, v => { _rotation = v; ApplyLive(); }, "");
        SetupSlider("PosXSlider", "PosXValue", _posX, v => { _posX = v; UpdatePosInputs(); ApplyLive(); }, "");
        SetupSlider("PosYSlider", "PosYValue", _posY, v => { _posY = v; UpdatePosInputs(); ApplyLive(); }, "");
        SetupSlider("SpinSpeedSlider", "SpinSpeedValue", _spinSpeed, v => { _spinSpeed = v; ApplyLive(); }, "x");
        SetupSlider("OrbitRadiusSlider", "OrbitRadiusValue", _orbitRadius, v => { _orbitRadius = v; ApplyLive(); }, "");
        SetupSlider("OrbitSpeedSlider", "OrbitSpeedValue", _orbitSpeed, v => { _orbitSpeed = v; ApplyLive(); }, "x");

        SetupToggleButton("SpinDirBtn", new[] { "Normal", "Reverse" }, _spinDir == "reverse" ? 1 : 0, idx =>
        {
            _spinDir = idx == 0 ? "normal" : "reverse";
            ApplyLive();
        });
        SetupToggleButton("OrbitDirBtn", new[] { "Normal", "Reverse" }, _orbitDir == "reverse" ? 1 : 0, idx =>
        {
            _orbitDir = idx == 0 ? "normal" : "reverse";
            ApplyLive();
        });

        SetupButton("StartOverlayBtn", () => _overlayDispatcher?.Invoke(() => _overlayWindow?.Show()));
        SetupButton("StopOverlayBtn", () => _overlayDispatcher?.Invoke(() => _overlayWindow?.Hide()));
        SetupButton("ResetBtn", () => { ResetDefaults(); SyncAllControls(); ApplyLive(); });
        SetupButton("CenterAllBtn", () => { _posX = 0; _posY = 0; SyncSlider("PosXSlider", 0); SyncSlider("PosYSlider", 0); UpdatePosInputs(); SaveSettings(); ApplyLive(); });
        SetupButton("SaveCenterBtn", () => { _posX = 0; _posY = 0; SyncSlider("PosXSlider", 0); SyncSlider("PosYSlider", 0); UpdatePosInputs(); SaveSettings(); ApplyLive(); });

        SetupButton("AddAnimBtn", async () =>
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return;
            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Import Rainbow Preset",
                AllowMultiple = false,
                FileTypeFilter = new[] { new FilePickerFileType("JSON") { Patterns = new[] { "*.json" } } }
            });
            if (files.Count == 0) return;
            var srcPath = files[0].TryGetLocalPath();
            if (string.IsNullOrEmpty(srcPath)) return;
            try
            {
                var presetsDir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "presets", "crosshair", "animpresets");
                Directory.CreateDirectory(presetsDir);
                var dstPath = System.IO.Path.Combine(presetsDir, System.IO.Path.GetFileName(srcPath));
                File.Copy(srcPath, dstPath, true);
                RebuildRainbowThemes();
            }
            catch { }
        });

        var bindInput = this.FindControl<Button>("BindInput");
        if (bindInput != null)
        {
            var bindKeys = new[] { "F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "F11", "F12" };
            int bindIdx = Math.Max(0, Array.IndexOf(bindKeys, _bindKey));
            bindInput.Content = _bindKey;
            bindInput.Click += (_, _) => { bindIdx = (bindIdx + 1) % bindKeys.Length; _bindKey = bindKeys[bindIdx]; bindInput.Content = _bindKey; ApplyLive(); };
        }

        var applyColorBtn = this.FindControl<Button>("ApplyColorBtn");
        if (applyColorBtn != null)
        {
            applyColorBtn.Click += (_, _) =>
            {
                var tb = this.FindControl<TextBox>("CustomColorInput");
                if (tb != null && !string.IsNullOrWhiteSpace(tb.Text))
                {
                    var hex = tb.Text.Trim();
                    if (!hex.StartsWith("#")) hex = "#" + hex;
                    try { Color.Parse(hex); _selectedColor = hex; SyncColorButtons(); ApplyLive(); } catch { }
                }
            };
        }

        var symbolInput = this.FindControl<TextBox>("SymbolInput");
        if (symbolInput != null)
        {
            symbolInput.Text = _customSymbol;
            symbolInput.TextChanged += (_, _) =>
            {
                if (!string.IsNullOrEmpty(symbolInput.Text)) { _customSymbol = symbolInput.Text; if (_selectedPreset == "custom") ApplyLive(); }
            };
        }

        SetupButton("CopyConfigBtn2", async () =>
        {
            var ch = new CrosshairState
            {
                Preset = _selectedPreset, Animation = _selectedAnim,
                SpinEnabled = _spinEnabled, OrbitEnabled = _orbitEnabled,
                Color = _selectedColor, Size = (int)_size, Thickness = (int)_thickness, Opacity = (int)_opacity,
                Speed = (int)_speed, Rotation = (int)_rotation, PosX = (int)_posX, PosY = (int)_posY,
                SpinSpeed = (int)_spinSpeed, SpinDir = _spinDir,
                OrbitRadius = (int)_orbitRadius, OrbitSpeed = (int)_orbitSpeed, OrbitDir = _orbitDir,
                RainbowEnabled = _rainbowEnabled, RainbowSpeed = _rainbowSpeed, RainbowTheme = _rainbowTheme,
                BindKey = _bindKey, Symbol = _customSymbol
            };
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(ch);
            var encoded = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json));
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel != null) await topLevel.Clipboard.SetTextAsync(encoded);
            ShowConfigMsg("Copied! Share this code with a friend");
        });

        SetupButton("PasteConfigBtn", async () =>
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return;
            var text = await topLevel.Clipboard.GetTextAsync();
            if (string.IsNullOrWhiteSpace(text)) { ShowConfigMsg("Clipboard is empty"); return; }
            try
            {
                var json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(text.Trim()));
                var ch = Newtonsoft.Json.JsonConvert.DeserializeObject<CrosshairState>(json);
                if (ch == null) { ShowConfigMsg("Invalid config"); return; }
                ApplyCrosshairState(ch);
                ShowConfigMsg("Loaded!");
            }
            catch { ShowConfigMsg("Invalid config code"); }
        });

        SetupConfigs();

        ApplyLive();
    }

    private void SetupConfigs()
    {
        SetupButton("SaveConfigBtn", () =>
        {
            var configsDir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "presets", "crosshair", "configs");
            Directory.CreateDirectory(configsDir);

            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var configName = $"config_{timestamp}";
            var path = System.IO.Path.Combine(configsDir, $"{configName}.json");

            var ch = new CrosshairState
            {
                Preset = _selectedPreset, Animation = _selectedAnim,
                SpinEnabled = _spinEnabled, OrbitEnabled = _orbitEnabled,
                Color = _selectedColor, Size = (int)_size, Thickness = (int)_thickness, Opacity = (int)_opacity,
                Speed = (int)_speed, Rotation = (int)_rotation, PosX = (int)_posX, PosY = (int)_posY,
                SpinSpeed = (int)_spinSpeed, SpinDir = _spinDir,
                OrbitRadius = (int)_orbitRadius, OrbitSpeed = (int)_orbitSpeed, OrbitDir = _orbitDir,
                RainbowEnabled = _rainbowEnabled, RainbowSpeed = _rainbowSpeed, RainbowTheme = _rainbowTheme,
                BindKey = _bindKey, Symbol = _customSymbol
            };

            var json = Newtonsoft.Json.JsonConvert.SerializeObject(ch, Newtonsoft.Json.Formatting.Indented);
            File.WriteAllText(path, json);

            ShowConfigStatus($"Saved: {configName}");
            RefreshConfigsList();
        });

        SetupButton("CopyConfigBtn", () =>
        {
            var ch = new CrosshairState
            {
                Preset = _selectedPreset, Animation = _selectedAnim,
                SpinEnabled = _spinEnabled, OrbitEnabled = _orbitEnabled,
                Color = _selectedColor, Size = (int)_size, Thickness = (int)_thickness, Opacity = (int)_opacity,
                Speed = (int)_speed, Rotation = (int)_rotation, PosX = (int)_posX, PosY = (int)_posY,
                SpinSpeed = (int)_spinSpeed, SpinDir = _spinDir,
                OrbitRadius = (int)_orbitRadius, OrbitSpeed = (int)_orbitSpeed, OrbitDir = _orbitDir,
                RainbowEnabled = _rainbowEnabled, RainbowSpeed = _rainbowSpeed, RainbowTheme = _rainbowTheme,
                BindKey = _bindKey, Symbol = _customSymbol
            };
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(ch, Newtonsoft.Json.Formatting.Indented);
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel != null)
                topLevel.Clipboard.SetTextAsync(json);
            ShowConfigStatus("Copied to clipboard");
        });

        SetupButton("RefreshConfigsBtn", () => RefreshConfigsList());
        RefreshConfigsList();
    }

    private void RefreshConfigsList()
    {
        var panel = this.FindControl<WrapPanel>("ConfigsGroup");
        if (panel == null) return;
        panel.Children.Clear();

        var configsDir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "presets", "crosshair", "configs");
        if (!Directory.Exists(configsDir)) return;

        foreach (var file in Directory.GetFiles(configsDir, "*.json"))
        {
            var name = System.IO.Path.GetFileNameWithoutExtension(file);
            var btn = new Button
            {
                Content = name, MinWidth = 60, Height = 24, Tag = file, FontSize = 10, Classes = { "pill" },
                BorderBrush = new SolidColorBrush(Color.Parse("#232329"))
            };
            btn.Click += (_, _) => LoadConfig(file);
            btn.ContextRequested += (_, _) =>
            {
                try { File.Delete(file); RefreshConfigsList(); ShowConfigStatus($"Deleted: {name}"); } catch { }
            };
            panel.Children.Add(btn);
        }
    }

    public void LoadConfigFile(string path) => LoadConfig(path);

    public void SelectPreset(string presetName)
    {
        if (!_presetNames.Contains(presetName)) return;
        _selectedPreset = presetName;
        SyncButtonGroupActive("PresetGroup", _presetNames, _selectedPreset);
        ApplyLive();
    }

    private void LoadConfig(string path)
    {
        try
        {
            var json = File.ReadAllText(path);
            var ch = Newtonsoft.Json.JsonConvert.DeserializeObject<CrosshairState>(json);
            if (ch == null) return;
            ApplyCrosshairState(ch);
            ShowConfigStatus($"Loaded: {System.IO.Path.GetFileNameWithoutExtension(path)}");
        }
        catch { ShowConfigStatus("Failed to load"); }
    }

    private void ApplyCrosshairState(CrosshairState ch)
    {
        _selectedPreset = ch.Preset ?? "dot";
        _selectedAnim = ch.Animation ?? "none";
        _spinEnabled = ch.SpinEnabled;
        _orbitEnabled = ch.OrbitEnabled;
        _selectedColor = ch.Color ?? "#22c55e";
        _size = ch.Size; _thickness = ch.Thickness; _opacity = ch.Opacity;
        _speed = ch.Speed; _rotation = ch.Rotation;
        _posX = ch.PosX; _posY = ch.PosY;
        _spinSpeed = ch.SpinSpeed; _spinDir = ch.SpinDir ?? "normal";
        _orbitRadius = ch.OrbitRadius; _orbitSpeed = ch.OrbitSpeed; _orbitDir = ch.OrbitDir ?? "normal";
        _rainbowEnabled = ch.RainbowEnabled;
        _rainbowSpeed = ch.RainbowSpeed; _rainbowTheme = ch.RainbowTheme ?? "full";
        _bindKey = ch.BindKey ?? "F6";
        _customSymbol = ch.Symbol ?? "+";
        SyncAllControls();
    }

    private void ShowConfigMsg(string msg)
    {
        var tb = this.FindControl<TextBlock>("ConfigMsg");
        if (tb == null) return;
        tb.Text = msg;
        tb.IsVisible = true;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        timer.Tick += (_, _) => { tb.IsVisible = false; timer.Stop(); };
        timer.Start();
    }

    private void ShowConfigStatus(string msg)
    {
        var tb = this.FindControl<TextBlock>("ConfigStatus");
        if (tb == null) return;
        tb.Text = msg;
        tb.IsVisible = true;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        timer.Tick += (_, _) => { tb.IsVisible = false; timer.Stop(); };
        timer.Start();
    }

    private void SetupButton(string name, Action onClick)
    {
        var btn = this.FindControl<Button>(name);
        if (btn != null) btn.Click += (_, _) => onClick();
    }

    private void SetupToggleButton(string name, string[] labels, int initial, Action<int> onToggle)
    {
        var btn = this.FindControl<Button>(name);
        if (btn == null) return;
        int idx = initial;
        btn.Content = labels[idx];
        btn.Click += (_, _) => { idx = (idx + 1) % labels.Length; btn.Content = labels[idx]; onToggle(idx); };
    }

    private void UpdateMotionBtn(Button btn, bool active)
    {
        if (active) { btn.Classes.Add("active"); btn.BorderBrush = new SolidColorBrush(Color.Parse("#7c5cfc")); }
        else { btn.Classes.Remove("active"); btn.BorderBrush = new SolidColorBrush(Color.Parse("#232329")); }
    }

    private void SetVisible(string name, bool visible)
    {
        var el = this.FindControl<StackPanel>(name);
        if (el != null) el.IsVisible = visible;
    }

    private void SetupColorGroup()    {
        var panel = this.FindControl<WrapPanel>("ColorGroup");
        if (panel == null) return;
        foreach (var hex in _colorHexes)
        {
            var btn = new Button
            {
                Width = 24, Height = 24, MinWidth = 24, MinHeight = 24,
                MaxWidth = 24, MaxHeight = 24, Padding = new Thickness(0),
                Tag = hex, Classes = { "pill" },
                BorderBrush = new SolidColorBrush(Color.Parse(hex == _selectedColor ? "#ffffff" : "#232329")),
                BorderThickness = new Thickness(2),
                Background = new SolidColorBrush(Color.Parse(hex))
            };
            btn.Click += (_, _) => { _selectedColor = hex; SyncColorButtons(); var tb = this.FindControl<TextBox>("CustomColorInput"); if (tb != null) tb.Text = hex; ApplyLive(); };
            panel.Children.Add(btn);
        }
    }

    private void SyncColorButtons()
    {
        var panel = this.FindControl<WrapPanel>("ColorGroup");
        if (panel == null) return;
        foreach (var child in panel.Children)
            if (child is Button b && b.Tag is string hex)
                b.BorderBrush = new SolidColorBrush(Color.Parse(hex == _selectedColor ? "#ffffff" : "#232329"));
    }

    private void SetupRainbowCard()
    {
        var toggle = this.FindControl<Button>("RainbowToggleBtn");
        var card = this.FindControl<Border>("RainbowCard");
        if (toggle == null || card == null) return;

        RebuildRainbowThemes();

        SetupSlider("RainbowSpeedSlider", "RainbowSpeedValue", _rainbowSpeed, v => { _rainbowSpeed = (int)v; ApplyLive(); }, "x");

        toggle.Click += (_, _) =>
        {
            _rainbowCardOpen = !_rainbowCardOpen;
            card.IsVisible = true;
            AnimateRainbowCard(card, _rainbowCardOpen);
            if (_rainbowCardOpen)
            {
                _rainbowEnabled = true;
                toggle.Content = "On";
            }
            if (!_rainbowCardOpen)
            {
                _rainbowEnabled = false;
                toggle.Content = "Rainbow";
                var fadeOut = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
                fadeOut.Tick += (_, _) => { if (!_rainbowCardOpen) card.IsVisible = false; fadeOut.Stop(); };
                fadeOut.Start();
            }
            ApplyLive();
        };

        UpdateRainbowPreview();
    }

    private void AnimateRainbowCard(Border card, bool open)
    {
        _rainbowAnimTimer?.Stop();
        double targetH = open ? 130 : 0;
        double targetOp = open ? 1.0 : 0.0;
        double startH = card.MaxHeight;
        double startOp = card.Opacity;
        _rainbowExpandPhase = 0;
        _rainbowAnimTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _rainbowAnimTimer.Tick += (_, _) =>
        {
            _rainbowExpandPhase += 0.08;
            double t = Math.Min(_rainbowExpandPhase, 1.0);
            double ease = 1.0 - Math.Pow(1.0 - t, 3);
            card.MaxHeight = startH + (targetH - startH) * ease;
            card.Opacity = startOp + (targetOp - startOp) * ease;
            if (t >= 1.0) _rainbowAnimTimer?.Stop();
        };
        _rainbowAnimTimer.Start();
    }

    private void UpdateRainbowPreview()
    {
        var bar = this.FindControl<Border>("RainbowPreviewBar");
        if (bar == null) return;
        int themeIdx = Array.IndexOf(_rainbowThemeNames, _rainbowTheme);
        if (themeIdx < 0) themeIdx = 0;
        var colors = _rainbowThemeColors[themeIdx];
        var gradient = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative)
        };
        for (int i = 0; i < colors.Length; i++)
            gradient.GradientStops.Add(new GradientStop(Color.Parse(colors[i]), (double)i / (colors.Length - 1)));
        bar.Background = gradient;

        var palette = this.FindControl<WrapPanel>("RainbowPaletteGroup");
        if (palette != null)
        {
            palette.Children.Clear();
            for (int i = 0; i < colors.Length; i++)
            {
                var swatch = new Border
                {
                    Width = 20, Height = 20, CornerRadius = new CornerRadius(4),
                    Background = new SolidColorBrush(Color.Parse(colors[i])),
                    BorderBrush = new SolidColorBrush(Color.Parse("#232329")),
                    BorderThickness = new Thickness(1),
                    Margin = new Thickness(1)
                };
                palette.Children.Add(swatch);
            }
        }
    }

    private void SyncSlider(string name, double val)
    {
        var s = this.FindControl<Slider>(name);
        if (s != null) s.Value = val;
    }

    private void SyncAllControls()
    {
        _suppressSliderEvents = true;
        SyncSlider("SizeSlider", _size);
        SyncSlider("ThicknessSlider", _thickness);
        SyncSlider("OpacitySlider", _opacity);
        SyncSlider("SpeedSlider", _speed);
        SyncSlider("RotationSlider", _rotation);
        SyncSlider("PosXSlider", _posX);
        SyncSlider("PosYSlider", _posY);
        UpdatePosInputs();
        SyncSlider("SpinSpeedSlider", _spinSpeed);
        SyncSlider("OrbitRadiusSlider", _orbitRadius);
        SyncSlider("OrbitSpeedSlider", _orbitSpeed);
        SyncSlider("RainbowSpeedSlider", _rainbowSpeed);

        SyncText("SizeValue", _size, "px");
        SyncText("ThicknessValue", _thickness, "px");
        SyncText("OpacityValue", _opacity, "%");
        SyncText("SpeedValue", _speed, "x");
        SyncText("RotationValue", _rotation, "");
        SyncText("PosXValue", _posX, "");
        SyncText("PosYValue", _posY, "");
        SyncText("SpinSpeedValue", _spinSpeed, "x");
        SyncText("OrbitRadiusValue", _orbitRadius, "");
        SyncText("OrbitSpeedValue", _orbitSpeed, "x");
        SyncText("RainbowSpeedValue", _rainbowSpeed, "x");

        var bindBtn = this.FindControl<Button>("BindInput");
        if (bindBtn != null) bindBtn.Content = _bindKey;
        var symInput = this.FindControl<TextBox>("SymbolInput");
        if (symInput != null) symInput.Text = _customSymbol;
        var customColor = this.FindControl<TextBox>("CustomColorInput");
        if (customColor != null) customColor.Text = _selectedColor;

        SetVisible("SpinSpeedRow", _spinEnabled);
        SetVisible("SpinDirRow", _spinEnabled);
        SetVisible("OrbitRow", _orbitEnabled);
        SetVisible("OrbitSpeedRow", _orbitEnabled);
        SetVisible("OrbitDirRow", _orbitEnabled);

        SyncButtonGroupActive("PresetGroup", _presetNames, _selectedPreset);
        SyncButtonGroupActive("AnimGroup", _animNames, _selectedAnim);
        var spinBtn = this.FindControl<Button>("SpinBtn");
        if (spinBtn != null) UpdateMotionBtn(spinBtn, _spinEnabled);
        var orbitBtn = this.FindControl<Button>("OrbitBtn");
        if (orbitBtn != null) UpdateMotionBtn(orbitBtn, _orbitEnabled);
        SyncColorButtons();
        UpdateRainbowPreview();

        var spinDirBtn = this.FindControl<Button>("SpinDirBtn");
        if (spinDirBtn != null) spinDirBtn.Content = _spinDir == "reverse" ? "Reverse" : "Normal";
        var orbitDirBtn = this.FindControl<Button>("OrbitDirBtn");
        if (orbitDirBtn != null) orbitDirBtn.Content = _orbitDir == "reverse" ? "Reverse" : "Normal";

        _suppressSliderEvents = false;
    }

    private void SyncText(string name, double val, string suffix)
    {
        var tb = SafeFind<TextBlock>(name);
        if (tb != null) { tb.Text = $"{val:0}{suffix}"; return; }
        var box = SafeFind<TextBox>(name);
        if (box != null && !box.IsFocused) box.Text = $"{val:0}{suffix}";
    }

    private void SyncButtonGroupActive(string panelName, string[] names, string current)
    {
        var panel = this.FindControl<Panel>(panelName);
        if (panel == null) return;
        foreach (var child in panel.Children)
            if (child is Button b)
            {
                if ((string)(b.Tag ?? "") == current) { b.Classes.Add("active"); b.BorderBrush = new SolidColorBrush(Color.Parse("#7c5cfc")); }
                else { b.Classes.Remove("active"); b.BorderBrush = new SolidColorBrush(Color.Parse("#232329")); }
            }
    }

    private void ApplyLive()
    {
        UpdateNotebook();
        PushToOverlay();
        StartPreviewAnimation();
    }

    private void PushToOverlay()
    {
        var palette = new System.Collections.Generic.List<string>();
        int themeIdx = Array.IndexOf(_rainbowThemeNames, _rainbowTheme);
        if (themeIdx >= 0 && themeIdx < _rainbowThemeColors.Length)
            palette.AddRange(_rainbowThemeColors[themeIdx]);

        var ch = new CrosshairState
        {
            Preset = _selectedPreset, Animation = _selectedAnim, SpinEnabled = _spinEnabled, OrbitEnabled = _orbitEnabled,
            Color = _selectedColor, Size = (int)_size, Thickness = (int)_thickness, Opacity = (int)_opacity,
            Speed = (int)_speed, Rotation = (int)_rotation, PosX = (int)_posX, PosY = (int)_posY,
            SpinSpeed = (int)_spinSpeed, SpinDir = _spinDir,
            OrbitRadius = (int)_orbitRadius, OrbitSpeed = (int)_orbitSpeed, OrbitDir = _orbitDir,
            RainbowEnabled = _rainbowEnabled, RainbowSpeed = _rainbowSpeed, RainbowTheme = _rainbowTheme,
            RainbowPalette = palette,
            BindKey = _bindKey, Symbol = _customSymbol
        };
        _overlayDispatcher?.Invoke(() => _overlayWindow?.UpdateCrosshair(ch));
    }

    private void SetupButtonGroup(string panelName, string[] names, string[] labels, string current, Action<string> onSelect)
    {
        var panel = this.FindControl<Panel>(panelName);
        if (panel == null) return;
        for (int i = 0; i < names.Length; i++)
        {
            var name = names[i];
            var btn = new Button { Content = labels[i], MinWidth = 42, Height = 28, Tag = name, FontSize = 11 };
            btn.Classes.Add("pill");
            if (name == current) { btn.Classes.Add("active"); btn.BorderBrush = new SolidColorBrush(Color.Parse("#7c5cfc")); }
            btn.Click += (_, _) =>
            {
                onSelect(name);
                foreach (var child in panel.Children)
                    if (child is Button b)
                    {
                        if ((string)(b.Tag ?? "") == name) { b.Classes.Add("active"); b.BorderBrush = new SolidColorBrush(Color.Parse("#7c5cfc")); }
                        else { b.Classes.Remove("active"); b.BorderBrush = new SolidColorBrush(Color.Parse("#232329")); }
                    }
            };
            panel.Children.Add(btn);
        }
    }


    private void RebuildAnimGroup()
    {
        var panel = this.FindControl<Panel>("AnimGroup");
        if (panel == null) return;
        panel.Children.Clear();

        for (int i = 0; i < _animNames.Length; i++)
        {
            var tag = _animNames[i];
            var btn = new Button { Content = tag, MinWidth = 42, Height = 28, Tag = tag, FontSize = 11 };
            btn.Classes.Add("pill");
            if (tag == _selectedAnim) { btn.Classes.Add("active"); btn.BorderBrush = new SolidColorBrush(Color.Parse("#7c5cfc")); }
            else { btn.BorderBrush = new SolidColorBrush(Color.Parse("#232329")); }
            btn.Click += (_, _) =>
            {
                _selectedAnim = tag;
                foreach (var child in panel.Children)
                    if (child is Button b)
                    {
                        if ((string)(b.Tag ?? "") == tag) { b.Classes.Add("active"); b.BorderBrush = new SolidColorBrush(Color.Parse("#7c5cfc")); }
                        else { b.Classes.Remove("active"); b.BorderBrush = new SolidColorBrush(Color.Parse("#232329")); }
                    }
                ApplyLive();
            };
            panel.Children.Add(btn);
        }
    }

    private void RebuildRainbowThemes()
    {
        var presetsDir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "presets", "crosshair", "animpresets");
        if (!Directory.Exists(presetsDir)) return;

        var names = new System.Collections.Generic.List<string>();
        var labels = new System.Collections.Generic.List<string>();
        var colors = new System.Collections.Generic.List<string[]>();

        foreach (var file in Directory.GetFiles(presetsDir, "*.json"))
        {
            if (System.IO.Path.GetFileName(file).Equals("TEMPLATE.json", StringComparison.OrdinalIgnoreCase))
                continue;

            try
            {
                var json = File.ReadAllText(file);
                var obj = Newtonsoft.Json.JsonConvert.DeserializeObject<AnimPreset>(json);
                if (obj != null && !string.IsNullOrEmpty(obj.Name))
                {
                    names.Add(obj.Name.ToLower());
                    labels.Add(obj.Name);
                    colors.Add(obj.Palette.ToArray());
                }
            }
            catch { }
        }

        _rainbowThemeNames = names.ToArray();
        _rainbowThemeLabels = labels.ToArray();
        _rainbowThemeColors = colors.ToArray();

        var themePanel = this.FindControl<WrapPanel>("RainbowThemeGroup");
        if (themePanel == null) return;
        themePanel.Children.Clear();
        for (int i = 0; i < _rainbowThemeNames.Length; i++)
        {
            var name = _rainbowThemeNames[i];
            var label = _rainbowThemeLabels[i];
            var btn = new Button
            {
                Content = label, MinWidth = 50, Height = 24, Tag = name, FontSize = 10, Classes = { "pill" },
                BorderBrush = new SolidColorBrush(Color.Parse(name == _rainbowTheme ? "#7c5cfc" : "#232329"))
            };
            btn.Click += (_, _) =>
            {
                _rainbowTheme = name;
                foreach (var c in themePanel.Children)
                    if (c is Button tb) tb.BorderBrush = new SolidColorBrush(Color.Parse((string)(tb.Tag ?? "") == name ? "#7c5cfc" : "#232329"));
                UpdateRainbowPreview();
                ApplyLive();
            };
            themePanel.Children.Add(btn);
        }

        if (!Array.Exists(_rainbowThemeNames, n => n == _rainbowTheme) && _rainbowThemeNames.Length > 0)
            _rainbowTheme = _rainbowThemeNames[0];

        UpdateRainbowPreview();
    }

    private void UpdatePosInputs()
    {
        var xBox = SafeFind<TextBox>("PosXValue");
        var yBox = SafeFind<TextBox>("PosYValue");
        if (xBox != null && !xBox.IsFocused) xBox.Text = _posX.ToString("0");
        if (yBox != null && !yBox.IsFocused) yBox.Text = _posY.ToString("0");
    }

    private T? SafeFind<T>(string name) where T : class
    {
        if (string.IsNullOrEmpty(name)) return null;
        if (this.Name == name && this is T self) return self;
        foreach (var d in this.GetVisualDescendants())
            if (d is Control c && c.Name == name)
                return c as T;
        return null;
    }

    private void SetupSlider(string sliderName, string valueName, double initial, Action<double> onChange, string suffix)
    {
        var slider = this.FindControl<Slider>(sliderName);
        var valueBlock = SafeFind<TextBlock>(valueName);
        var valueBox = SafeFind<TextBox>(valueName);
        if (slider == null) return;
        slider.ValueChanged += (_, _) =>
        {
            if (_suppressSliderEvents) return;
            onChange(slider.Value);
            if (valueBlock != null) valueBlock.Text = $"{slider.Value:0}{suffix}";
            else if (valueBox != null && !valueBox.IsFocused) valueBox.Text = $"{slider.Value:0}{suffix}";
        };
        slider.Value = initial;
        if (valueBlock != null) valueBlock.Text = $"{initial:0}{suffix}";
        else if (valueBox != null) valueBox.Text = $"{initial:0}{suffix}";

        if (valueBox != null)
        {
            valueBox.KeyDown += (_, e) =>
            {
                if (e.Key != Key.Enter) return;
                if (double.TryParse(valueBox.Text, out var parsed))
                {
                    _suppressSliderEvents = true;
                    slider.Value = Math.Clamp(parsed, slider.Minimum, slider.Maximum);
                    _suppressSliderEvents = false;
                    onChange(slider.Value);
                }
                valueBox.Text = $"{slider.Value:0}{suffix}";
            };
        }
    }

    private void UpdateNotebook()
    {
        SetText("NbPreset", $"Preset: {Capitalize(_selectedPreset)}");
        SetText("NbAnimation", $"Animation: {Capitalize(_selectedAnim)}");
        var motions = new List<string>();
        if (_spinEnabled) motions.Add("Spin");
        if (_orbitEnabled) motions.Add("Orbit");
        SetText("NbMotion", $"Motion: {(motions.Count > 0 ? string.Join(" + ", motions) : "None")}");
        SetText("NbSize", $"Size: {_size:0}px");
        SetText("NbThickness", $"Thickness: {_thickness:0}px");
        SetText("NbOpacity", $"Opacity: {_opacity:0}%");
        SetText("NbColor", $"Color: {_selectedColor}");
        SetText("NbSpeed", $"Speed: {_speed:0}x");
        SetText("NbPos", $"Position: X {_posX:0} / Y {_posY:0}");
        SetText("NbRotation", $"Rotation: {_rotation:0}");
        SetText("NbBind", $"Bind: {_bindKey}");
    }

    private void SetText(string name, string text)
    {
        var tb = this.FindControl<TextBlock>(name);
        if (tb != null) tb.Text = text;
    }

    private void StartPreviewAnimation()
    {
        _previewTimer?.Stop();
        bool needsAnim = _selectedAnim != "none" || _spinEnabled || _orbitEnabled || _rainbowEnabled;
        if (!needsAnim) { _previewTimer = null; DrawPreview(); return; }
        _animPhase = 0;
        _previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _previewTimer.Tick += (_, _) => { _animPhase += 0.016; DrawPreview(); };
        _previewTimer.Start();
        DrawPreview();
    }

    private void DrawPreview()
    {
        if (_canvas == null) return;
        _canvas.Children.Clear();

        const double center = 150.0;
        double cx = center + _posX;
        double cy = center + _posY;

        double animScale = 1.0;
        double animOpacity = _opacity / 100.0;
        double extraRotation = 0;
        double orbitX = 0, orbitY = 0;
        Color currentColor = Color.Parse(_selectedColor);

        double speedFactor = Math.Max(_speed, 1);

        if (_selectedAnim != "none")
        {
            double animDuration = _selectedAnim switch
            {
                "breathe" => 3.0,
                "scale" => 1.5,
                _ => 10.0 / speedFactor
            };
            double phase = (_animPhase / animDuration) * Math.PI * 2;
            double t = (Math.Sin(phase) + 1.0) / 2.0;

            switch (_selectedAnim)
            {
                case "pulse":
                    animScale = 1.0 + t * 0.15;
                    break;
                case "scale":
                    animScale = 1.0 + t * 0.5;
                    break;
                case "glow":
                    animOpacity = (_opacity / 100.0) * (1.0 - t * 0.3);
                    break;
            }
        }

        if (_rainbowEnabled)
        {
            double rainbowDur = 3.0 / Math.Max(_rainbowSpeed, 1);
            double hue = (_animPhase / rainbowDur) * 360.0;
            currentColor = GetRainbowColor(hue % 360.0);
        }

        if (_spinEnabled)
        {
            double spinDur = 10.0 / Math.Max(_spinSpeed, 1);
            double spinPhase = (_animPhase / spinDur) * 360.0;
            extraRotation = _spinDir == "reverse" ? -spinPhase : spinPhase;
        }
        if (_orbitEnabled)
        {
            double orbitDur = 10.0 / Math.Max(_orbitSpeed, 1);
            double orbitPhase = (_animPhase / orbitDur) * Math.PI * 2;
            double r = _orbitRadius;
            if (_orbitDir == "reverse") orbitPhase = -orbitPhase;
            orbitX = Math.Cos(orbitPhase) * r;
            orbitY = Math.Sin(orbitPhase) * r;
        }

        cx += orbitX;
        cy += orbitY;

        double totalRotation = _rotation + extraRotation;
        double sz = _size * animScale;
        double th = _thickness;
        var brush = new SolidColorBrush(currentColor);

        switch (_selectedPreset)
        {
            case "dot":
            {
                var el = new Ellipse { Width = sz, Height = sz, Fill = brush, Opacity = animOpacity };
                Canvas.SetLeft(el, cx - sz / 2);
                Canvas.SetTop(el, cy - sz / 2);
                ApplyRotation(el, cx, cy, totalRotation);
                _canvas.Children.Add(el);
                break;
            }
            case "cross":
            {
                var h = new Line { StartPoint = new Point(cx - sz, cy), EndPoint = new Point(cx + sz, cy), Stroke = brush, StrokeThickness = th, Opacity = animOpacity };
                var v = new Line { StartPoint = new Point(cx, cy - sz), EndPoint = new Point(cx, cy + sz), Stroke = brush, StrokeThickness = th, Opacity = animOpacity };
                ApplyRotation(h, cx, cy, totalRotation);
                ApplyRotation(v, cx, cy, totalRotation);
                _canvas.Children.Add(h);
                _canvas.Children.Add(v);
                break;
            }
            case "circle":
            {
                var el = new Ellipse { Width = sz * 2, Height = sz * 2, Stroke = brush, StrokeThickness = th, Opacity = animOpacity };
                Canvas.SetLeft(el, cx - sz);
                Canvas.SetTop(el, cy - sz);
                ApplyRotation(el, cx, cy, totalRotation);
                _canvas.Children.Add(el);
                break;
            }
            case "bracket":
            {
                double bs = sz * 0.6;
                var pts = new (Point f, Point t)[] {
                    (new(cx - bs, cy - bs), new(cx + bs, cy - bs)),
                    (new(cx - bs, cy + bs), new(cx + bs, cy + bs)),
                    (new(cx - bs, cy - bs), new(cx - bs, cy + bs)),
                    (new(cx + bs, cy - bs), new(cx + bs, cy + bs))
                };
                foreach (var (f, t) in pts)
                {
                    var l = new Line { StartPoint = f, EndPoint = t, Stroke = brush, StrokeThickness = th, Opacity = animOpacity };
                    ApplyRotation(l, cx, cy, totalRotation);
                    _canvas.Children.Add(l);
                }
                break;
            }
            case "tactical":
            {
                var pts = new (Point f, Point t)[] {
                    (new(cx, cy - sz), new(cx, cy + sz)),
                    (new(cx - sz, cy), new(cx - th * 2, cy)),
                    (new(cx + th * 2, cy), new(cx + sz, cy))
                };
                foreach (var (f, t) in pts)
                {
                    var l = new Line { StartPoint = f, EndPoint = t, Stroke = brush, StrokeThickness = th, Opacity = animOpacity };
                    ApplyRotation(l, cx, cy, totalRotation);
                    _canvas.Children.Add(l);
                }
                break;
            }
            case "custom":
            {
                var el = new TextBlock { Text = _customSymbol, FontSize = sz * 2.5, Foreground = brush, Opacity = animOpacity };
                el.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                Canvas.SetLeft(el, cx - el.DesiredSize.Width / 2);
                Canvas.SetTop(el, cy - el.DesiredSize.Height / 2);
                ApplyRotation(el, cx, cy, totalRotation);
                _canvas.Children.Add(el);
                break;
            }
        }
    }

    private Color GetRainbowColor(double hue)
    {
        return _rainbowTheme switch
        {
            "warm" => LerpRainbow(hue, new (double, string)[] { (0, "#ff0000"), (60, "#ffff00"), (120, "#ff8800"), (360, "#ff0000") }),
            "cool" => LerpRainbow(hue, new (double, string)[] { (0, "#0044ff"), (120, "#00ffff"), (240, "#0088ff"), (360, "#0044ff") }),
            "neon" => LerpRainbow(hue, new (double, string)[] { (0, "#ff00ff"), (90, "#00ffff"), (180, "#88ff00"), (270, "#ffff00"), (360, "#ff00ff") }),
            "pastel" => LerpRainbow(hue, new (double, string)[] { (0, "#ffcccc"), (72, "#ffffcc"), (144, "#ccffcc"), (216, "#ccffff"), (288, "#ccccff"), (360, "#ffcccc") }),
            _ => HslToRgb(hue, 1.0, 0.5)
        };
    }

    private static Color LerpRainbow(double hue, (double h, string color)[] stops)
    {
        for (int i = 0; i < stops.Length - 1; i++)
        {
            if (hue >= stops[i].h && hue <= stops[i + 1].h)
            {
                double t = (hue - stops[i].h) / (stops[i + 1].h - stops[i].h);
                var c1 = Color.Parse(stops[i].color);
                var c2 = Color.Parse(stops[i + 1].color);
                return Color.FromRgb(
                    (byte)(c1.R + (c2.R - c1.R) * t),
                    (byte)(c1.G + (c2.G - c1.G) * t),
                    (byte)(c1.B + (c2.B - c1.B) * t));
            }
        }
        return Color.Parse(stops[0].color);
    }

    private static void ApplyRotation(Control element, double centerX, double centerY, double angle)
    {
        if (Math.Abs(angle) < 0.01) return;
        element.RenderTransformOrigin = new RelativePoint(new Point(centerX, centerY), RelativeUnit.Absolute);
        element.RenderTransform = new RotateTransform(angle);
    }

    private void ResetDefaults()
    {
        var def = new CrosshairState();
        _selectedPreset = def.Preset; _selectedAnim = def.Animation; _spinEnabled = def.SpinEnabled; _orbitEnabled = def.OrbitEnabled;
        _selectedColor = def.Color; _size = def.Size; _thickness = def.Thickness; _opacity = def.Opacity;
        _speed = def.Speed; _rotation = def.Rotation; _posX = def.PosX; _posY = def.PosY;
        _spinSpeed = def.SpinSpeed; _spinDir = def.SpinDir;
        _orbitRadius = def.OrbitRadius; _orbitSpeed = def.OrbitSpeed; _orbitDir = def.OrbitDir;
        _rainbowSpeed = def.RainbowSpeed; _rainbowTheme = def.RainbowTheme;
        _bindKey = def.BindKey; _customSymbol = def.Symbol;
    }

    private void SaveSettings()
    {
        var ch = new CrosshairState
        {
            Preset = _selectedPreset, Animation = _selectedAnim, SpinEnabled = _spinEnabled, OrbitEnabled = _orbitEnabled,
            Color = _selectedColor, Size = (int)_size, Thickness = (int)_thickness, Opacity = (int)_opacity,
            Speed = (int)_speed, Rotation = (int)_rotation, PosX = (int)_posX, PosY = (int)_posY,
            SpinSpeed = (int)_spinSpeed, SpinDir = _spinDir,
            OrbitRadius = (int)_orbitRadius, OrbitSpeed = (int)_orbitSpeed, OrbitDir = _orbitDir,
            RainbowEnabled = _rainbowEnabled, RainbowSpeed = _rainbowSpeed, RainbowTheme = _rainbowTheme,
            BindKey = _bindKey, Symbol = _customSymbol
        };
        _settings.SaveCrosshair(ch);
    }

    private static Color HslToRgb(double h, double s, double l)
    {
        h /= 360;
        double r, g, b;
        if (s == 0) { r = g = b = l; }
        else
        {
            double q = l < 0.5 ? l * (1 + s) : l + s - l * s;
            double p = 2 * l - q;
            r = HueToRgb(p, q, h + 1.0 / 3);
            g = HueToRgb(p, q, h);
            b = HueToRgb(p, q, h - 1.0 / 3);
        }
        return Color.FromRgb((byte)(r * 255), (byte)(g * 255), (byte)(b * 255));
    }

    private static double HueToRgb(double p, double q, double t)
    {
        if (t < 0) t += 1;
        if (t > 1) t -= 1;
        if (t < 1.0 / 6) return p + (q - p) * 6 * t;
        if (t < 1.0 / 2) return q;
        if (t < 2.0 / 3) return p + (q - p) * (2.0 / 3 - t) * 6;
        return p;
    }

    private static string Capitalize(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0]) + s[1..];
}
