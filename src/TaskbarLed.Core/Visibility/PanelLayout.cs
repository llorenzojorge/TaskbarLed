using TaskbarLed.Core.Settings;

namespace TaskbarLed.Core.Visibility;

/// <summary>
/// Where the panel and its full-screen companions go on a monitor. Pure math in physical pixels,
/// so every placement rule is unit-tested without Windows.
/// </summary>
public static class PanelLayout
{
    /// <summary>Gap between the panel and the taskbar's top and bottom edges, in DIP.</summary>
    public const double VerticalMarginDip = 6;

    /// <summary>Panel height when it is not inside the taskbar, in DIP.</summary>
    public const double FloatingHeightDip = 36;

    /// <summary>Smallest usable panel width, in DIP.</summary>
    public const double MinWidthDip = 160;

    /// <summary>Distance under which a dragged panel snaps to an edge, in DIP.</summary>
    public const double SnapDistanceDip = 12;

    /// <summary>Margin of corner overlays (mini panel, alerts) from the screen edges, in DIP.</summary>
    public const double CornerMarginDip = 20;

    /// <summary>Thickness of the full-screen edge glow, in DIP.</summary>
    public const double EdgeThicknessDip = 3;

    /// <summary>Mini panel width in full screen, in DIP.</summary>
    public const double MiniWidthDip = 340;

    /// <summary>Space kept between the panel (halo included) and any taskbar button, in DIP.</summary>
    public const double ItemClearanceDip = 4;

    /// <summary>Panel rectangle for <paramref name="settings"/> on a monitor.</summary>
    /// <param name="settings">Placement and remembered positions.</param>
    /// <param name="monitor">Monitor rectangle.</param>
    /// <param name="taskbar">The monitor's taskbar, if any.</param>
    /// <param name="scale">Monitor scale factor (1.0 = 96 DPI).</param>
    /// <param name="taskbarItems">
    /// Buttons on the taskbar (Start, apps, tray). In taskbar mode the panel never covers them:
    /// see <see cref="AvoidItems"/>. Null or empty keeps the saved position as is.
    /// </param>
    public static PixelRect Compute(PanelSettings settings, PixelRect monitor, PixelRect? taskbar, double scale, IReadOnlyList<PixelRect>? taskbarItems = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(scale, 0);

        var position = settings.CurrentPosition;
        var bar = UsableTaskbar(taskbar, monitor);

        return settings.Placement switch
        {
            PanelPlacement.Taskbar when bar is { } b => AvoidItems(InTaskbar(b, scale, position), b, taskbarItems, scale),
            PanelPlacement.Taskbar or PanelPlacement.AboveTaskbar => AboveTaskbar(bar, monitor, scale, position, settings.AboveGap),
            _ => Free(monitor, scale, position),
        };
    }

    /// <summary>Converts a panel rectangle back into the position stored for the current placement.</summary>
    public static PanelPosition ToPosition(PanelSettings settings, PixelRect rect, PixelRect monitor, PixelRect? taskbar, double scale)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(scale, 0);

        var width = rect.Width / scale;
        if (settings.Placement == PanelPlacement.Free)
        {
            return new PanelPosition((rect.Left - monitor.Left) / scale, (rect.Top - monitor.Top) / scale, width);
        }

