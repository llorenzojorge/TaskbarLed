using System.Windows.Automation;
using TaskbarLed.Core.Visibility;
using static TaskbarLed.Platform.Windows.Native.NativeMethods;

namespace TaskbarLed.Platform.Windows.Windowing;

/// <summary>
/// Where the taskbar's buttons are: Start, widgets, search, the app icons and the tray, so a panel in the
/// taskbar can stay off them (<see cref="PanelLayout.AvoidItems"/>). Windows 11 draws them with XAML,
/// without windows of their own, so they are read through UI Automation. A query into Explorer takes about
/// 20 ms, so it runs on a background timer and callers get the last result without waiting.
/// </summary>
public sealed class TaskbarButtonWatcher : IDisposable
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);

    private readonly Timer _timer;
    private readonly Action<Exception>? _onError;
    private IReadOnlyList<PixelRect> _items = [];
    private int _busy;
    private bool _errorReported;

    /// <param name="onError">Called once, the first time a read fails (later failures stay quiet).</param>
    public TaskbarButtonWatcher(Action<Exception>? onError = null)
    {
        _onError = onError;
        _timer = new Timer(_ => Refresh(), null, TimeSpan.Zero, Interval);
    }

    /// <summary>Last known buttons that lie on <paramref name="taskbar"/>; empty when unknown.</summary>
    public IReadOnlyList<PixelRect> On(PixelRect? taskbar) =>
        taskbar is { } bar ? [.. Volatile.Read(ref _items).Where(i => !i.Intersect(bar).IsEmpty)] : [];

    public void Dispose() => _timer.Dispose();

    private void Refresh()
    {
        if (Interlocked.Exchange(ref _busy, 1) == 1)
        {
            return; // Explorer is slow right now; skip a tick rather than pile up.
        }

        try
        {
            Volatile.Write(ref _items, Read());
        }
#pragma warning disable CA1031 // Explorer restarting or a UIA hiccup must never stop the panel.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            if (!_errorReported)
            {
                _errorReported = true;
                _onError?.Invoke(ex);
            }
        }
        finally
        {
            Volatile.Write(ref _busy, 0);
        }
    }

    private static List<PixelRect> Read()
    {
        var result = new List<PixelRect>();
        AddFrom(result, FindWindowEx(0, 0, "Shell_TrayWnd", null));
        for (var hwnd = FindWindowEx(0, 0, "Shell_SecondaryTrayWnd", null);
             hwnd != 0;
             hwnd = FindWindowEx(0, hwnd, "Shell_SecondaryTrayWnd", null))
        {
            AddFrom(result, hwnd);
        }

        return result;
    }

    // Windows 11: every button is a child of the XAML "TaskbarFrame"; tray icons carry the id "SystemTrayIcon".
    // On Windows 10 neither exists and nothing is added, so the panel keeps its saved spot as before.
    private static void AddFrom(List<PixelRect> result, nint taskbar)
    {
        if (taskbar == 0)
        {
            return;
        }

        var root = AutomationElement.FromHandle(taskbar);
        var frame = root.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "TaskbarFrame"));
        if (frame is not null)
        {
            AddAll(result, frame.FindAll(TreeScope.Children, Condition.TrueCondition));
        }

        AddAll(result, root.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "SystemTrayIcon")));
    }

    private static void AddAll(List<PixelRect> result, AutomationElementCollection elements)
    {
        foreach (AutomationElement element in elements)
        {
            var r = element.Current.BoundingRectangle;
            if (!r.IsEmpty && r.Width > 0 && r.Height > 0)
            {
                result.Add(new PixelRect((int)Math.Floor(r.Left), (int)Math.Floor(r.Top), (int)Math.Ceiling(r.Right), (int)Math.Ceiling(r.Bottom)));
            }
        }
    }
}
