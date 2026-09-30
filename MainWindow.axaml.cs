using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using VoidUI.Services;

namespace VoidUI;

public partial class MainWindow : Window
{
    private readonly SettingsService _settings;
    private readonly SteamService _steamService;
    private readonly CombatService _combatService;

    private string _currentTheme = "black";

    private OverlayWindow? _overlayWindow;
    private System.Windows.Threading.Dispatcher? _overlayDispatcher;
    private System.Threading.Thread? _overlayThread;
    private System.Threading.ManualResetEventSlim? _overlayReady;
    private System.Timers.Timer? _themeWatcher;

    public MainWindow()
    {
        _settings = new SettingsService();
        _steamService = new SteamService();
        _combatService = new CombatService(_steamService);

        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        SetupDrag();
        WireTabButtons();
        WireControlButtons();
        WireSearch();
        DetectSteam();
        ApplyTheme(_settings.GetSettings().Theme ?? "black");
        NavigateTo("home");
        StartOverlay();
        StartThemeWatcher();
    }

    #region Drag

    private void SetupDrag()
    {
        var topBar = this.FindControl<Border>("TopBar");
        if (topBar == null) return;
        topBar.PointerPressed += (s, e) =>
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                BeginMoveDrag(e);
        };
    }

    #endregion

    #region Tab Navigation

    private void WireTabButtons()
    {
        var tabs = new[] { ("TabGraphics", "graphics"), ("TabBinds", "binds"), ("TabHelpers", "helpers"),
                           ("TabCrosshair", "crosshair"), ("TabCombat", "combat") };
        foreach (var (name, page) in tabs)
        {
            var btn = this.FindControl<Button>(name);
            if (btn != null) btn.Click += (_, _) => NavigateTo(page);
        }

        var logoBtn = this.FindControl<Border>("LogoBtn");
        if (logoBtn != null) logoBtn.PointerPressed += (_, _) => NavigateTo("home");
    }

    private void UpdateTabActive(string page)
    {
        var tabNames = new Dictionary<string, string>
        {
            ["graphics"] = "TabGraphics", ["binds"] = "TabBinds", ["helpers"] = "TabHelpers",
            ["crosshair"] = "TabCrosshair", ["combat"] = "TabCombat"
        };
        foreach (var (pg, name) in tabNames)
        {
            var btn = this.FindControl<Button>(name);
            if (btn == null) continue;
            if (pg == page) btn.Classes.Add("active"); else btn.Classes.Remove("active");
        }
    }

    #endregion

    #region Global Search

    private sealed record SearchEntry(string Label, string Category, string Kind, string Value);
    private bool _searchGuard;

    private void WireSearch()
    {
        var box = this.FindControl<TextBox>("GlobalSearch");
        var popup = this.FindControl<Popup>("SearchPopup");
        var list = this.FindControl<ListBox>("SearchList");
        if (box == null || popup == null || list == null) return;

        popup.PlacementTarget = box;

        box.TextChanged += (_, _) => UpdateSearchResults(box, popup, list);
        box.GotFocus += (_, _) => UpdateSearchResults(box, popup, list);
        box.KeyDown += (_, e) =>
        {
            if (!popup.IsOpen || list.ItemCount == 0) return;
            if (e.Key == Key.Down)
            {
                _searchGuard = true;
                list.SelectedIndex = Math.Min(list.SelectedIndex + 1, list.ItemCount - 1);
                _searchGuard = false;
                e.Handled = true;
            }
            else if (e.Key == Key.Up)
            {
                _searchGuard = true;
                list.SelectedIndex = Math.Max(list.SelectedIndex - 1, -1);
                _searchGuard = false;
                e.Handled = true;
            }
            else if (e.Key == Key.Enter && list.SelectedItem is SearchEntry entry)
            {
                ExecuteSearchEntry(entry);
                CloseSearchBox(box, popup, list);
                e.Handled = true;
            }
        };

        list.SelectionChanged += (_, _) =>
        {
            if (_searchGuard) return;
            if (list.SelectedItem is not SearchEntry entry) return;
            ExecuteSearchEntry(entry);
            CloseSearchBox(box, popup, list);
        };
    }

    private void CloseSearchBox(TextBox box, Popup popup, ListBox list)
    {
        _searchGuard = true;
        list.SelectedItem = null;
        list.ItemsSource = null;
        _searchGuard = false;
        popup.IsOpen = false;
        box.Text = "";
    }

    private void UpdateSearchResults(TextBox box, Popup popup, ListBox list)
    {
        var q = box.Text?.Trim() ?? "";
        if (q.Length == 0)
        {
            popup.IsOpen = false;
            list.ItemsSource = null;
            return;
        }

        var ql = q.ToLowerInvariant();
        var hits = BuildSearchIndex()
            .Where(s => s.Label.ToLowerInvariant().Contains(ql) || s.Category.ToLowerInvariant().Contains(ql))
            .Take(14)
            .ToList();

        _searchGuard = true;
        list.ItemsSource = hits;
        list.SelectedIndex = hits.Count > 0 ? 0 : -1;
        _searchGuard = false;
        popup.IsOpen = hits.Count > 0;
    }

    private List<SearchEntry> BuildSearchIndex()
    {
        var list = new List<SearchEntry>
        {
            new("Home", "Page", "page", "home"),
            new("Graphics", "Page", "page", "graphics"),
            new("Binds", "Page", "page", "binds"),
            new("Helpers", "Page", "page", "helpers"),
            new("Crosshair", "Page", "page", "crosshair"),
            new("Combat", "Page", "page", "combat"),
            new("Settings", "Page", "page", "settings"),

            new("Dot preset", "Crosshair preset", "preset", "dot"),
            new("Cross preset", "Crosshair preset", "preset", "cross"),
            new("Circle preset", "Crosshair preset", "preset", "circle"),
            new("Bracket preset", "Crosshair preset", "preset", "bracket"),
            new("Tactical preset", "Crosshair preset", "preset", "tactical"),
            new("Custom preset", "Crosshair preset", "preset", "custom"),

            new("Black theme", "Theme", "theme", "black"),
            new("Glass theme", "Theme", "theme", "glass-black"),
            new("Retro theme", "Theme", "theme", "retro"),
            new("Pink theme", "Theme", "theme", "pink"),
            new("Slack theme", "Theme", "theme", "slack"),
            new("Cyber theme", "Theme", "theme", "cyber"),
            new("Mono theme", "Theme", "theme", "mono")
        };

        try
        {
            var dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "presets", "crosshair", "configs");
            if (Directory.Exists(dir))
            {
                foreach (var file in Directory.GetFiles(dir, "*.json"))
                    list.Add(new SearchEntry(Path.GetFileNameWithoutExtension(file), "Crosshair config", "config", file));
            }
        }
        catch { }
        return list;
    }

    private void ExecuteSearchEntry(SearchEntry entry)
    {
        void GoCrosshair()
        {
            NavigateTo("crosshair");
            var content = this.FindControl<ContentControl>("PageContent");
            if (content?.Content is Views.CrosshairPage page)
            {
                if (entry.Kind == "preset") page.SelectPreset(entry.Value);
                else page.LoadConfigFile(entry.Value);
            }
        }

        if (entry.Kind == "page")
        {
            NavigateTo(entry.Value);
        }
        else if (entry.Kind is "preset" or "config")
        {
            GoCrosshair();
            SetStatus(entry.Kind == "preset" ? $"Preset: {entry.Value}" : $"Loaded config: {entry.Label}");
        }
        else if (entry.Kind == "theme")
        {
            ApplyTheme(entry.Value);
            NavigateTo("settings");
        }
    }

    #endregion

    #region Control Buttons

    private void WireControlButtons()
    {
        var closeBtn = this.FindControl<Button>("CloseBtn");
        var minBtn = this.FindControl<Button>("MinBtn");
        var playBtn = this.FindControl<Button>("PlayBtn");
        var overlayBtn = this.FindControl<Button>("OverlayBtn");

        if (closeBtn != null) closeBtn.Click += (_, _) => Close();
        if (minBtn != null) minBtn.Click += (_, _) => WindowState = WindowState.Minimized;
        if (playBtn != null) playBtn.Click += (_, _) => { _steamService.LaunchGame(); SetStatus("Launching game..."); };
        if (overlayBtn != null) overlayBtn.Click += (_, _) =>
        {
            _overlayDispatcher?.Invoke(() =>
            {
                if (_overlayWindow == null) return;
                if (_overlayWindow.IsVisible) _overlayWindow.Hide(); else _overlayWindow.Show();
            });
            SetStatus("Overlay toggled");
        };
    }

    #endregion

    #region Navigation

    private void NavigateTo(string pageName)
    {
        UpdateTabActive(pageName);
        var content = this.FindControl<ContentControl>("PageContent");
        if (content == null) return;

        if (pageName is "crosshair" or "settings")
            _overlayReady?.Wait(2000);

        UserControl page = pageName switch
        {
            "home" => new Views.HomePage(NavigateTo),
            "graphics" => new Views.GraphicsPage(),
            "binds" => new Views.BindsPage(),
            "helpers" => new Views.HelpersPage(),
            "crosshair" => new Views.CrosshairPage(_settings, _overlayWindow, _overlayDispatcher),
            "combat" => new Views.CombatPage(_combatService, _settings),
            "settings" => new Views.SettingsPage(_settings, _currentTheme, ApplyTheme, ResetAll),
            _ => new Views.GraphicsPage()
        };
        content.Content = page;
        SetStatus($"Page: {pageName}");
    }

    #endregion

    #region Theme

    private void ApplyTheme(string theme)
    {
        _currentTheme = theme;
        _settings.SetTheme(theme);

        var resources = Application.Current?.Resources;
        if (resources == null) return;

        var c = theme switch
        {
            "black" => new { Base = "#0c0c0e", Low = "#111114", Mid = "#18181c", High = "#1f1f24", Overlay = "#27272e", BSubtle = "#1a1a1f", BDef = "#232329", BStrong = "#35353d" },
            "glass-black" => new { Base = "#07070a", Low = "#0d0d11", Mid = "#131318", High = "#19191f", Overlay = "#202028", BSubtle = "#16161b", BDef = "#1e1e25", BStrong = "#2d2d36" },
            "retro" => new { Base = "#0f0f1a", Low = "#151525", Mid = "#1a1a2e", High = "#222240", Overlay = "#2a2a50", BSubtle = "#1a1a35", BDef = "#25254a", BStrong = "#35356a" },
            "pink" => new { Base = "#100a0e", Low = "#17101a", Mid = "#1e1520", High = "#251a28", Overlay = "#2d1f30", BSubtle = "#201525", BDef = "#2d1a2a", BStrong = "#3d2a3a" },
            "slack" => new { Base = "#111315", Low = "#161819", Mid = "#1c1e20", High = "#222426", Overlay = "#282a2c", BSubtle = "#1e2022", BDef = "#252729", BStrong = "#323436" },
            "cyber" => new { Base = "#070a0e", Low = "#0c1018", Mid = "#111620", High = "#171c28", Overlay = "#1d2330", BSubtle = "#121825", BDef = "#1a2332", BStrong = "#253040" },
            "mono" => new { Base = "#0c0c0c", Low = "#111111", Mid = "#1a1a1a", High = "#222222", Overlay = "#2a2a2a", BSubtle = "#1a1a1a", BDef = "#2a2a2a", BStrong = "#3a3a3a" },
            _ => new { Base = "#0c0c0e", Low = "#111114", Mid = "#18181c", High = "#1f1f24", Overlay = "#27272e", BSubtle = "#1a1a1f", BDef = "#232329", BStrong = "#35353d" }
        };

        SetBrush("SurfaceBase", c.Base);
        SetBrush("SurfaceLow", c.Low);
        SetBrush("SurfaceMid", c.Mid);
        SetBrush("SurfaceHigh", c.High);
        SetBrush("SurfaceOverlay", c.Overlay);
        SetBrush("BorderSubtle", c.BSubtle);
        SetBrush("BorderDefault", c.BDef);
        SetBrush("BorderStrong", c.BStrong);

        SetStatus($"Theme: {theme}");

        void SetBrush(string key, string hex)
        {
            if (resources.TryGetResource(key, null, out var existing) && existing is SolidColorBrush old)
            {
                old.Color = Color.Parse(hex);
            }
        }
    }

    private void StartThemeWatcher()
    {
        var settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VoidUI", "settings.json");
        if (!File.Exists(settingsPath)) return;
        _themeWatcher = new System.Timers.Timer(2000);
        var lastWrite = File.GetLastWriteTimeUtc(settingsPath);
        _themeWatcher.Elapsed += (_, _) =>
        {
            try
            {
                var current = File.GetLastWriteTimeUtc(settingsPath);
                if (current != lastWrite)
                {
                    lastWrite = current;
                    var s = _settings.Load();
                    if (s.Theme != _currentTheme)
                        Avalonia.Threading.Dispatcher.UIThread.Post(() => ApplyTheme(s.Theme ?? "black"));
                }
            }
            catch { }
        };
        _themeWatcher.AutoReset = true;
        _themeWatcher.Start();
    }

    #endregion

    #region Steam / Overlay

    private void DetectSteam()
    {
        try
        {
            var result = _steamService.DetectSteam();
            SetStatus(result.Success ? $"Rust found: {Path.GetFileName(result.RustPath)}" : "Rust not detected");
        }
        catch (Exception ex) { SetStatus($"Steam detect error: {ex.Message}"); }
    }

    private void StartOverlay()
    {
        _overlayReady = new System.Threading.ManualResetEventSlim(false);
        _overlayThread = new System.Threading.Thread(() =>
        {
            try
            {
                var app = new System.Windows.Application();
                app.ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown;
                _overlayWindow = new OverlayWindow(_settings);
                _overlayDispatcher = _overlayWindow.Dispatcher;
                _overlayReady.Set();
                _overlayWindow.Show();
                app.Run();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Overlay] Failed to start: {ex.Message}");
                try
                {
                    var path = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "overlay_debug.log");
                    System.IO.File.AppendAllText(path, $"{DateTime.Now:HH:mm:ss.fff} [Overlay] FAILED: {ex}\n");
                }
                catch { }
                _overlayReady.Set();
            }
        });
        _overlayThread.SetApartmentState(System.Threading.ApartmentState.STA);
        _overlayThread.IsBackground = true;
        _overlayThread.Start();
    }

    #endregion

    #region Settings

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
    }

    private void ResetAll()
    {
        try
        {
            var settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VoidUI", "settings.json");
            if (File.Exists(settingsPath)) File.Delete(settingsPath);
            _settings.Load();
            ApplyTheme("black");
            SetStatus("All settings reset");
        }
        catch { }
    }

    #endregion

    #region Status

    private void SetStatus(string text)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            try
            {
                var grid = this.FindControl<Grid>("PageContent")?.Parent as Grid;
                if (grid == null) return;
                var statusBar = grid.Children.OfType<Border>().LastOrDefault();
                var innerGrid = statusBar?.Child as Grid;
                var sp = innerGrid?.Children.OfType<StackPanel>().FirstOrDefault();
                var tb = sp?.Children.OfType<TextBlock>().LastOrDefault();
                if (tb != null) tb.Text = text;
            }
            catch { }
        });
    }

    #endregion
}
