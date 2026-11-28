using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using TaskbarLed.Core.Presentation;

namespace TaskbarLed.App.Panels;

/// <summary>
/// Horizontal row of session tabs that adapts to the panel's width: full chat titles when there is room,
/// the longest ones shortened first when there is not (<see cref="TabStripLayout"/>). Tabs trim their own
/// text, so a narrower slot shows "1 Bug visual da…" and keeps its status mark.
/// </summary>
public sealed class TabStripPanel : Panel
{
    /// <summary>Narrowest a tab gets: number, a letter or two and the status mark.</summary>
    public const double MinTabWidth = 44;

    private readonly HashSet<INotifyPropertyChanged> _watched = [];
    private double[] _widths = [];

    protected override Size MeasureOverride(Size availableSize)
    {
        var children = InternalChildren.Cast<UIElement>().ToList();
        WatchItems(children);
        foreach (var child in children)
        {
            child.Measure(new Size(double.PositiveInfinity, availableSize.Height));
        }

        _widths = TabStripLayout.Fit([.. children.Select(c => c.DesiredSize.Width)], availableSize.Width, MinTabWidth);

        var height = 0d;
        for (var i = 0; i < children.Count; i++)
        {
            children[i].Measure(new Size(_widths[i], availableSize.Height));
            height = Math.Max(height, children[i].DesiredSize.Height);
        }

        return new Size(double.IsInfinity(availableSize.Width) ? _widths.Sum() : Math.Min(_widths.Sum(), availableSize.Width), height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var x = 0d;
        var children = InternalChildren.Cast<UIElement>().ToList();
        for (var i = 0; i < children.Count; i++)
        {
            var width = i < _widths.Length ? _widths[i] : children[i].DesiredSize.Width;
            children[i].Arrange(new Rect(x, 0, width, finalSize.Height));
            x += width;
        }

        return finalSize;
    }

    // Tabs are updated in place. When a title changes (the folder name first, the chat title later), WPF
    // re-measures that tab only, inside the slot it already had, so its desired width never grows and this
    // panel is never asked again: the tab stayed as narrow as "1 TaskbarLed" with room to spare. Any change
    // to a tab's item measures the whole row again.
    private void WatchItems(List<UIElement> children)
    {
        var current = children
            .OfType<FrameworkElement>()
            .Select(c => c.DataContext)
            .OfType<INotifyPropertyChanged>()
            .ToHashSet();

        foreach (var gone in _watched.Where(item => !current.Contains(item)).ToList())
        {
            gone.PropertyChanged -= OnItemChanged;
            _watched.Remove(gone);
        }

        foreach (var item in current.Where(_watched.Add))
        {
            item.PropertyChanged += OnItemChanged;
        }
    }

    private void OnItemChanged(object? sender, PropertyChangedEventArgs e) => InvalidateMeasure();
}
