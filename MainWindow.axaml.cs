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
    private Avalonia.Threading.DispatcherTimer? _statusTimer;
    private int _statusTick;
    private string _workingFile = "output_log.txt";
    private bool _rustPathExpanded;
    private string? _rustFullPath;
    private bool _targetOk;
    private bool _rustMissing;
    private bool _workStarted;     // открыты binds/graphics или запущен парс

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
        StartStatusBar();
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

        // Первая «работа»: открыли binds или graphics — ready уходит
        if (pageName is "binds" or "graphics")
            _workStarted = true;

        // В баре показываем файл, с которым идёт работа на этой странице
        SetWorkingFile(pageName switch
        {
            "binds" => "cfg/keys.txt",
            "graphics" => "cfg/client.txt",
            _ => "output_log.txt"
        });
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

    /// <summary>Нижний бар: анимация точек + периодическая проверка rust/лога.</summary>
    private void StartStatusBar()
    {
        RefreshRustStatus();

        _statusTimer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _statusTimer.Tick += (_, _) =>
        {
            _statusTick++;
            // Ловим старт парса в комбате (Run/Auto) — ready сразу уходит в работу
            if (!_workStarted && _combatService.IsMonitoring)
            {
                _workStarted = true;
                RefreshRustStatus();
            }
            if (_statusTick % 25 == 0) RefreshRustStatus();   // раз в ~10 секунд

            var dots = this.FindControl<TextBlock>("StatusDots");
            if (dots == null || _rustMissing) return;   // rust не найден — анимация скрыта
            if (_workStarted && _targetOk)
            {
                // Работаем: точки появляются и пропадают, строка всегда 3 символа.
                dots.Text = (_statusTick % 8) switch
                {
                    1 => ".  ",
                    2 => ".. ",
                    3 => "...",
                    4 => "...",
                    5 => ".. ",
                    6 => ".  ",
                    _ => "   "
                };
            }
            else
            {
                // Простой: вместо точек милый смайл, символы меняются по кругу (тоже 3 символа).
                dots.Text = (_statusTick % 4) switch
                {
                    0 => "^~^",
                    1 => "^o^",
                    2 => "^_^",
                    _ => "^-^"
                };
            }
        };
        _statusTimer.Start();
    }

    private void RefreshRustStatus()
    {
        try
        {
            var det = _steamService.DetectSteam();
            bool found = det.Success && !string.IsNullOrEmpty(det.RustPath);
            var logPath = _steamService.GetRustLogPath();
            _rustFullPath = found ? det.RustPath : null;
            if (!found) _rustPathExpanded = false;
            _rustMissing = !found;

            // Центр прячем, когда раста нет: сообщение живёт слева, без дублей.
            var rustText = this.FindControl<TextBlock>("RustStatusText");
            if (rustText != null)
            {
                rustText.IsVisible = found;
                rustText.Text = _rustPathExpanded ? _rustFullPath!
                    : $"rust in {AbbrevPath(_rustFullPath)}";
            }

            var rustDot = this.FindControl<Border>("RustStatusDot");
            if (rustDot != null)
            {
                rustDot.IsVisible = found;
                rustDot.Background = new SolidColorBrush(Color.Parse("#22c55e"));
            }

            // Файл, над которым идёт работа: показываем как есть (в т.ч. .txt),
            // а существование проверяем по реальному файлу игры (.cfg в папке cfg).
            string? relReal = _workingFile switch
            {
                "cfg/keys.txt" => "cfg/keys.cfg",
                "cfg/client.txt" => "cfg/client.cfg",
                _ => _workingFile
            };
            string? targetPath = relReal == "output_log.txt"
                ? logPath
                : found ? Path.Combine(det.RustPath, relReal.Replace('/', Path.DirectorySeparatorChar)) : null;
            _targetOk = targetPath != null && File.Exists(targetPath);

            var dotsUi = this.FindControl<TextBlock>("StatusDots");
            var fileLabel = this.FindControl<TextBlock>("StatusFileLabel");
            var fileDot = this.FindControl<Border>("StatusFileDot");
            var filePath = this.FindControl<TextBlock>("StatusFilePath");

            if (!found)
            {
                // Rust не найден: только красный кружок и эта строка — и больше ничего.
                if (fileLabel != null)
                {
                    fileLabel.Text = "rust dont found :(";
                    fileLabel.IsVisible = true;
                }
                if (fileDot != null)
                {
                    fileDot.Background = new SolidColorBrush(Color.Parse("#ef4444"));
                    fileDot.IsVisible = true;
                }
                if (dotsUi != null) dotsUi.IsVisible = false;
                if (filePath != null)
                {
                    filePath.IsVisible = false;
                    filePath.Text = "";
                }
            }
            else if (!_workStarted)
            {
                // Ещё ничего не начато: ready — жёлтый кружок и смайл, файла пока нет.
                if (fileLabel != null)
                {
                    fileLabel.Text = "ready";
                    fileLabel.IsVisible = true;
                }
                if (fileDot != null)
                {
                    fileDot.Background = new SolidColorBrush(Color.Parse("#facc15"));
                    fileDot.IsVisible = true;
                }
                if (dotsUi != null) dotsUi.IsVisible = true;   // здесь крутится ^~^
                if (filePath != null)
                {
                    filePath.IsVisible = false;
                    filePath.Text = "";
                }
            }
            else
            {
                if (fileLabel != null)
                {
                    fileLabel.Text = _targetOk ? "working" : "waiting";
                    fileLabel.IsVisible = true;
                }
                if (fileDot != null)
                {
                    // зелёный — работаем; жёлтый — простой (файла нет)
                    fileDot.Background = new SolidColorBrush(Color.Parse(_targetOk ? "#22c55e" : "#facc15"));
                    fileDot.IsVisible = true;
                }
                if (dotsUi != null) dotsUi.IsVisible = true;
                if (filePath != null)
                {
                    filePath.IsVisible = true;
                    filePath.Text = $"with {_workingFile} on rust directory";
                }
            }
        }
        catch { }
    }

    /// <summary>
    /// Путь в виде ..../..../..../Rust/output_log.txt:
    /// родительские папки заменяются группами точек (максимум 3),
    /// а хвост — папка игры и файл — остаётся настоящим.
    /// </summary>
    private static string AbbrevPath(string? path)
    {
        if (string.IsNullOrEmpty(path)) return "";
        var segs = path.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
        if (segs.Length == 0) return path;

        // отрезаем от папки игры (Rust), иначе — последние два сегмента
        int keepFrom = -1;
        for (int i = segs.Length - 1; i >= 0; i--)
            if (segs[i].Equals("rust", StringComparison.OrdinalIgnoreCase)) { keepFrom = i; break; }
        if (keepFrom < 0)
            for (int i = segs.Length - 1; i >= 0; i--)
                if (segs[i].IndexOf("rust", StringComparison.OrdinalIgnoreCase) >= 0) { keepFrom = i; break; }
        if (keepFrom < 0) keepFrom = Math.Max(0, segs.Length - 2);

        var sb = new System.Text.StringBuilder();
        int groups = Math.Min(keepFrom, 3);
        for (int i = 0; i < groups; i++) sb.Append("..../");
        for (int i = keepFrom; i < segs.Length; i++)
        {
            sb.Append(segs[i]);
            if (i < segs.Length - 1) sb.Append("/");
        }
        return sb.ToString();
    }

    /// <summary>Переключает файл, над которым работаем (вызывается при смене страницы).</summary>
    private void SetWorkingFile(string file)
    {
        if (_workingFile != file) _rustPathExpanded = false;
        _workingFile = file;
        RefreshRustStatus();   // обновляем всегда: мог сработать триггер ready → работа
    }

    /// <summary>Клик по rust-статусу: ..../..../Rust ⇄ полный путь.</summary>
    private void OnRustStatusClick(object? sender, PointerPressedEventArgs e)
    {
        if (string.IsNullOrEmpty(_rustFullPath)) return;
        _rustPathExpanded = !_rustPathExpanded;
        var tb = this.FindControl<TextBlock>("RustStatusText");
        if (tb != null)
            tb.Text = _rustPathExpanded ? _rustFullPath! : $"rust in {AbbrevPath(_rustFullPath)}";
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
        _statusTimer?.Stop();
        _statusTimer = null;
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
            var tb = this.FindControl<TextBlock>("BarStatusText");
            if (tb != null) tb.Text = text;
        });
    }

    #endregion
}
