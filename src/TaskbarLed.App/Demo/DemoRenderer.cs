using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using TaskbarLed.App.Panels;
using TaskbarLed.App.Presentation;
using TaskbarLed.App.Themes;
using TaskbarLed.Core.Presentation;
using TaskbarLed.Core.Sessions;

namespace TaskbarLed.App.Demo;

/// <summary>
/// Renders README screenshots from the real panel XAML and the real composer, so the images always match
/// the app. Run <c>TaskbarLed.exe --render-demo docs/images</c> after changing the look or adding a theme.
/// </summary>
internal static class DemoRenderer
{
    private const double Scale = 2; // Retina-friendly PNGs.
    private const double TaskbarWidth = 1000;
    private const double TaskbarHeight = 48;

    private static readonly DateTimeOffset Now = new(2026, 11, 14, 14, 32, 0, TimeSpan.Zero);

    public static void RenderAll(Application app, string folder)
    {
        Directory.CreateDirectory(folder);

        foreach (var (suffix, culture) in new[] { ("en", "en-US"), ("pt", "pt-BR") })
        {
            var lang = CultureInfo.GetCultureInfo(culture);
            ThemeCatalog.Apply(app.Resources, ThemeCatalog.DefaultId);

            Save(Taskbar(Frame(Scenario.Running, lang)), folder, $"state-running-{suffix}.png");
            Save(Taskbar(Frame(Scenario.Waiting, lang)), folder, $"state-waiting-{suffix}.png");
            Save(Taskbar(Frame(Scenario.Done, lang), halo: true), folder, $"state-done-{suffix}.png");
            Save(Taskbar(Frame(Scenario.Running, lang), editing: Infrastructure.UiStrings.For(lang).EditHint, ui: Infrastructure.UiStrings.For(lang)), folder, $"state-editing-{suffix}.png");
            Save(Fullscreen(lang), folder, $"fullscreen-{suffix}.png");
        }

        var english = CultureInfo.GetCultureInfo("en-US");
        foreach (var theme in ThemeCatalog.All)
        {
            ThemeCatalog.Apply(app.Resources, theme.Id);
            Save(Taskbar(Frame(Scenario.Mixed, english), width: 560, uppercase: theme.Uppercase, light: theme.Id == "Paper"), folder, $"theme-{theme.Id}.png");
        }

        // The example user theme, drawn through the same JSON path the app uses for files in the themes folder.
        if (Core.Themes.CustomTheme.TryParse(ExampleTheme.FileName, ExampleTheme.Json, out var example, out var error))
        {
            ThemeCatalog.ApplyCustom(app.Resources, example!);
            Save(Taskbar(Frame(Scenario.Mixed, english), width: 560, uppercase: example!.Uppercase), folder, "theme-custom-example.png");
        }
        else
        {
            throw new InvalidOperationException($"The example theme does not parse: {error}");
        }

        ThemeCatalog.Apply(app.Resources, ThemeCatalog.DefaultId);
    }

    // State and full-screen screenshots use the default theme, in its own letter case.
    private static bool DefaultUppercase => ThemeCatalog.Resolve(ThemeCatalog.DefaultId).Uppercase;

    private static string Cased(string text, bool pt) =>
        DefaultUppercase ? text.ToUpper(CultureInfo.GetCultureInfo(pt ? "pt-BR" : "en-US")) : text;

    private enum Scenario
    {
        Running,
        Waiting,
        Done,
        Mixed,
    }

    // Builds sessions the way the hooks would, then lets the real composer decide what the panel shows.
    private static PanelFrame Frame(Scenario scenario, CultureInfo culture)
    {
        var pt = culture.TwoLetterISOLanguageName == "pt";
        var store = new SessionStore();

        void Prompt(string id, string project, int minutesAgo, int secondsAgo = 0) =>
            store.Apply(new PromptSubmitted(id, "demo", Now.AddMinutes(-minutesAgo).AddSeconds(-secondsAgo), null, project));

        // Chats are named by their title, as in the Claude app; generic ones, never real projects.
        void Title(string id, string title) => store.Apply(new TitleIdentified(id, "demo", Now, title));

        Prompt("a", "webapp", 4, 12);
        Title("a", pt ? "Scraper de preços" : "Price scraper");
        store.Apply(new ActivityStarted("a", "demo", Now.AddSeconds(-3), pt ? "Editando scraper.py" : "Editing scraper.py"));

        Prompt("b", "api", 12, 47);
        Title("b", pt ? "Testes da API de pedidos" : "Orders API tests");
        Prompt("c", "site", 1, 3);
        Title("c", pt ? "Landing page" : "Landing page");
        store.Apply(new ActivityStarted("c", "demo", Now.AddSeconds(-5), pt ? "Rodando npm test" : "Running npm test"));

        switch (scenario)
        {
            case Scenario.Waiting:
                store.Apply(new AttentionRequested("c", "demo", Now, pt ? "Rodar npm install?" : "Run npm install?"));
                break;

            case Scenario.Done:
                store.Apply(new ResponseCompleted("b", "demo", Now, pt ? "Pronto. 24 testes passando." : "Done. All 24 tests pass."));
                break;

            case Scenario.Mixed:
                // Theme gallery: main color in the feed, plus a finished tab for contrast.
                store.Apply(new ResponseCompleted("b", "demo", Now, null));
                break;

            case Scenario.Running:
            default:
                break;
        }

        var composer = new PanelComposer(new SessionRotation(TimeSpan.FromSeconds(8)), TickerStrings.For(culture), culture);
        var frame = composer.Compose(store.Snapshot(), Now);

        // Show the interesting session in the feed for each picture.
        var focus = scenario switch
        {
            Scenario.Done => "b",
            Scenario.Waiting => "c",
            _ => "a",
        };

        if (frame.SelectedSessionId != focus)
        {
            for (var i = 0; i < 3 && frame.SelectedSessionId != focus; i++)
            {
                composer.Rotation.Skip();
                frame = composer.Compose(store.Snapshot(), Now.AddSeconds(i + 1));
            }
        }

        return frame;
    }

