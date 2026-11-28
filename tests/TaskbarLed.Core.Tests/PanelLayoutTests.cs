using TaskbarLed.Core.Settings;
using TaskbarLed.Core.Visibility;

namespace TaskbarLed.Core.Tests;

public class PanelLayoutTests
{
    private static readonly PixelRect Monitor = new(0, 0, 1920, 1080);
    private static readonly PixelRect Taskbar = new(0, 1032, 1920, 1080); // 48 px tall, bottom

    private static PanelSettings InTaskbar(double x = 12, double width = 420) =>
        new() { Placement = PanelPlacement.Taskbar, TaskbarPosition = new(x, 0, width) };

    [Fact]
    public void Above_dock_is_centered_horizontally_and_sits_the_gap_above_the_work_area()
    {
        var workArea = new PixelRect(0, 25, 1440, 830); // menu bar on top, Dock below 830

        var rect = PanelLayout.AboveDock(workArea, 1.0, 420, 8);

        Assert.Equal(new PixelRect(510, 786, 930, 822), rect);
    }

    [Fact]
    public void Above_dock_scales_for_retina_and_stays_inside_a_narrow_work_area()
    {
        var retina = PanelLayout.AboveDock(new PixelRect(0, 50, 2880, 1660), 2.0, 420, 8);
        Assert.Equal(840, retina.Width);
        Assert.Equal(72, retina.Height);
        Assert.Equal(1660 - 16, retina.Bottom);
        Assert.Equal((2880 - 840) / 2, retina.Left);

        var narrow = PanelLayout.AboveDock(new PixelRect(100, 0, 400, 300), 1.0, 420, 8);
        Assert.Equal(100, narrow.Left);
        Assert.Equal(300, narrow.Width);
    }

    [Fact]
    public void Taskbar_mode_sits_inside_the_taskbar_vertically_centered()
    {
        var rect = PanelLayout.Compute(InTaskbar(), Monitor, Taskbar, 1.0);
        Assert.Equal(new PixelRect(12, 1038, 432, 1074), rect);
    }

    [Fact]
    public void Taskbar_mode_scales_with_monitor_dpi()
    {
        var monitor = new PixelRect(0, 0, 3840, 2160);
        var taskbar = new PixelRect(0, 2088, 3840, 2160); // 72 px at 150%
        var rect = PanelLayout.Compute(InTaskbar(10, 400), monitor, taskbar, 1.5);

        Assert.Equal(15, rect.Left);
        Assert.Equal(600, rect.Width);
        Assert.Equal(72 - 18, rect.Height);
    }

    [Fact]
    public void Taskbar_mode_never_leaves_the_taskbar()
    {
        var rect = PanelLayout.Compute(InTaskbar(5000, 5000), Monitor, Taskbar, 1.0);
        Assert.Equal(Taskbar.Left, rect.Left);
        Assert.Equal(Taskbar.Right, rect.Right);
    }

    // Windows 11 with centered icons: Start at 762, nine apps, tray on the right.
    private static readonly PixelRect[] Items =
    [
        PixelRect.FromSize(762, 1032, 45, 48),   // Start
        PixelRect.FromSize(807, 1032, 352, 48),  // apps up to 1159
        PixelRect.FromSize(1706, 1032, 214, 48), // tray
    ];

    [Fact]
    public void Taskbar_mode_shrinks_away_from_a_start_button_that_slid_under_it()
    {
        // Saved at 361..775: Start moved left when more apps opened and now starts at 762.
        var rect = PanelLayout.Compute(InTaskbar(361, 414), Monitor, Taskbar, 1.0, Items);

        Assert.Equal(361, rect.Left);
        Assert.Equal(762 - 4, rect.Right);
    }

    [Fact]
    public void Taskbar_mode_keeps_the_saved_spot_when_the_buttons_are_out_of_the_way()
    {
        var rect = PanelLayout.Compute(InTaskbar(361, 300), Monitor, Taskbar, 1.0, Items);

        Assert.Equal(new PixelRect(361, 1038, 661, 1074), rect);
    }

    [Fact]
    public void Taskbar_mode_moves_off_the_buttons_once_it_cannot_shrink_further()
    {
        var rect = PanelLayout.Compute(InTaskbar(700, 420), Monitor, Taskbar, 1.0, Items);

        Assert.Equal(762 - 4, rect.Right);
        Assert.Equal(160, rect.Width);
    }

