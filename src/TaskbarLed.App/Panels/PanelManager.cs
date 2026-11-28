using TaskbarLed.App.Presentation;
using TaskbarLed.Core.Settings;
using TaskbarLed.Core.Visibility;
using TaskbarLed.Platform.Windows.Windowing;

namespace TaskbarLed.App.Panels;

/// <summary>
/// Owns one <see cref="PanelWindow"/> per target monitor: creates and closes them as monitors come and go,
/// places them for the chosen placement, adapts them to full-screen content, and turns edit-mode
/// drags and nudges into new settings.
/// </summary>
public sealed class PanelManager : IDisposable
{
    private const double MiniOpacity = 0.88;

    private readonly PanelViewModel _viewModel;
    private readonly Dictionary<nint, PanelWindow> _windows = [];
    private PanelSettings _settings;
    private PanelSettings? _dragOrigin;
    private bool _disposed;

    public PanelManager(PanelViewModel viewModel, PanelSettings settings)
    {
        _viewModel = viewModel;
        _settings = settings;
    }

    public event EventHandler? OpenRequested;

    /// <summary>A session tab was clicked; the argument is its session id.</summary>
    public event EventHandler<string>? TabOpenRequested;

    public event EventHandler? NextRequested;

    public event EventHandler? MenuRequested;

    /// <summary>Position or size changed in edit mode (fires on every drag step).</summary>
    public event EventHandler<PanelSettings>? SettingsEdited;

    /// <summary>Enter (true) or Escape (false) pressed in edit mode.</summary>
    public event EventHandler<bool>? EditFinished;

    public PanelSettings Settings
    {
        get => _settings;
        set => _settings = value;
    }

    /// <summary>True when at least one panel is on screen.</summary>
    public bool AnyVisible => _windows.Values.Any(w => w.IsVisible);

    /// <summary>Monitors that should get a panel under the current settings.</summary>
    public static IReadOnlyList<MonitorInfo> Targets(IReadOnlyList<MonitorInfo> monitors, PanelSettings settings)
    {
        ArgumentNullException.ThrowIfNull(monitors);
        ArgumentNullException.ThrowIfNull(settings);

        var list = settings.MonitorMode switch
        {
            MonitorMode.All => monitors.ToList(),
            MonitorMode.Specific => monitors.Where(m => m.DeviceName == settings.MonitorDeviceName).ToList(),
            _ => monitors.Where(m => m.IsPrimary).ToList(),
        };

        // Monitor unplugged: fall back to the primary rather than showing nothing.
        return list.Count > 0 ? list : [.. monitors.Where(m => m.IsPrimary).Take(1)];
    }

    /// <summary>Brings windows in line with the target monitors, full-screen state and desired visibility.</summary>
    /// <param name="targets">Monitors from <see cref="Targets"/>.</param>
    /// <param name="fullscreen">Full-screen state per monitor handle.</param>
    /// <param name="shouldShow">Claude is out of sight and there is something to show.</param>
    /// <param name="preview">Show the panel no matter what (menu open): settings changes are visible right away.</param>
    public void Sync(IReadOnlyList<MonitorInfo> targets, IReadOnlyDictionary<nint, FullscreenKind> fullscreen, bool shouldShow, bool preview = false)
    {
        if (_disposed)
        {
            return;
        }

        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(fullscreen);

        var targetHandles = targets.Select(t => t.Handle).ToHashSet();
        foreach (var handle in _windows.Keys.Where(h => !targetHandles.Contains(h)).ToList())
        {
            _windows[handle].Close();
            _windows.Remove(handle);
        }

        foreach (var monitor in targets)
        {
            if (!_windows.TryGetValue(monitor.Handle, out var window))
            {
                window = CreateWindow(monitor);
                _windows[monitor.Handle] = window;
            }

            window.Monitor = monitor;
            var kind = fullscreen.GetValueOrDefault(monitor.Handle);
            var mini = !_viewModel.IsEditing && !preview && kind == FullscreenKind.Borderless && _settings.Fullscreen == FullscreenBehavior.MiniPanel;
            var visible = _viewModel.IsEditing || preview || (shouldShow && (kind == FullscreenKind.None || mini));

            if (!visible)
            {
                if (window.IsVisible)
                {
                    window.Hide();
                }

                continue;
            }

            if (!window.IsVisible)
            {
                window.Show();
            }

            window.SetClickThrough(mini);
            window.Opacity = mini ? MiniOpacity : 1;
            window.Place(mini
                ? PanelLayout.MiniCorner(monitor.Bounds, monitor.Scale)
                : PanelLayout.Compute(_settings, monitor.Bounds, monitor.Taskbar, monitor.Scale, monitor.TaskbarItems));
            window.KeepOnTop();
        }
    }