    private static Grid Taskbar(PanelFrame frame, double width = TaskbarWidth, bool halo = false, string? editing = null, bool? uppercase = null, bool light = false, Infrastructure.UiStrings? ui = null)
    {
        var viewModel = new PanelViewModel(editing ?? string.Empty, CultureInfo.CurrentUICulture)
        {
            Uppercase = uppercase ?? DefaultUppercase,
            SaveLabel = ui?.Save ?? "Save",
            CancelLabel = ui?.Cancel ?? "Cancel",
        };
        viewModel.Apply(frame);
        viewModel.IsEditing = editing is not null;

        var scene = new Grid { Width = width, Height = TaskbarHeight, Background = new SolidColorBrush(light ? Color.FromRgb(0xEE, 0xEE, 0xEE) : Color.FromRgb(0x1C, 0x1C, 0x1C)) };
        scene.Children.Add(new Border { BorderBrush = new SolidColorBrush(Color.FromRgb(0x2E, 0x2E, 0x2E)), BorderThickness = new Thickness(0, 1, 0, 0) });

        var icons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        if (width >= TaskbarWidth)
        {
            icons.Margin = new Thickness(200, 0, 0, 0);
            foreach (var shade in new byte[] { 0x0A, 0x3A, 0x3A, 0x3A, 0x3A, 0x3A })
            {
                icons.Children.Add(new Border { Width = 24, Height = 24, Margin = new Thickness(8, 0, 8, 0), CornerRadius = new CornerRadius(5), Background = new SolidColorBrush(Color.FromRgb(shade == 0x0A ? (byte)0x3B : shade, shade == 0x0A ? (byte)0x8E : shade, shade == 0x0A ? (byte)0xEA : shade)) });
            }

            scene.Children.Add(icons);
            scene.Children.Add(new TextBlock
            {
                Text = "14:32",
                Foreground = Brushes.Gainsboro,
                FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"),
                FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 16, 0),
            });
        }

        var view = new PanelView
        {
            DataContext = viewModel,
            Width = Math.Min(460, width - 24),
            Height = 36,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 0, 0),
        };
        scene.Children.Add(view);

        if (halo)
        {
            var outline = new Rectangle { Width = view.Width, Height = 36, RadiusX = 4, RadiusY = 4, StrokeThickness = 1.5, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Margin = view.Margin };
            outline.SetResourceReference(Shape.StrokeProperty, "Led.DoneBrush");
            scene.Children.Add(outline);
        }

        Layout(scene);
        view.ShowLine();
        return scene;
    }

    // A full-screen video with the edge glow and an alert, as TaskbarLed draws them over YouTube.
    private static Grid Fullscreen(CultureInfo culture)
    {
        const double width = 960;
        const double height = 540;
        var pt = culture.TwoLetterISOLanguageName == "pt";

        var scene = new Grid { Width = width, Height = height, Background = new LinearGradientBrush(Color.FromRgb(0x2A, 0x16, 0x3D), Color.FromRgb(0x0B, 0x1A, 0x33), 45) };
        scene.Children.Add(new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M 0,0 L 56,34 L 0,68 Z"),
            Fill = new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        });

        var edge = new Border { Height = 4, VerticalAlignment = VerticalAlignment.Bottom };
        edge.SetResourceReference(Border.BackgroundProperty, "Led.DoneBrush");
        scene.Children.Add(edge);

        var text = new TextBlock
        {
            Text = Cased(pt ? "✓ 2 API  ›  Concluído em 12m47s" : "✓ 2 API  ›  Done in 12m47s", pt),
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(14, 8, 14, 8),
            Effect = new DropShadowEffect { BlurRadius = 8, ShadowDepth = 0, Opacity = 0.75, Color = Color.FromRgb(0x33, 0xFF, 0x66) },
        };
        text.SetResourceReference(TextBlock.FontFamilyProperty, "Led.FontFamily");
        text.SetResourceReference(TextBlock.ForegroundProperty, "Led.DoneBrush");

        var alert = new Border
        {
            CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 24, 28),
            Child = text,
        };
        alert.SetResourceReference(Border.BackgroundProperty, "Led.BackgroundBrush");
        alert.SetResourceReference(Border.BorderBrushProperty, "Led.BorderBrush");
        scene.Children.Add(alert);

        Layout(scene);
        return scene;
    }

    private static void Layout(FrameworkElement scene)
    {
        scene.Measure(new Size(scene.Width, scene.Height));
        scene.Arrange(new Rect(0, 0, scene.Width, scene.Height));
        scene.UpdateLayout();
    }

    private static void Save(FrameworkElement scene, string folder, string name)
    {
        Layout(scene);
        var bitmap = new RenderTargetBitmap(
            (int)Math.Ceiling(scene.Width * Scale), (int)Math.Ceiling(scene.Height * Scale), 96 * Scale, 96 * Scale, PixelFormats.Pbgra32);
        bitmap.Render(scene);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(System.IO.Path.Combine(folder, name));
        encoder.Save(file);
    }
}
