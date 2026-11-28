using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using TaskbarLed.App.Avalonia.Panels;
using TaskbarLed.App.Avalonia.Presentation;
using TaskbarLed.Core.Presentation;
using TaskbarLed.Core.Sessions;

namespace TaskbarLed.App.Avalonia.Tests;

public class PanelCustomizationTests
{
    [AvaloniaFact]
    public void All_themes_apply_to_the_same_panel()
    {
        var model = Model();
        var app = Application.Current!;
        var backgrounds = new HashSet<Color>();
        foreach (var theme in PanelThemes.All)
        {
            Assert.Equal(theme, PanelThemes.Apply(app, model, theme.Id));
            Assert.Equal(theme.Uppercase, model.Uppercase);
            backgrounds.Add(Assert.IsType<SolidColorBrush>(app.Resources["Led.BackgroundBrush"]).Color);
            Assert.IsAssignableFrom<IBrush>(app.Resources["Led.DotGridBrush"]);
        }

        Assert.Equal(19, backgrounds.Count);
        Assert.All(PanelThemes.All, theme =>
        {
            var option = App.CreateThemeMenuItem(theme);
            Assert.Equal(theme.Name, option.Header);
            Assert.DoesNotContain("•", theme.Name, StringComparison.Ordinal);
            Assert.NotNull(option.HeaderTemplate);
        });
        Assert.Equal("AmberTree", PanelThemes.Apply(app, model, "AmberTree").Id);
        Assert.Equal(Color.Parse("#48483B"), Assert.IsType<SolidColorBrush>(app.Resources["Led.SessionTreeBrush"]).Color);
        Assert.Equal("AmberRail", PanelThemes.Apply(app, model, "AmberRail").Id);
        Assert.Equal(Color.Parse("#B88953"), Assert.IsType<SolidColorBrush>(app.Resources["Led.SessionRailBrush"]).Color);
        Assert.Equal("ClaudeCode", PanelThemes.Apply(app, model, "unknown").Id);
        Assert.Equal("✻", app.Resources["Led.FeedPrefix"]);
    }

    [AvaloniaFact]
    public void Drag_and_resize_save_coordinates_and_cancel_restores_them()
    {
        var panel = new FloatingPanelWindow(Model(), () => PanelFrame.Empty("Idle"), keepVisible: true);
        PanelPreferences? saved = null;
        panel.ConfigurePlacement(new PanelPreferences(X: 200, Y: 200), value => saved = value);
        panel.Show();
        try
        {
            panel.BeginEdit();
            Assert.IsType<ComboBox>(EditorFooter(panel).Children[0]).SelectedIndex = 1;
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var preview = Assert.IsType<Grid>(EditorGrid(panel).Children[0]);
            Drag(panel, new Point(40, 15), new Point(120, 50));
            Assert.NotEqual(new PixelPoint(200, 200), panel.Position);
            var widthBefore = panel.Width;
            var heightBefore = preview.Bounds.Height;
            Drag(panel, new Point(widthBefore - 5, heightBefore - 5), new Point(widthBefore + 48, heightBefore + 33));
            Assert.True(panel.Width > widthBefore);
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.True(preview.Bounds.Height > heightBefore);
            Assert.IsType<Slider>(Assert.IsType<StackPanel>(EditorGrid(panel).Children[2]).Children[1]).Value = 80;
            ClickEditorButton(panel, 0);
            Assert.NotNull(saved);
            Assert.Equal(panel.Position.X, saved.X);
            Assert.Equal(panel.Position.Y, saved.Y);
            Assert.Equal(panel.Width, saved.Width);
            Assert.True(panel.Height > heightBefore);
            Assert.False(saved.AutomaticSize);
            Assert.Equal(panel.Height, saved.Height);
            Assert.Equal(0.80, saved.PanelOpacity);

            var position = panel.Position;
            var width = panel.Width;
            panel.BeginEdit();
            Drag(panel, new Point(40, 15), new Point(95, 55));
            Assert.NotEqual(position, panel.Position);
            Assert.IsType<Slider>(Assert.IsType<StackPanel>(EditorGrid(panel).Children[2]).Children[1]).Value = 90;
            ClickEditorButton(panel, 1);
            Assert.Equal(0.80, Assert.IsType<PanelViewModel>(Assert.IsType<PanelView>(panel.Content).DataContext).PanelOpacity);
            Assert.Equal(position, panel.Position);
            Assert.Equal(width, panel.Width);
        }
        finally
        {
            panel.Close();
        }
    }

    [AvaloniaFact]
    public void Automatic_mode_follows_session_text_and_worker_count()
    {
        var model = Model();
        PanelThemes.Apply(Application.Current!, model, "ClaudeCode");
        var frame = PanelFrame.Empty("Idle");
        var panel = new FloatingPanelWindow(model, () => frame, keepVisible: true);
        panel.Show();
        try
        {
            var initial = panel.ClientSize;
            frame = frame with
            {
                SessionRows =
                [
                    new("agent", "Antigravity → Configurar aprendizado de alemão e revisar vocabulário", SessionStatus.Running, true),
                    new("worker", "Pesquisador", SessionStatus.Running, false),
                    new("worker2", "Revisor", SessionStatus.Running, false),
                ],
            };
            panel.ConfigurePlacement(new PanelPreferences(AutomaticSize: true), _ => { });
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.True(panel.ClientSize.Width > initial.Width);
            Assert.True(panel.ClientSize.Height > initial.Height);
        }
        finally
        {
            panel.Close();
        }
    }

    private static PanelViewModel Model() => new(CultureInfo.GetCultureInfo("en-US"));

    private static void Drag(FloatingPanelWindow panel, Point start, Point end)
    {
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        panel.MouseDown(start, MouseButton.Left);
        panel.MouseMove(end, RawInputModifiers.LeftMouseButton);
        panel.MouseUp(end, MouseButton.Left);
    }

    private static void ClickEditorButton(FloatingPanelWindow panel, int index)
    {
        var button = Assert.IsType<Button>(EditorFooter(panel).Children[index + 1]);
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }
    private static Grid EditorGrid(FloatingPanelWindow panel) =>
        Assert.IsType<Grid>(Assert.IsType<Border>(panel.Content).Child);

    private static StackPanel EditorFooter(FloatingPanelWindow panel) =>
        Assert.IsType<StackPanel>(EditorGrid(panel).Children[1]);

    [Fact]
    public void Worker_elapsed_time_updates_the_existing_row()
    {
        var model = Model();
        SessionDisplayRow worker = new("worker", "Subagent api", SessionStatus.Running, false)
            { ElapsedSeconds = 62, IsElapsedApproximate = true };
        var frame = PanelFrame.Empty("");
        model.Apply(frame with { SessionRows = [worker] });
        var row = Assert.Single(model.SessionRows);
        Assert.Contains("~01:02", row.Status, StringComparison.Ordinal);
        Assert.DoesNotContain("tok", row.Status, StringComparison.Ordinal);

        model.Apply(frame with { SessionRows = [worker with { ElapsedSeconds = 63 }] });
        Assert.Same(row, Assert.Single(model.SessionRows));
        Assert.Contains("~01:03", row.Status, StringComparison.Ordinal);
    }
}
