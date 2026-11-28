using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using TaskbarLed.App.Presentation;
using TaskbarLed.Core.Visibility;
using TaskbarLed.Platform.Windows.Windowing;

namespace TaskbarLed.App.Panels;

/// <summary>What a drag in edit mode changes.</summary>
public enum EditKind
{
    Move,
    Resize,
}

/// <summary>Drag progress in physical pixels since the drag started (or a keyboard nudge).</summary>
public sealed class EditDragEventArgs(EditKind kind, int deltaX, int deltaY, bool completed, bool isNudge = false) : EventArgs
{
    public EditKind Kind { get; } = kind;

    public int DeltaX { get; } = deltaX;

    public int DeltaY { get; } = deltaY;

    public bool Completed { get; } = completed;

    /// <summary>Keyboard fine adjustment: exact pixels, no snapping.</summary>
    public bool IsNudge { get; } = isNudge;
}

/// <summary>
/// Hosts one <see cref="PanelView"/> on one monitor and turns mouse and keyboard input into events.
/// Placement, visibility and persistence belong to <see cref="PanelManager"/>.
/// </summary>
public partial class PanelWindow : Window
{
    internal bool TopActivityBar { get; private set; }
    internal bool TopActivityExpanded { get; private set; }

    internal void SetTopActivityBar(bool enabled)
    {
        TopActivityBar = enabled;
        if (!enabled)
        {
            TopActivityExpanded = false;
        }

        View.ShowActivitySummary = enabled && !TopActivityExpanded;
    }

    internal bool ShowSessionRows { set => View.ShowSessionRows = value; }

    internal void SetBackgroundOpacity(double opacity) => View.Surface.Opacity = opacity;

    private readonly PanelViewModel _viewModel;
    private nint _handle;
    private EditKind? _dragKind;
    private Point _dragStart;

    public PanelWindow(PanelViewModel viewModel, MonitorInfo monitor)
    {
        _viewModel = viewModel;
        Monitor = monitor;
        DataContext = viewModel;
        InitializeComponent();
        _viewModel.PropertyChanged += OnViewModelChanged;
        View.SaveRequested += (_, _) => EditFinished?.Invoke(this, true);
        View.CancelRequested += (_, _) => EditFinished?.Invoke(this, false);
    }

    /// <summary>The user clicked the feed (outside edit mode).</summary>
    public event EventHandler? OpenRequested;

    /// <summary>The user clicked a session tab (outside edit mode); the argument is its session id.</summary>
    public event EventHandler<string>? TabOpenRequested;

    /// <summary>The user scrolled over the panel: show the next session.</summary>
    public event EventHandler? NextRequested;

    /// <summary>Mouse drag (live and final) or arrow-key nudge (always final) in edit mode.</summary>
    public event EventHandler<EditDragEventArgs>? EditDrag;

    /// <summary>Enter (true: save) or Escape (false: discard) in edit mode.</summary>
    public event EventHandler<bool>? EditFinished;

    /// <summary>Right click: show the app menu here.</summary>
    public event EventHandler? MenuRequested;

    public MonitorInfo Monitor { get; set; }

    /// <summary>Lets clicks fall through to the content below (mini panel over full-screen video).</summary>
    public void SetClickThrough(bool enabled)
    {
        if (_handle != 0)
        {
            WindowActions.SetClickThrough(_handle, enabled);
        }
    }

    /// <summary>Last position applied, in physical pixels.</summary>
    public PixelRect Bounds { get; private set; }

    public void Place(PixelRect bounds)
    {
        if (bounds == Bounds || _handle == 0)
        {
            return;
        }

        Bounds = bounds;
        WindowActions.PlaceOnTop(_handle, bounds);
    }

    public void KeepOnTop()
    {
        if (_handle != 0)
        {
            WindowActions.KeepOnTop(_handle);
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _handle = new WindowInteropHelper(this).Handle;
        WindowActions.MakeOverlay(_handle);
    }

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.PropertyChanged -= OnViewModelChanged;
        base.OnClosed(e);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnMouseLeftButtonDown(e);
        if (!_viewModel.IsEditing)
        {
            return;
        }

        _dragKind = ReferenceEquals(e.OriginalSource, View.ResizeGrip) ? EditKind.Resize : EditKind.Move;
        _dragStart = ScreenPoint(e);
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnMouseMove(e);
        if (_dragKind is { } kind)
        {
            var now = ScreenPoint(e);
            EditDrag?.Invoke(this, new EditDragEventArgs(kind, (int)(now.X - _dragStart.X), (int)(now.Y - _dragStart.Y), completed: false));
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnMouseLeftButtonUp(e);
        if (_dragKind is { } kind)
        {
            ReleaseMouseCapture();
            _dragKind = null;
            var now = ScreenPoint(e);
            EditDrag?.Invoke(this, new EditDragEventArgs(kind, (int)(now.X - _dragStart.X), (int)(now.Y - _dragStart.Y), completed: true));
        }
        else if (!_viewModel.IsEditing)
        {
            if (TopActivityBar)
            {
                TopActivityExpanded = !TopActivityExpanded;
                SetTopActivityBar(true);
            }
            else if (TabUnder(e.OriginalSource) is { SessionId.Length: > 0 } tab)
            {
                TabOpenRequested?.Invoke(this, tab.SessionId);
            }
            else
            {
                OpenRequested?.Invoke(this, EventArgs.Empty);
            }
        }

        e.Handled = true;
    }

    // Everything inside a tab's template inherits the tab's view model as its DataContext.
    private static ChannelViewModel? TabUnder(object source) => source switch
    {
        FrameworkElement { DataContext: ChannelViewModel tab } => tab,
        FrameworkContentElement { DataContext: ChannelViewModel tab } => tab,
        _ => null,
    };

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnMouseWheel(e);
        NextRequested?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnMouseRightButtonUp(e);
        MenuRequested?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnKeyDown(e);
        if (!_viewModel.IsEditing)
        {
            return;
        }

        // Arrows nudge the panel 1 px, Shift+arrows 10 px.
        var step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 10 : 1;
        (int dx, int dy)? nudge = e.Key switch
        {
            Key.Left => (-step, 0),
            Key.Right => (step, 0),
            Key.Up => (0, -step),
            Key.Down => (0, step),
            _ => null,
        };

        if (nudge is { } d)
        {
            EditDrag?.Invoke(this, new EditDragEventArgs(EditKind.Move, d.dx, d.dy, completed: true, isNudge: true));
            e.Handled = true;
        }
        else if (e.Key is Key.Enter or Key.Escape)
        {
            EditFinished?.Invoke(this, e.Key == Key.Enter);
            e.Handled = true;
        }
    }

    // Screen coordinates in physical pixels (the app is per-monitor DPI aware).
    private Point ScreenPoint(MouseEventArgs e) => PointToScreen(e.GetPosition(this));

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PanelViewModel.IsEditing) && _viewModel.IsEditing && IsVisible)
        {
            Activate(); // Receive arrows, Enter and Escape.
        }
    }
}
