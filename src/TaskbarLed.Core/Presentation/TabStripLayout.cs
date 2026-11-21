namespace TaskbarLed.Core.Presentation;

/// <summary>
/// Shares the tab row between session tabs. Every tab gets its full title when they all fit. When they do
/// not, the longest titles are shortened first, to a common cap, so short names stay whole and no tab
/// takes the room of the others. Pure math, shared by the Windows and macOS panels.
/// </summary>
public static class TabStripLayout
{
    /// <summary>Widths for tabs that would like <paramref name="desired"/>, in a row <paramref name="available"/> wide.</summary>
    /// <param name="desired">Each tab's width with its whole title.</param>
    /// <param name="available">Width of the row.</param>
    /// <param name="minWidth">No tab gets less (number, a letter or two and the status mark), unless its title is shorter.</param>
    public static double[] Fit(IReadOnlyList<double> desired, double available, double minWidth)
    {
        ArgumentNullException.ThrowIfNull(desired);

        var widths = desired.Select(d => Math.Max(0, d)).ToArray();
        if (widths.Sum() <= available || widths.Length == 0)
        {
            return widths;
        }

        // Find the cap: tabs narrower than it keep their width, the others get exactly the cap.
        var sorted = widths.Order().ToArray();
        var remaining = Math.Max(0, available);
        var cap = 0d;
        for (var i = 0; i < sorted.Length; i++)
        {
            var share = remaining / (sorted.Length - i);
            if (sorted[i] > share)
            {
                cap = share;
                break;
            }

            remaining -= sorted[i];
        }

        return [.. widths.Select(w => Math.Min(w, Math.Max(cap, Math.Min(w, minWidth))))];
    }
}