        var anchorLeft = UsableTaskbar(taskbar, monitor)?.Left ?? monitor.Left;
        return new PanelPosition(Math.Max(0, (rect.Left - anchorLeft) / scale), 0, width);
    }

    /// <summary>
    /// How far above the taskbar a dropped panel still counts as "above the taskbar", in DIP. Further up it
    /// floats freely.
    /// </summary>
    public const double AboveTaskbarBandDip = 24;

    /// <summary>
    /// The placement a panel dragged to <paramref name="rect"/> belongs to: <see cref="PanelPlacement.Taskbar"/>
    /// when its middle is on the taskbar, <see cref="PanelPlacement.AboveTaskbar"/> when its bottom is within
    /// <see cref="AboveTaskbarBandDip"/> above it, otherwise <see cref="PanelPlacement.Free"/> (also whenever the
    /// taskbar is hidden or not at the bottom).
    /// </summary>
    public static PanelPlacement PlacementAt(PixelRect rect, PixelRect monitor, PixelRect? taskbar, double scale)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(scale, 0);

        if (UsableTaskbar(taskbar, monitor) is not { } bar)
        {
            return PanelPlacement.Free;
        }

        if ((rect.Top + rect.Bottom) / 2 >= bar.Top)
        {
            return PanelPlacement.Taskbar;
        }

        return rect.Bottom >= bar.Top - (int)Math.Round(AboveTaskbarBandDip * scale)
            ? PanelPlacement.AboveTaskbar
            : PanelPlacement.Free;
    }

    /// <summary>
    /// Settings after the panel is dragged to <paramref name="rect"/> anywhere on the monitor: the placement is
    /// picked from where it is (<see cref="PlacementAt"/>) and that placement's position is updated. A free
    /// panel snaps to nearby edges. The other placements keep their remembered spots.
    /// </summary>
    public static PanelSettings MoveTo(PanelSettings settings, PixelRect rect, PixelRect monitor, PixelRect? taskbar, double scale)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var placement = PlacementAt(rect, monitor, taskbar, scale);
        if (placement == PanelPlacement.Free)
        {
            rect = Snap(rect, monitor, taskbar, scale);
        }

        var moved = settings with { Placement = placement };
        return moved.WithCurrentPosition(ToPosition(moved, rect, monitor, taskbar, scale)).Normalize();
    }

    /// <summary>Pulls a dragged rectangle onto nearby monitor or taskbar edges.</summary>
    public static PixelRect Snap(PixelRect rect, PixelRect monitor, PixelRect? taskbar, double scale)
    {
        var distance = (int)Math.Round(SnapDistanceDip * scale);
        var bottomEdge = UsableTaskbar(taskbar, monitor)?.Top ?? monitor.Bottom;
        int dx = 0, dy = 0;

        if (Math.Abs(rect.Left - monitor.Left) <= distance)
        {
            dx = monitor.Left - rect.Left;
        }
        else if (Math.Abs(monitor.Right - rect.Right) <= distance)
        {
            dx = monitor.Right - rect.Right;
        }

        if (Math.Abs(rect.Top - monitor.Top) <= distance)
        {
            dy = monitor.Top - rect.Top;
        }
        else if (Math.Abs(bottomEdge - rect.Bottom) <= distance)
        {
            dy = bottomEdge - rect.Bottom;
        }

        return Offset(rect, dx, dy);
    }

    /// <summary>
    /// Floating panel centered horizontally just above the bottom of the work area (macOS: above the Dock).
    /// The work area already excludes the Dock and the menu bar, so this also works with a side or hidden Dock.
    /// </summary>
    /// <param name="workArea">Usable screen area, in physical pixels.</param>
    /// <param name="scale">Screen scale factor (1.0 = 96 DPI on Windows, 2.0 on Retina).</param>
    /// <param name="widthDip">Wanted panel width; clamped to <see cref="MinWidthDip"/> and the work area.</param>
    /// <param name="gapDip">Space between the panel and the bottom of the work area.</param>
    public static PixelRect AboveDock(PixelRect workArea, double scale, double widthDip, double gapDip)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(scale, 0);

        var height = Math.Min((int)Math.Round(FloatingHeightDip * scale), workArea.Height);
        var width = ClampWidth(widthDip, scale, workArea.Width);
        var left = workArea.Left + ((workArea.Width - width) / 2);
        var bottom = workArea.Bottom - (int)Math.Round(Math.Max(0, gapDip) * scale);
        return PixelRect.FromSize(left, Math.Max(workArea.Top, bottom - height), width, height);
    }

    /// <summary>Compact panel in the bottom-right corner, used over full-screen content.</summary>
    public static PixelRect MiniCorner(PixelRect monitor, double scale)
    {
        var width = (int)Math.Round(MiniWidthDip * scale);
        var height = (int)Math.Round(FloatingHeightDip * scale);
        var margin = (int)Math.Round(CornerMarginDip * scale);
        return PixelRect.FromSize(monitor.Right - margin - width, monitor.Bottom - margin - height, width, height);
    }

    /// <summary>Thin strip along the bottom edge of the monitor.</summary>
    public static PixelRect EdgeStrip(PixelRect monitor, double scale)
    {
        var thickness = Math.Max(2, (int)Math.Round(EdgeThicknessDip * scale));
        return new PixelRect(monitor.Left, monitor.Bottom - thickness, monitor.Right, monitor.Bottom);
    }

    /// <summary>Bottom-right anchored rectangle for an alert of the given pixel size.</summary>
    public static PixelRect Alert(PixelRect monitor, double scale, int width, int height)
    {
        var margin = (int)Math.Round(CornerMarginDip * scale);
        var aboveEdge = (int)Math.Round((CornerMarginDip + EdgeThicknessDip) * scale);
        return PixelRect.FromSize(monitor.Right - margin - width, monitor.Bottom - aboveEdge - height, width, height);
    }

    /// <summary>
    /// The taskbar, unless it is auto-hidden (only a sliver is on screen) or not at the bottom.
    /// </summary>
    public static PixelRect? UsableTaskbar(PixelRect? taskbar, PixelRect monitor)
    {
        if (taskbar is not { } bar)
        {
            return null;
        }

        var onScreen = bar.Intersect(monitor);
        var isBottom = bar.Width >= monitor.Width / 2 && bar.Bottom >= monitor.Bottom - 1;
        return !onScreen.IsEmpty && onScreen.Height >= bar.Height / 2 && isBottom ? bar : null;
    }

    /// <summary>
    /// Keeps a taskbar-mode panel off the taskbar's buttons. Windows 11 centers its icons, so the Start
    /// button moves left as apps open and can slide under a panel saved next to it. The panel stays in the
    /// free stretch it is mostly in, with <see cref="ItemClearanceDip"/> on each side (the blinking halo is
    /// drawn inside the window, so it never reaches a button either): it shrinks first, down to
    /// <see cref="MinWidthDip"/>, then moves away from the button. When that stretch is too small it goes
    /// to the widest one. The saved position is not changed, so the panel grows back when there is room.
    /// </summary>
    /// <param name="panel">Panel rectangle from the saved position.</param>
    /// <param name="taskbar">The taskbar the panel sits in.</param>
    /// <param name="items">Taskbar buttons, in physical pixels. Items outside the taskbar row are ignored.</param>
    /// <param name="scale">Monitor scale factor.</param>
    public static PixelRect AvoidItems(PixelRect panel, PixelRect taskbar, IReadOnlyList<PixelRect>? items, double scale)
    {
        if (items is null || items.Count == 0)
        {
            return panel;
        }

        var gaps = FreeStretches(taskbar, items, (int)Math.Round(ItemClearanceDip * scale));
        if (gaps.Count == 0)
        {
            return panel; // Nowhere free at all: keep the user's spot rather than vanish.
        }

        var minWidth = Math.Min((int)Math.Round(MinWidthDip * scale), panel.Width);
        var gap = gaps
            .Where(g => g.Right - g.Left >= minWidth)
            .OrderByDescending(g => Overlap(g, panel))
            .ThenBy(g => Distance(g, panel))
            .Select(g => ((int Left, int Right)?)g)
            .FirstOrDefault() ?? gaps.MaxBy(g => g.Right - g.Left);

        var left = Math.Max(panel.Left, gap.Left);
        var right = Math.Min(panel.Right, gap.Right);
        if (right - left < minWidth)
        {
            // Too thin where it is: keep the minimum width, pushed back inside the stretch.
            left = Math.Clamp(Math.Min(left, right - minWidth), gap.Left, Math.Max(gap.Left, gap.Right - minWidth));
            right = Math.Min(gap.Right, left + minWidth);
        }

        return panel with { Left = left, Right = right };
    }

    // Horizontal stretches of the taskbar not covered by any item (each widened by the clearance).
    private static List<(int Left, int Right)> FreeStretches(PixelRect taskbar, IReadOnlyList<PixelRect> items, int clearance)
    {
        var blocked = items
            .Where(i => !i.Intersect(taskbar).IsEmpty)
            .Select(i => (Left: i.Left - clearance, Right: i.Right + clearance))
            .OrderBy(i => i.Left);

        var gaps = new List<(int Left, int Right)>();
        var cursor = taskbar.Left;
        foreach (var (left, right) in blocked)
        {
            if (left > cursor)
            {
                gaps.Add((cursor, Math.Min(left, taskbar.Right)));
            }

            cursor = Math.Max(cursor, right);
        }

        if (cursor < taskbar.Right)
        {
            gaps.Add((cursor, taskbar.Right));
        }

        return gaps;
    }

    private static int Overlap((int Left, int Right) gap, PixelRect panel) =>
        Math.Max(0, Math.Min(gap.Right, panel.Right) - Math.Max(gap.Left, panel.Left));

    private static int Distance((int Left, int Right) gap, PixelRect panel) =>
        Math.Max(0, Math.Max(gap.Left - panel.Right, panel.Left - gap.Right));

    private static PixelRect InTaskbar(PixelRect taskbar, double scale, PanelPosition position)
    {
        var margin = (int)Math.Round(VerticalMarginDip * scale);
        var height = Math.Max(8, taskbar.Height - (2 * margin));
        var width = ClampWidth(position.Width, scale, taskbar.Width);
        var offset = Math.Min((int)Math.Round(Math.Max(0, position.X) * scale), taskbar.Width - width);
        return PixelRect.FromSize(taskbar.Left + offset, taskbar.Top + ((taskbar.Height - height) / 2), width, height);
    }

    private static PixelRect AboveTaskbar(PixelRect? taskbar, PixelRect monitor, double scale, PanelPosition position, double gapDip)
    {
        var anchor = taskbar ?? new PixelRect(monitor.Left, monitor.Bottom, monitor.Right, monitor.Bottom);
        var height = (int)Math.Round(FloatingHeightDip * scale);
        var width = ClampWidth(position.Width, scale, monitor.Width);
        var left = Math.Clamp(anchor.Left + (int)Math.Round(Math.Max(0, position.X) * scale), monitor.Left, monitor.Right - width);
        var bottom = anchor.Top - (int)Math.Round(gapDip * scale);
        return PixelRect.FromSize(left, Math.Max(monitor.Top, bottom - height), width, height);
    }

    private static PixelRect Free(PixelRect monitor, double scale, PanelPosition position)
    {
        var height = (int)Math.Round(FloatingHeightDip * scale);
        var width = ClampWidth(position.Width, scale, monitor.Width);
        var left = Math.Clamp(monitor.Left + (int)Math.Round(position.X * scale), monitor.Left, monitor.Right - width);
        var top = Math.Clamp(monitor.Top + (int)Math.Round(position.Y * scale), monitor.Top, monitor.Bottom - height);
        return PixelRect.FromSize(left, top, width, height);
    }

    private static int ClampWidth(double widthDip, double scale, int available) =>
        Math.Min((int)Math.Round(Math.Max(MinWidthDip, widthDip) * scale), Math.Max(1, available));

    private static PixelRect Offset(PixelRect r, int dx, int dy) => new(r.Left + dx, r.Top + dy, r.Right + dx, r.Bottom + dy);
}
