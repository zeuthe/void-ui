using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
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
    private double _size = 16, _thickness = 3, _opacity = 100, _speed = 5, _rotation = 0, _posX = 0, _posY = 0;
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

    private readonly string[] _presetNames = { "dot", "cross", "circle", "bracket", "custom" };
    private readonly string[] _presetLabels = { ".", "+", "o", "[ ]", "S" };
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

    // Превью строится один раз при изменении настроек; кадры анимации двигают
    // только трансформы/прозрачность/цвет — без пересборки визуального дерева.
    private Canvas? _previewShapes;
    private CrosshairState? _previewState;
    private TransformGroup? _previewTg;
    private RotateTransform? _previewRotate;
    private ScaleTransform? _previewScale;
    private readonly List<SolidColorBrush> _previewBrushes = new();
    private byte[] _previewPaletteRgb = Array.Empty<byte>();
    private int _previewPaletteCount;
    private readonly System.Diagnostics.Stopwatch _animClock = new();

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
        // При смене вкладки страница выбрасывается, но DispatcherTimer держит её в памяти
        // и крутит 60 FPS уже для чужого кадра — останавливаем таймер при выходе из дерева.
        AttachedToVisualTree += (_, _) => StartPreviewAnimation();
        DetachedFromVisualTree += (_, _) => { _previewTimer?.Stop(); _previewTimer = null; };
        var ch = _settings.GetSettings().Crosshair ?? new CrosshairState();
        _selectedPreset = ch.Preset ?? "dot";
        _selectedAnim = ch.Animation ?? "none";
        _spinEnabled = ch.SpinEnabled;
        _orbitEnabled = ch.OrbitEnabled;
        _selectedColor = ch.Color ?? "#22c55e";
        _size = ch.Size; _thickness = Math.Max(ch.Thickness, 1); _opacity = ch.Opacity;
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
            var ch = BuildCurrentState();
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
            var t = text.Trim().Trim('"');
            CrosshairState? ch = null;
            // 1) share code (base64)  2) plain JSON (copied from a file / Save->Copy)
            try
            {
                var json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(t));
                ch = Newtonsoft.Json.JsonConvert.DeserializeObject<CrosshairState>(json);
            }
            catch { ch = null; }
            if (ch == null)
            {
                try { ch = Newtonsoft.Json.JsonConvert.DeserializeObject<CrosshairState>(t); }
                catch { ch = null; }
            }
            if (ch == null) { ShowConfigMsg("Invalid config"); return; }

            var incoming = NormalizeState(ch);
            var same = SameState(incoming, BuildCurrentState());
            try
            {
                if (!same && topLevel is Window owner)
                {
                    ShowConfigMsg("Confirming paste...");
                    var ok = await ConfirmPasteAsync(owner);
                    if (!ok) { ShowConfigMsg("Paste cancelled"); return; }
                }
                ApplyCrosshairState(incoming);
                ShowConfigMsg(same ? "Applied (identical)" : "Applied!");
            }
            catch (Exception ex) { ShowConfigMsg("Paste error: " + ex.Message); }
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

            var ch = BuildCurrentState();

            var json = Newtonsoft.Json.JsonConvert.SerializeObject(ch, Newtonsoft.Json.Formatting.Indented);
            File.WriteAllText(path, json);

            ShowConfigStatus($"Saved: {configName}");
            RefreshConfigsList();
        });

        SetupButton("CopyConfigBtn", () =>
        {
            var ch = BuildCurrentState();
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

    private async Task<bool> ConfirmPasteAsync(Window owner)
    {
        var win = new Window
        {
            Width = 340,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Brushes.Transparent,
            SystemDecorations = SystemDecorations.None,
            CanResize = false,
            ShowInTaskbar = false,
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent }
        };

        var card = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#16161c")),
            BorderBrush = new SolidColorBrush(Color.Parse("#2a2a34")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(20),
            BoxShadow = BoxShadows.Parse("0 12 32 0 #55000000")
        };

        var title = new TextBlock
        {
            Text = "Replace current settings?",
            Foreground = new SolidColorBrush(Color.Parse("#ededef")),
            FontSize = 14,
            FontWeight = FontWeight.SemiBold
        };
        var body = new TextBlock
        {
            Text = "The pasted crosshair differs from the active one. Apply it and save?",
            Foreground = new SolidColorBrush(Color.Parse("#a5a5b0")),
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 0)
        };

        var replaceBtn = new Button
        {
            Content = "Replace",
            Width = 100,
            Height = 32,
            Background = new SolidColorBrush(Color.Parse("#7c5cfc")),
            Foreground = Brushes.White,
            CornerRadius = new CornerRadius(9),
            BorderThickness = new Thickness(0),
            FontSize = 11,
            FontWeight = FontWeight.SemiBold,
            Cursor = new Cursor(StandardCursorType.Hand)
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
            Cursor = new Cursor(StandardCursorType.Hand)
        };

        var btnRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        btnRow.Children.Add(cancelBtn);
        btnRow.Children.Add(replaceBtn);

        var panel = new StackPanel { Spacing = 4 };
        panel.Children.Add(title);
        panel.Children.Add(body);
        panel.Children.Add(btnRow);
        card.Child = panel;
        win.Content = card;

        replaceBtn.Click += (_, _) => win.Close(true);
        cancelBtn.Click += (_, _) => win.Close(false);
        win.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) win.Close(false);
            else if (e.Key == Key.Enter) win.Close(true);
        };

        return await win.ShowDialog<bool>(owner);
    }

    private CrosshairState BuildCurrentState() => new CrosshairState
    {
        Preset = _selectedPreset, Animation = _selectedAnim,
        SpinEnabled = _spinEnabled, OrbitEnabled = _orbitEnabled,
        Color = _selectedColor, Size = (int)_size, Thickness = _thickness, Opacity = (int)_opacity,
        Speed = (int)_speed, Rotation = (int)_rotation, PosX = (int)_posX, PosY = (int)_posY,
        SpinSpeed = (int)_spinSpeed, SpinDir = _spinDir,
        OrbitRadius = (int)_orbitRadius, OrbitSpeed = (int)_orbitSpeed, OrbitDir = _orbitDir,
        RainbowEnabled = _rainbowEnabled, RainbowSpeed = _rainbowSpeed, RainbowTheme = _rainbowTheme,
        BindKey = _bindKey, Symbol = _customSymbol
    };

    // Fills the same defaults ApplyCrosshairState uses, so a partial config
    // deserializes to the exact state it would end up in after applying.
    private static CrosshairState NormalizeState(CrosshairState ch) => new CrosshairState
    {
        Preset = ch.Preset ?? "dot", Animation = ch.Animation ?? "none",
        SpinEnabled = ch.SpinEnabled, OrbitEnabled = ch.OrbitEnabled,
        Color = ch.Color ?? "#22c55e", Size = ch.Size, Thickness = Math.Max(ch.Thickness, 1),
        Opacity = ch.Opacity, Speed = ch.Speed, Rotation = ch.Rotation,
        PosX = ch.PosX, PosY = ch.PosY,
        SpinSpeed = ch.SpinSpeed, SpinDir = ch.SpinDir ?? "normal",
        OrbitRadius = ch.OrbitRadius, OrbitSpeed = ch.OrbitSpeed, OrbitDir = ch.OrbitDir ?? "normal",
        RainbowEnabled = ch.RainbowEnabled, RainbowSpeed = ch.RainbowSpeed,
        RainbowTheme = ch.RainbowTheme ?? "full",
        BindKey = ch.BindKey ?? "F6", Symbol = ch.Symbol ?? "+"
    };

    private static bool SameState(CrosshairState a, CrosshairState b) =>
        Newtonsoft.Json.JsonConvert.SerializeObject(a) == Newtonsoft.Json.JsonConvert.SerializeObject(b);

    private void ApplyCrosshairState(CrosshairState ch)
    {
        _selectedPreset = ch.Preset ?? "dot";
        _selectedAnim = ch.Animation ?? "none";
        _spinEnabled = ch.SpinEnabled;
        _orbitEnabled = ch.OrbitEnabled;
        _selectedColor = ch.Color ?? "#22c55e";
        _size = ch.Size; _thickness = Math.Max(ch.Thickness, 1); _opacity = ch.Opacity;
        _speed = ch.Speed; _rotation = ch.Rotation;
        _posX = ch.PosX; _posY = ch.PosY;
        _spinSpeed = ch.SpinSpeed; _spinDir = ch.SpinDir ?? "normal";
        _orbitRadius = ch.OrbitRadius; _orbitSpeed = ch.OrbitSpeed; _orbitDir = ch.OrbitDir ?? "normal";
        _rainbowEnabled = ch.RainbowEnabled;
        _rainbowSpeed = ch.RainbowSpeed; _rainbowTheme = ch.RainbowTheme ?? "full";
        _bindKey = ch.BindKey ?? "F6";
        _customSymbol = ch.Symbol ?? "+";
        SyncAllControls();
        ApplyLive();
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
        if (tb != null) { tb.Text = $"{val:0.#}{suffix}"; return; }
        var box = SafeFind<TextBox>(name);
        if (box != null && !box.IsFocused) box.Text = $"{val:0.#}{suffix}";
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
        SaveSettings();
    }

    private void PushToOverlay()
    {
        var ch = BuildCurrentState();
        ch.RainbowPalette = BuildPalette();
        _overlayDispatcher?.Invoke(() => _overlayWindow?.UpdateCrosshair(ch));
    }

    /// <summary>Палитра rainbow для текущей темы — один источник и для превью, и для оверлея.</summary>
    private List<string> BuildPalette()
    {
        var palette = new List<string>();
        int themeIdx = Array.IndexOf(_rainbowThemeNames, _rainbowTheme);
        if (themeIdx >= 0 && themeIdx < _rainbowThemeColors.Length)
            palette.AddRange(_rainbowThemeColors[themeIdx]);
        return palette;
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
            if (valueBlock != null) valueBlock.Text = $"{slider.Value:0.#}{suffix}";
            else if (valueBox != null && !valueBox.IsFocused) valueBox.Text = $"{slider.Value:0.#}{suffix}";
        };
        slider.Value = initial;
        if (valueBlock != null) valueBlock.Text = $"{initial:0.#}{suffix}";
        else if (valueBox != null) valueBox.Text = $"{initial:0.#}{suffix}";

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
                valueBox.Text = $"{slider.Value:0.#}{suffix}";
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
        SetText("NbThickness", $"Thickness: {_thickness:0.#}px");
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
        _previewTimer = null;
        _animClock.Restart();
        _animPhase = 0;

        RebuildPreview();   // форма строится один раз
        PreviewFrame();     // кадр на t=0 (он же финальный, если анимации нет)

        var s = _previewState;
        bool needsAnim = s != null && (s.Animation != "none" || s.SpinEnabled || s.OrbitEnabled || s.RainbowEnabled);
        if (!needsAnim) return;

        _previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _previewTimer.Tick += (_, _) =>
        {
            if (!IsVisible) { _previewTimer?.Stop(); _previewTimer = null; return; }
            _animPhase = _animClock.Elapsed.TotalSeconds;
            PreviewFrame();
        };
        _previewTimer.Start();
    }

    /// <summary>Строит фигуры превью один раз — при изменении настроек, а не каждый кадр.</summary>
    private void RebuildPreview()
    {
        if (_canvas == null) return;
        _canvas.Children.Clear();
        _previewBrushes.Clear();
        _previewShapes = null;
        _previewTg = null;
        _previewRotate = null;
        _previewScale = null;

        var state = BuildCurrentState();
        _previewState = state;

        // Та же палитра, что уходит в оверлей.
        var palette = BuildPalette();
        _previewPaletteCount = palette.Count;
        _previewPaletteRgb = new byte[palette.Count * 3];
        for (int i = 0; i < palette.Count; i++)
        {
            var pc = Color.Parse(palette[i]);
            _previewPaletteRgb[i * 3] = pc.R;
            _previewPaletteRgb[i * 3 + 1] = pc.G;
            _previewPaletteRgb[i * 3 + 2] = pc.B;
        }

        const double center = 150.0;
        double posX = center + state.PosX;
        double posY = center + state.PosY;
        // Орбита запекается в координаты фигур (эквивалент TranslateTransform(r,0) в оверлее),
        // а общий поворот вокруг центра гонит фигуры по кругу.
        double cx = posX + (state.OrbitEnabled ? state.OrbitRadius : 0);
        double cy = posY;

        var brush = new SolidColorBrush(Color.Parse(state.Color));
        _previewBrushes.Add(brush);

        double t = CrosshairGeometry.Thickness(state.Thickness);
        double sz = state.Size;

        var shapes = new Canvas { Width = 300, Height = 300 };

        switch (state.Preset)
        {
            case "cross":
            {
                var bars = new List<Bar>(2);
                CrosshairGeometry.CrossBars(cx, cy, sz, t, bars);
                AddBars(shapes, bars, brush);
                break;
            }
            case "bracket":
            {
                var bars = new List<Bar>(8);
                CrosshairGeometry.BracketBars(cx, cy, sz, t, bars);
                AddBars(shapes, bars, brush);
                break;
            }
            case "circle":
            {
                double d = CrosshairGeometry.CircleDiameter(sz);
                var el = new Ellipse { Width = d, Height = d, Stroke = brush, StrokeThickness = t };
                Canvas.SetLeft(el, Snap(cx - d / 2.0));
                Canvas.SetTop(el, Snap(cy - d / 2.0));
                shapes.Children.Add(el);
                break;
            }
            case "custom":
            {
                var el = new TextBlock
                {
                    Text = _customSymbol,
                    FontSize = CrosshairGeometry.SymbolFontSize(sz),
                    Foreground = brush
                };
                el.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                Canvas.SetLeft(el, Snap(cx - el.DesiredSize.Width / 2.0));
                Canvas.SetTop(el, Snap(cy - el.DesiredSize.Height / 2.0));
                shapes.Children.Add(el);
                break;
            }
            case "dot":
            default:
            {
                double d = CrosshairGeometry.DotSize(sz);
                var el = new Ellipse { Width = d, Height = d, Fill = brush };
                Canvas.SetLeft(el, Snap(cx - d / 2.0));
                Canvas.SetTop(el, Snap(cy - d / 2.0));
                shapes.Children.Add(el);
                break;
            }
        }

        // Точка вращения — центр прицела (без орбитального смещения), как в оверлее.
        _previewRotate = new RotateTransform();
        _previewScale = new ScaleTransform(1, 1);
        _previewTg = new TransformGroup();
        _previewTg.Children.Add(_previewRotate);   // точка: сначала масштаб, потом поворот
        _previewTg.Children.Add(_previewScale);
        shapes.RenderTransformOrigin = new RelativePoint(posX, posY, RelativeUnit.Absolute);
        shapes.RenderTransform = _previewTg;

        _previewShapes = shapes;
        _canvas.Children.Add(shapes);
    }

    private void AddBars(Canvas shapes, List<Bar> bars, IBrush brush)
    {
        foreach (var b in bars)
        {
            var r = new Rectangle { Width = Snap(b.W), Height = Snap(b.H), Fill = brush };
            Canvas.SetLeft(r, Snap(b.X));
            Canvas.SetTop(r, Snap(b.Y));
            shapes.Children.Add(r);
        }
    }

    /// <summary>Один кадр анимации: двигает только трансформы, прозрачность и цвет.</summary>
    private void PreviewFrame()
    {
        var s = _previewState;
        var shapes = _previewShapes;
        if (s == null || shapes == null || _previewTg == null || _previewRotate == null || _previewScale == null) return;

        double t = _animPhase;
        double animScale = 1.0;
        double animOpacity = s.Opacity / 100.0;

        if (s.Animation != "none")
        {
            double dur = s.Animation == "scale"
                ? CrosshairGeometry.ScaleDuration
                : CrosshairGeometry.AnimDuration(s.Speed);
            double u = CrosshairGeometry.Triangle(t, dur);
            switch (s.Animation)
            {
                case "pulse":
                    animScale = CrosshairGeometry.PulseFrom + u * (CrosshairGeometry.PulseTo - CrosshairGeometry.PulseFrom);
                    break;
                case "scale":
                    animScale = CrosshairGeometry.ScaleFrom + u * (CrosshairGeometry.ScaleTo - CrosshairGeometry.ScaleFrom);
                    break;
                case "glow":
                    animOpacity = (s.Opacity / 100.0) * (1.0 + u * (CrosshairGeometry.GlowDim - 1.0));
                    break;
            }
        }

        // Один угол = статичный поворот + спин + орбита: в оверлее всё это тоже
        // сводится к поворотам вокруг одной точки, поэтому картинка совпадает.
        double angle = s.Rotation;
        if (s.SpinEnabled)
        {
            double phase = (t / CrosshairGeometry.SpinDuration(s.SpinSpeed)) * 360.0;
            angle += s.SpinDir == "reverse" ? -phase : phase;
        }
        if (s.OrbitEnabled)
        {
            double phase = (t / CrosshairGeometry.OrbitDuration(s.OrbitSpeed)) * 360.0;
            angle += s.OrbitDir == "reverse" ? -phase : phase;
        }

        if (s.RainbowEnabled && _previewPaletteCount > 0)
        {
            CrosshairGeometry.PaletteColor(_previewPaletteRgb, _previewPaletteCount, t,
                CrosshairGeometry.RainbowCycleDuration(s.RainbowSpeed), out byte rr, out byte gg, out byte bb);
            var c = Color.FromRgb(rr, gg, bb);
            foreach (var b in _previewBrushes) b.Color = c;
        }

        shapes.Opacity = animOpacity;
        _previewRotate.Angle = angle;
        _previewScale.ScaleX = _previewScale.ScaleY = animScale;

        // Идентичный трансформ выключаем: без него тонкие линии остаются чёткими.
        double norm = angle % 360.0;
        if (norm < -180.0) norm += 360.0;
        else if (norm > 180.0) norm -= 360.0;
        bool identity = Math.Abs(norm) < 0.01 && Math.Abs(animScale - 1.0) < 0.0001;
        shapes.RenderTransform = identity ? null : _previewTg;
    }

    /// <summary>Привязка к границе физического пикселя с учётом DPI (как в оверлее).</summary>
    private double Snap(double v)
    {
        double scale = 1.0;
        try { scale = TopLevel.GetTopLevel(_canvas)?.RenderScaling ?? 1.0; } catch { }
        if (scale <= 0) scale = 1.0;
        return Math.Round(v * scale, MidpointRounding.AwayFromZero) / scale;
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
            Color = _selectedColor, Size = (int)_size, Thickness = _thickness, Opacity = (int)_opacity,
            Speed = (int)_speed, Rotation = (int)_rotation, PosX = (int)_posX, PosY = (int)_posY,
            SpinSpeed = (int)_spinSpeed, SpinDir = _spinDir,
            OrbitRadius = (int)_orbitRadius, OrbitSpeed = (int)_orbitSpeed, OrbitDir = _orbitDir,
            RainbowEnabled = _rainbowEnabled, RainbowSpeed = _rainbowSpeed, RainbowTheme = _rainbowTheme,
            BindKey = _bindKey, Symbol = _customSymbol
        };
        _settings.SaveCrosshair(ch);
    }

    private static string Capitalize(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0]) + s[1..];
}
