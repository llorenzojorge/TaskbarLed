using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using TaskbarLed.App.Avalonia.Panels;
using TaskbarLed.App.Avalonia.Presentation;
using TaskbarLed.Core.Presentation;
using TaskbarLed.Core.Sessions;

namespace TaskbarLed.App.Avalonia.Tests;

public class ThemeGalleryTests
{
    [AvaloniaFact]
    public void Every_theme_renders_a_provider_neutral_session_hierarchy()
    {
        var folder = Environment.GetEnvironmentVariable("TASKBARLED_THEME_GALLERY");
        if (folder is not null)
        {
            Directory.CreateDirectory(folder);
        }

        var model = new PanelViewModel(CultureInfo.GetCultureInfo("pt-BR"));
        var view = new PanelView { DataContext = model };
        var window = new Window
        {
            Width = 598,
            Height = 223,
            Background = new SolidColorBrush(Color.Parse("#53606C")),
            Content = new Border { Padding = new Thickness(10), Child = view },
        };
        model.Apply(PanelFrame.Empty("") with
        {
            SessionRows =
            [
                new("codex", "Codex → Referências do curso", SessionStatus.Running, true)
                    { Engine = "Codex", RouteLetter = "A", Heading = "Referências do curso" },
                new("research", "Subagente Pesquisador", SessionStatus.Running, false)
                    { Engine = "Codex", RouteLetter = "A", StationName = "Pesquisador" },
                new("review", "Subagente Revisor", SessionStatus.Completed, false, 8)
                    { Engine = "Codex", RouteLetter = "A", StationName = "Revisor", LastStation = true },
                new("agy", "Antigravity → Alemão", SessionStatus.Idle, true) { Engine = "Antigravity", RouteLetter = "B", Heading = "Alemão" },
                new("vocabulary", "Subagente Vocabulário", SessionStatus.Running, false)
                    { Engine = "Antigravity", RouteLetter = "B", StationName = "Vocabulário" },
                new("grammar", "Subagente Gramática", SessionStatus.AwaitingInput, false)
                    { Engine = "Antigravity", RouteLetter = "B", StationName = "Gramática", LastStation = true },
            ],
        });
        window.Show();
        try
        {
            foreach (var theme in PanelThemes.All)
            {
                PanelThemes.Apply(Application.Current!, model, theme.Id);
                using var bitmap = window.CaptureRenderedFrame();
                Assert.NotNull(bitmap);
                Assert.Equal(6, model.SessionRows.Count);
                Assert.Equal(2, model.SessionGroups.Count);
                if (theme.Id == "OliveLcd")
                {
                    var content = view.FindControl<Grid>("PanelContent")!;
                    var frame = view.FindControl<Border>("PanelFrame")!;
                    Assert.True(content.Bounds.Top > frame.BorderThickness.Top);
                    Assert.True(content.Bounds.Left > frame.BorderThickness.Left);
                    Assert.True(content.Bounds.Bottom < frame.Bounds.Height - frame.BorderThickness.Bottom);
                    Assert.True(content.Bounds.Right < frame.Bounds.Width - frame.BorderThickness.Right);
                }
                if (folder is not null)
                {
                    bitmap.Save(Path.Combine(folder, theme.Id + ".png"), new PngBitmapEncoderOptions());
                }
            }
        }
        finally
        {
            window.Close();
        }
    }
    [AvaloniaFact]
    public void Every_theme_keeps_the_island_count_readable_with_one_brand_header()
    {
        var model = new PanelViewModel(CultureInfo.GetCultureInfo("pt-BR"))
        {
            IslandMode = true,
            IslandHeaderHeight = 30,
            IslandActivityCount = 12,
        };
        var view = new PanelView { DataContext = model, Classes = { "dynamic-island" } };
        var window = new Window { Width = 320, Height = 34, Content = view };
        window.Show();
        try
        {
            foreach (var theme in PanelThemes.All)
            {
                var app = Application.Current!;
                PanelThemes.Apply(app, model, theme.Id);
                var background = Assert.IsAssignableFrom<ISolidColorBrush>(app.Resources["Led.IslandBadgeBackground"]);
                var foreground = Assert.IsAssignableFrom<ISolidColorBrush>(app.Resources["Led.IslandBadgeForeground"]);
                Assert.True(PanelThemes.Contrast(background.Color, foreground.Color) >= 4.5, theme.Id);

                model.IslandExpanded = false;
                window.Height = 34;
                using var compact = window.CaptureRenderedFrame();
                Assert.NotNull(compact);
                var badge = view.FindControl<Border>("IslandActivityBadge")!;
                Assert.True(badge.Bounds.Height > 0 && badge.Bounds.Height <= 30, theme.Id);
                Assert.False(view.FindControl<Grid>("PanelContent")!.IsVisible);

                model.IslandExpanded = true;
                window.Height = 220;
                using var expanded = window.CaptureRenderedFrame();
                Assert.NotNull(expanded);
                Assert.True(view.FindControl<Grid>("PanelContent")!.IsVisible);
                Assert.False(view.FindControl<Grid>("PanelBrand")!.IsVisible);
            }
        }
        finally
        {
            window.Close();
        }
    }

}
