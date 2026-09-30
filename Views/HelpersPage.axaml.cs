using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace VoidUI.Views;

public partial class HelpersPage : UserControl
{
    private readonly (string Title, string Desc, string Tags, string Url, string Accent)[] _helpers = new[]
    {
        ("Breeding Helper", "Cross-breed plants with breeding formulas", "Farming, Genetics", "https://rustbreeder.com/", "#34d399"),
        ("Raid Calculator", "Calculate optimal raid composition and costs", "PvP, Raiding", "https://rustexplore.com/tools/raid-calculator", "#f87171"),
        ("Server Stats", "Real-time server statistics and performance", "Server, Stats", "https://www.rustalyzer.com/search", "#60a5fa"),
        ("Interactive Map", "Full interactive map with markers and routes", "Navigation", "https://rustmaps.com/", "#fbbf24"),
        ("Monument Map", "Monument locations with loot tables", "Monuments, Loot", "https://rustmaps.com/monuments", "#a78bfa"),
        ("Recycling Calculator", "Find out how much scrap from recycling", "Recycling, Scrap", "https://howmuchscrap.com/", "#fb923c"),
        ("Electricity Simulator", "Design and optimize electrical systems", "Electricity", "https://www.rustrician.io/", "#f472b6"),
        ("Rust Wiki", "Complete game documentation and tutorials", "Docs, Guides", "https://rust.fandom.com/wiki/Rust_Wiki", "#94a3b8"),
        ("Beginner's Guide", "Step-by-step guide for new players", "Tutorial", "https://altarofgaming.com/rust-beginners-guide/", "#34d399"),
        ("Base Planner", "Design and plan your base with 2D/3D tools", "Building", "https://www.freshspawn.online/build", "#60a5fa"),
        ("Twitch Drops", "Connect Steam and Twitch, get skins", "Twitch, Drops", "https://twitch.facepunch.com/", "#a78bfa"),
    };

    public HelpersPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        var wrap = this.FindControl<WrapPanel>("HelpersWrap");
        if (wrap == null) return;

        foreach (var (title, desc, tags, url, accent) in _helpers)
        {
            var card = new Border
            {
                Width = 235, MinHeight = 140, Margin = new Thickness(0, 0, 12, 12),
                Background = new SolidColorBrush(Color.Parse("#111114")),
                BorderBrush = new SolidColorBrush(Color.Parse("#1a1a1f")),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(20)
            };

            var sp = new StackPanel { Spacing = 6 };
            var accentBorder = new Border
            {
                Background = new SolidColorBrush(Color.Parse(accent)) { Opacity = 0.15 },
                CornerRadius = new CornerRadius(8), Width = 36, Height = 36,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 0, 4), Padding = new Thickness(0)
            };
            var accentIcon = new TextBlock
            {
                Text = title.Substring(0, 1),
                FontSize = 16, FontWeight = FontWeight.Bold,
                Foreground = new SolidColorBrush(Color.Parse(accent)),
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            };
            accentBorder.Child = accentIcon;
            sp.Children.Add(accentBorder);
            sp.Children.Add(new TextBlock { Text = title, Foreground = new SolidColorBrush(Color.Parse("#ededef")), FontSize = 13, FontWeight = FontWeight.SemiBold });
            sp.Children.Add(new TextBlock { Text = desc, Foreground = new SolidColorBrush(Color.Parse("#5c5c66")), FontSize = 10, TextWrapping = TextWrapping.Wrap, MaxHeight = 32 });
            sp.Children.Add(new TextBlock { Text = tags, Foreground = new SolidColorBrush(Color.Parse("#3a3a42")), FontSize = 9 });

            var openBtn = new Button { Content = "Open Tool", Height = 28, MinWidth = 80, FontSize = 10, Tag = url };
            openBtn.Classes.Add("pill");
            openBtn.Click += (_, _) =>
            {
                if (openBtn.Tag is string u)
                    Process.Start(new ProcessStartInfo { FileName = u, UseShellExecute = true });
            };
            sp.Children.Add(openBtn);

            card.Child = sp;
            wrap.Children.Add(card);
        }
    }
}