    [Fact]
    public void Taskbar_mode_never_covers_a_button_at_any_dpi()
    {
        var monitor = new PixelRect(0, 0, 3840, 2160);
        var taskbar = new PixelRect(0, 2088, 3840, 2160);
        PixelRect[] items = [PixelRect.FromSize(1500, 2088, 800, 72), PixelRect.FromSize(3400, 2088, 440, 72)];

        foreach (var x in new[] { 0, 500, 900, 1200, 1600, 2000 })
        {
            var rect = PanelLayout.Compute(InTaskbar(x, 420), monitor, taskbar, 1.5, items);
            Assert.All(items, item => Assert.True(rect.Intersect(item).IsEmpty, $"x={x}: {rect} covers {item}"));
            Assert.True(rect.Width >= 240, $"x={x}: {rect} is narrower than the minimum");
        }
    }

    [Fact]
    public void Taskbar_items_do_not_change_the_saved_position()
    {
        var settings = InTaskbar(361, 414);
        var rect = PanelLayout.Compute(settings, Monitor, Taskbar, 1.0, Items);

        Assert.Equal(414, settings.CurrentPosition.Width);
        Assert.NotEqual(414, rect.Width);
    }

    [Fact]
    public void Above_mode_floats_over_the_taskbar_with_the_gap()
    {
        var settings = new PanelSettings { Placement = PanelPlacement.AboveTaskbar, AbovePosition = new(20, 0, 400), AboveGap = 8 };

        var rect = PanelLayout.Compute(settings, Monitor, Taskbar, 1.0);

        Assert.Equal(Taskbar.Top - 8, rect.Bottom);
        Assert.Equal(36, rect.Height);
        Assert.Equal(20, rect.Left);
    }

    [Fact]
    public void Auto_hidden_taskbar_makes_taskbar_mode_fall_back_to_above_the_screen_edge()
    {
        var hidden = new PixelRect(0, 1078, 1920, 1126); // only 2 px visible

        var rect = PanelLayout.Compute(InTaskbar(), Monitor, hidden, 1.0);

        Assert.Equal(Monitor.Bottom - 8, rect.Bottom);
    }

    [Fact]
    public void Free_mode_uses_monitor_coordinates_and_stays_on_screen()
    {
        var settings = new PanelSettings { Placement = PanelPlacement.Free, FreePosition = new(100, 50, 400) };
        Assert.Equal(PixelRect.FromSize(100, 50, 400, 36), PanelLayout.Compute(settings, Monitor, Taskbar, 1.0));

        var offScreen = settings with { FreePosition = new(5000, 5000, 400) };
        var rect = PanelLayout.Compute(offScreen, Monitor, Taskbar, 1.0);
        Assert.Equal(Monitor.Right, rect.Right);
        Assert.Equal(Monitor.Bottom, rect.Bottom);
    }

    [Theory]
    [InlineData(PanelPlacement.Taskbar)]
    [InlineData(PanelPlacement.AboveTaskbar)]
    [InlineData(PanelPlacement.Free)]
    public void Position_round_trips_through_pixels(PanelPlacement placement)
    {
        var settings = new PanelSettings { Placement = placement }.WithCurrentPosition(new(80, 40, 300));
        var rect = PanelLayout.Compute(settings, Monitor, Taskbar, 1.25);

        var position = PanelLayout.ToPosition(settings, rect, Monitor, Taskbar, 1.25);

        Assert.Equal(80, position.X, 3);
        Assert.Equal(300, position.Width, 3);
        Assert.Equal(placement == PanelPlacement.Free ? 40 : 0, position.Y, 3);
    }

    [Fact]
    public void Each_placement_remembers_its_own_position()
    {
        var settings = new PanelSettings().WithCurrentPosition(new(111, 0, 420));
        var free = (settings with { Placement = PanelPlacement.Free }).WithCurrentPosition(new(222, 33, 420));

        Assert.Equal(111, (free with { Placement = PanelPlacement.Taskbar }).CurrentPosition.X);
        Assert.Equal(222, free.CurrentPosition.X);
    }

    [Fact]
    public void Snap_pulls_near_edges_and_ignores_far_ones()
    {
        var nearCorner = PixelRect.FromSize(7, 9, 300, 36);
        Assert.Equal(PixelRect.FromSize(0, 0, 300, 36), PanelLayout.Snap(nearCorner, Monitor, Taskbar, 1.0));

        var nearTaskbar = PixelRect.FromSize(500, Taskbar.Top - 36 - 5, 300, 36);
        Assert.Equal(Taskbar.Top, PanelLayout.Snap(nearTaskbar, Monitor, Taskbar, 1.0).Bottom);

        var middle = PixelRect.FromSize(500, 500, 300, 36);
        Assert.Equal(middle, PanelLayout.Snap(middle, Monitor, Taskbar, 1.0));
    }