    public void Dispose()
    {
        _disposed = true;
        foreach (var window in _windows.Values.ToList()) // Closing removes it from the dictionary.
        {
            window.Close();
        }

        _windows.Clear();
    }

    private PanelWindow CreateWindow(MonitorInfo monitor)
    {
        var window = new PanelWindow(_viewModel, monitor);
        window.OpenRequested += (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty);
        window.TabOpenRequested += (_, id) => TabOpenRequested?.Invoke(this, id);
        window.NextRequested += (_, _) => NextRequested?.Invoke(this, EventArgs.Empty);
        window.MenuRequested += (_, _) => MenuRequested?.Invoke(this, EventArgs.Empty);
        window.EditFinished += (_, save) => EditFinished?.Invoke(this, save);
        window.EditDrag += OnEditDrag;

        // A closed WPF window can never be shown again. If something other than us closes it
        // (Alt+F4, the shell), forget it so the next Sync creates a fresh one instead of throwing forever.
        window.Closed += (_, _) =>
        {
            if (_windows.TryGetValue(monitor.Handle, out var current) && ReferenceEquals(current, window))
            {
                _windows.Remove(monitor.Handle);
            }
        };

        return window;
    }

    private void OnEditDrag(object? sender, EditDragEventArgs e)
    {
        if (sender is not PanelWindow window)
        {
            return;
        }

        var monitor = window.Monitor;
        var origin = _dragOrigin ??= _settings;
        // The drag edits the saved spot; taskbar buttons only trim what is shown (see PanelLayout.AvoidItems).
        var originRect = PanelLayout.Compute(origin, monitor.Bounds, monitor.Taskbar, monitor.Scale);

        if (e.Kind == EditKind.Move && !e.IsNudge)
        {
            // A mouse drag goes anywhere on the monitor; where the panel is decides the placement
            // (in the taskbar, just above it, or free), live, so the panel shows where it will land.
            // A click without movement changes nothing.
            if (e.DeltaX != 0 || e.DeltaY != 0)
            {
                var dragged = new PixelRect(originRect.Left + e.DeltaX, originRect.Top + e.DeltaY, originRect.Right + e.DeltaX, originRect.Bottom + e.DeltaY);
                _settings = PanelLayout.MoveTo(origin, dragged, monitor.Bounds, monitor.Taskbar, monitor.Scale);
            }
        }
        else
        {
            // Arrow keys nudge within the current placement: taskbar modes stay on their row.
            var dy = origin.Placement == PanelPlacement.Free ? e.DeltaY : 0;
            var rect = e.Kind == EditKind.Move
                ? new PixelRect(originRect.Left + e.DeltaX, originRect.Top + dy, originRect.Right + e.DeltaX, originRect.Bottom + dy)
                : originRect with { Right = Math.Max(originRect.Left + 1, originRect.Right + e.DeltaX) };

            var position = PanelLayout.ToPosition(origin, rect, monitor.Bounds, monitor.Taskbar, monitor.Scale);
            _settings = origin.WithCurrentPosition(position).Normalize();
        }

        // Move every panel right away so the drag feels attached to the mouse.
        foreach (var w in _windows.Values.Where(w => w.IsVisible))
        {
            w.Place(PanelLayout.Compute(_settings, w.Monitor.Bounds, w.Monitor.Taskbar, w.Monitor.Scale, w.Monitor.TaskbarItems));
        }

        SettingsEdited?.Invoke(this, _settings);

        if (e.Completed)
        {
            _dragOrigin = null;
        }
    }
}
