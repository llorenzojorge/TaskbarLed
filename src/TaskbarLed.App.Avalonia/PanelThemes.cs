using System.Globalization;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Media;
using TaskbarLed.App.Avalonia.Presentation;

namespace TaskbarLed.App.Avalonia;

internal static class PanelThemes
{
    // Theme names are proper names, the same in every language and on both platforms (see ThemeCatalog).
    internal sealed record Theme(string Id, string Name, bool Uppercase = false);

    private static readonly Thickness DefaultPadding = new(5, 2, 7, 1);

    public static IReadOnlyList<Theme> All { get; } =
    [
        new("ClaudeCode", "Claude Code"),
        new("ModernTerminal", "Modern Terminal"),
        new("Matrix", "Matrix", true),
        new("Mono", "Mono", true),
        new("Minimal", "Minimal"),
        new("Glass", "Glass"),
        new("LiquidGlass", "Liquid Glass"),
        new("Paper", "Paper"),
        new("AmberTree", "Amber Tree"),
        new("CompactCharcoal", "Compact Charcoal"),
        new("AmberRail", "Amber Rail"),
        new("ClearList", "Clear List"),
        new("EditorialIndex", "Editorial Index"),
        new("CobaltWindow", "Cobalt Window"),
        new("VioletPhosphor", "Violet Phosphor"),
        new("LineMap", "Line Map"),
        new("OliveLcd", "Olive LCD", Uppercase: true),
    ];

    public static Theme Apply(Application app, PanelViewModel model, string themeId)
    {
        var theme = All.FirstOrDefault(t => t.Id == themeId) ?? All[0];
        model.Uppercase = theme.Uppercase;
        model.ThemeId = theme.Id;
        var (background, foreground) = PreviewColors(theme);
        app.Resources["Led.PanelSurfaceBrush"] = new SolidColorBrush(background);
        app.Resources["Led.SessionTitleBrush"] = new SolidColorBrush(foreground);
        app.Resources["Led.SessionRunningBrush"] = new SolidColorBrush(foreground);
        app.Resources["Led.SessionHeaderStatusBrush"] = new SolidColorBrush(foreground);
        foreach (var key in new[] { "SessionHeaderBrush", "SessionRailBrush", "SessionTreeBrush", "SessionChildBrush", "SessionStatusBrush", "SessionDividerBrush", "SessionNodeBrush" })
        {
            app.Resources["Led." + key] = Brushes.Transparent;
        }

        app.Resources["Led.PanelRadius"] = new CornerRadius(4);
        app.Resources["Led.FrameThickness"] = new Thickness(1);
        app.Resources["Led.PanelPadding"] = DefaultPadding;
        app.Resources["Led.CompletionBrush"] = new SolidColorBrush(Color.Parse("#BFE6C8"));
        app.Resources["Led.CompletionTint"] = new SolidColorBrush(Color.Parse("#55BFE6C8"));
        app.Resources["Led.BrandBackdropBrush"] = Brushes.Transparent;
        app.Resources["Led.BrandHeaderBrush"] = Brushes.Transparent;
        app.Resources["Led.RouteDividerBrush"] = Brushes.Transparent;
        app.Resources["Led.SessionHeaderWaitingBrush"] = Brushes.Transparent;
        foreach (var value in SharedThemeResources(theme.Id))
        {
            var key = (string?)value.Attribute(ResourceKey);
            if (key is null)
            {
                continue;
            }

            switch (key)
            {
                case "Led.FontFamily":
                    app.Resources[key] = new FontFamily(value.Value + ", Menlo, SF Mono");
                    break;
                case "Led.GlowColor":
                    app.Resources[key] = Color.Parse(value.Value);
                    break;
                case "Led.GlowOpacity":
                    app.Resources[key] = double.Parse(value.Value, CultureInfo.InvariantCulture);
                    break;
                case "Led.FeedPrefix":
                    app.Resources[key] = value.Value;
                    break;
                case "Led.DotGridBrush":
                    app.Resources[key] = BuildDotGrid(value);
                    break;
                case "Led.PanelRadius":
                    app.Resources[key] = new CornerRadius(double.Parse(value.Value, CultureInfo.InvariantCulture));
                    break;
                case "Led.FrameThickness":
                    app.Resources[key] = new Thickness(double.Parse(value.Value, CultureInfo.InvariantCulture));
                    break;
                case "Led.PanelPadding":
                    app.Resources[key] = Thickness.Parse(value.Value);
                    break;
                case "Led.PanelSurfaceBrush":
                    app.Resources[key] = new LinearGradientBrush
                    {
                        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                        EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                        GradientStops = [.. value.Elements().Select(stop => new GradientStop(
                            Color.Parse(stop.Attribute("Color")!.Value),
                            double.Parse(stop.Attribute("Offset")!.Value, CultureInfo.InvariantCulture)))],
                    };
                    break;
                default:
                    if (value.Attribute("Color") is { } color)
                    {
                        app.Resources[key] = new SolidColorBrush(Color.Parse(color.Value));
                    }

                    break;
            }
        }

        ApplyMenuPalette(app);
        return theme;
    }