    [Fact]
    public void Fullscreen_companions_sit_on_the_bottom_edge_and_corner()
    {
        Assert.Equal(new PixelRect(0, 1077, 1920, 1080), PanelLayout.EdgeStrip(Monitor, 1.0));

        var mini = PanelLayout.MiniCorner(Monitor, 1.0);
        Assert.Equal(1900, mini.Right);
        Assert.Equal(1060, mini.Bottom);

        var alert = PanelLayout.Alert(Monitor, 1.0, 300, 30);
        Assert.Equal(1900, alert.Right);
        Assert.True(alert.Bottom < 1077, "alert stays above the edge glow");
    }

    [Fact]
    public void A_panel_dropped_on_the_taskbar_goes_into_the_taskbar()
    {
        var free = new PanelSettings { Placement = PanelPlacement.Free, FreePosition = new(300, 200, 420) };

        var moved = PanelLayout.MoveTo(free, PixelRect.FromSize(700, 1030, 420, 36), Monitor, Taskbar, 1.0);

        Assert.Equal(PanelPlacement.Taskbar, moved.Placement);
        Assert.Equal(new PanelPosition(700, 0, 420), moved.TaskbarPosition);
        Assert.Equal(new PanelPosition(300, 200, 420), moved.FreePosition); // Each mode keeps its own spot.
    }

    [Fact]
    public void A_panel_dropped_just_above_the_taskbar_floats_above_it()
    {
        var moved = PanelLayout.MoveTo(InTaskbar(), PixelRect.FromSize(500, 980, 420, 36), Monitor, Taskbar, 1.0);

        Assert.Equal(PanelPlacement.AboveTaskbar, moved.Placement);
        Assert.Equal(500, moved.AbovePosition.X);
        Assert.Equal(new PixelRect(500, 988, 920, 1024), PanelLayout.Compute(moved, Monitor, Taskbar, 1.0));
    }

    [Fact]
    public void A_panel_dropped_higher_up_floats_freely_where_it_was_dropped()
    {
        var moved = PanelLayout.MoveTo(InTaskbar(), PixelRect.FromSize(640, 400, 420, 36), Monitor, Taskbar, 1.0);

        Assert.Equal(PanelPlacement.Free, moved.Placement);
        Assert.Equal(new PanelPosition(640, 400, 420), moved.FreePosition);
    }

    [Fact]
    public void A_freely_dropped_panel_snaps_to_a_nearby_screen_edge()
    {
        var moved = PanelLayout.MoveTo(InTaskbar(), PixelRect.FromSize(7, 5, 420, 36), Monitor, Taskbar, 1.0);

        Assert.Equal(new PanelPosition(0, 0, 420), moved.FreePosition);
    }

    [Theory]
    [InlineData(1014, PanelPlacement.Taskbar)]      // middle at 1032: on the taskbar's top edge
    [InlineData(1013, PanelPlacement.AboveTaskbar)] // middle at 1031: half over, mostly above
    [InlineData(972, PanelPlacement.AboveTaskbar)]  // bottom at 1008: exactly 24 px above
    [InlineData(971, PanelPlacement.Free)]          // bottom at 1007: past the band
    public void Placement_follows_where_the_panel_is_dropped(int top, PanelPlacement expected)
    {
        var placement = PanelLayout.PlacementAt(PixelRect.FromSize(0, top, 420, 36), Monitor, Taskbar, 1.0);

        Assert.Equal(expected, placement);
    }

    [Fact]
    public void Without_a_usable_taskbar_every_drop_is_free()
    {
        var hidden = new PixelRect(0, 1078, 1920, 1126); // auto-hidden: only a sliver on screen

        Assert.Equal(PanelPlacement.Free, PanelLayout.PlacementAt(PixelRect.FromSize(0, 1040, 420, 36), Monitor, hidden, 1.0));
        Assert.Equal(PanelPlacement.Free, PanelLayout.PlacementAt(PixelRect.FromSize(0, 1040, 420, 36), Monitor, null, 1.0));
    }

    [Fact]
    public void The_above_taskbar_band_scales_with_dpi()
    {
        var monitor = new PixelRect(0, 0, 3840, 2160);
        var taskbar = new PixelRect(0, 2088, 3840, 2160);

        // 30 px above the taskbar is inside the 24 DIP band at 150% (36 px), outside it at 100%.
        Assert.Equal(PanelPlacement.AboveTaskbar, PanelLayout.PlacementAt(PixelRect.FromSize(0, 2004, 600, 54), monitor, taskbar, 1.5));
        Assert.Equal(PanelPlacement.Free, PanelLayout.PlacementAt(PixelRect.FromSize(0, 2022, 600, 36), monitor, taskbar, 1.0));
    }
}
