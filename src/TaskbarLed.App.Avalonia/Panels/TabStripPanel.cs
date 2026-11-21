using Avalonia;
using Avalonia.Controls;
using TaskbarLed.Core.Presentation;

namespace TaskbarLed.App.Avalonia.Panels;

/// <summary>
/// Horizontal row of session tabs that adapts to the panel's width (<see cref="TabStripLayout"/>).
/// Mirrors <c>TaskbarLed.App.Panels.TabStripPanel</c> (WPF).
/// </summary>
public sealed class TabStripPanel : Panel
{
    /// <summary>Narrowest a tab gets: number, a letter or two and the status mark.</summary>
    public const double MinTabWidth = 44;

    private double[] _widths = [];

    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (var child in Children)
        {
            child.Measure(new Size(double.PositiveInfinity, availableSize.Height));
        }

        _widths = TabStripLayout.Fit([.. Children.Select(c => c.DesiredSize.Width)], availableSize.Width, MinTabWidth);

        var height = 0d;
        for (var i = 0; i < Children.Count; i++)
        {
            Children[i].Measure(new Size(_widths[i], availableSize.Height));
            height = Math.Max(height, Children[i].DesiredSize.Height);
        }

        var total = _widths.Sum();
        return new Size(double.IsInfinity(availableSize.Width) ? total : Math.Min(total, availableSize.Width), height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var x = 0d;
        for (var i = 0; i < Children.Count; i++)
        {
            var width = i < _widths.Length ? _widths[i] : Children[i].DesiredSize.Width;
            Children[i].Arrange(new Rect(x, 0, width, finalSize.Height));
            x += width;
        }

        return finalSize;
    }
}