    private static readonly XName ResourceKey = XName.Get("Key", "http://schemas.microsoft.com/winfx/2006/xaml");

    private static XElement[] SharedThemeResources(string id)
    {
        using var resource = typeof(PanelThemes).Assembly.GetManifestResourceStream($"PanelThemes.{id}.xaml")!;
        return [.. XDocument.Load(resource).Root!.Elements()];
    }

    internal static (Color Background, Color Text) PreviewColors(Theme theme)
    {
        var resources = SharedThemeResources(theme.Id);
        Color Read(string key) => Color.Parse(resources.First(element => (string?)element.Attribute(ResourceKey) == key)
            .Attribute("Color")!.Value);
        return (Read("Led.BackgroundBrush"), Read("Led.ForegroundBrush"));
    }

    private static void ApplyMenuPalette(Application app)
    {
        Color Read(string key) => ((ISolidColorBrush)app.Resources["Led." + key]!).Color;
        void Brush(string key, Color color) => app.Resources[key] = new SolidColorBrush(color);

        var translucent = Read("BackgroundBrush");
        var background = Blend(Color.Parse("#192321"), translucent, translucent.A / 255.0);
        var foreground = Read("ForegroundBrush");
        var accent = Read("EditBrush");
        if (Contrast(accent, background) < 4.5)
        {
            accent = foreground;
        }

        var border = Blend(background, foreground, 0.25);
        var highlight = Blend(background, accent, 0.15);
        var pressed = Blend(background, accent, 0.23);
        if (Contrast(foreground, pressed) < 4.5)
        {
            foreground = Contrast(Colors.Black, background) > Contrast(Colors.White, background) ? Colors.Black : Colors.White;
        }

        Brush("MenuFlyoutPresenterBackground", background);
        Brush("MenuFlyoutPresenterBorderBrush", border);
        Brush("MenuFlyoutItemForeground", foreground);
        Brush("MenuFlyoutItemForegroundPointerOver", foreground);
        Brush("MenuFlyoutItemForegroundPressed", foreground);
        Brush("MenuFlyoutItemForegroundDisabled", accent);
        Brush("MenuFlyoutSubItemChevron", foreground);
        Brush("MenuFlyoutItemBackgroundPointerOver", highlight);
        Brush("MenuFlyoutItemBackgroundPressed", pressed);
        Brush("Led.MenuQuitBrush", highlight);
        Brush("Led.MenuAccentBrush", accent);
    }

    private static Color Blend(Color baseColor, Color over, double opacity) => Color.FromRgb(
        (byte)Math.Round(baseColor.R + (over.R - baseColor.R) * opacity),
        (byte)Math.Round(baseColor.G + (over.G - baseColor.G) * opacity),
        (byte)Math.Round(baseColor.B + (over.B - baseColor.B) * opacity));

    private static double Contrast(Color a, Color b)
    {
        static double Linear(byte channel)
        {
            var value = channel / 255.0;
            return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
        }

        static double Luminance(Color color) => 0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B);
        var first = Luminance(a);
        var second = Luminance(b);
        return (Math.Max(first, second) + 0.05) / (Math.Min(first, second) + 0.05);
    }

    private static IBrush BuildDotGrid(XElement value)
    {
        if (value.Name.LocalName != "DrawingBrush")
        {
            return Brushes.Transparent;
        }

        var grid = value.Descendants().First(n => n.Name.LocalName == "GeometryDrawing");
        var color = new SolidColorBrush(Color.Parse(grid.Attribute("Brush")!.Value));
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing { Brush = Brushes.Transparent, Geometry = new RectangleGeometry(new Rect(0, 0, 3, 3)) });
        foreach (var rectangle in grid.Descendants().Where(n => n.Name.LocalName == "RectangleGeometry"))
        {
            group.Children.Add(new GeometryDrawing { Brush = color, Geometry = new RectangleGeometry(Rect.Parse(rectangle.Attribute("Rect")!.Value)) });
        }

        return new DrawingBrush
        {
            Drawing = group,
            DestinationRect = new RelativeRect(0, 0, 3, 3, RelativeUnit.Absolute),
            TileMode = TileMode.Tile,
            Opacity = double.Parse(value.Attribute("Opacity")!.Value, CultureInfo.InvariantCulture),
        };
    }
}
