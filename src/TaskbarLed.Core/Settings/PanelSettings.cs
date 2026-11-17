namespace TaskbarLed.Core.Settings;

/// <summary>Which monitors get a panel.</summary>
public enum MonitorMode
{
    /// <summary>Only the primary monitor.</summary>
    Primary = 0,

    /// <summary>Every monitor.</summary>
    All = 1,

    /// <summary>The monitor named in <see cref="PanelSettings.MonitorDeviceName"/>.</summary>
    Specific = 2,
}

/// <summary>Where the panel sits on a monitor.</summary>
public enum PanelPlacement
{
    /// <summary>Inside the taskbar's empty area. Falls back to <see cref="AboveTaskbar"/> when the taskbar auto-hides.</summary>
    Taskbar = 0,

    /// <summary>Floating just above the taskbar, with a small gap.</summary>
    AboveTaskbar = 1,

    /// <summary>Anywhere on the monitor; snaps to edges while dragging.</summary>
    Free = 2,
}

/// <summary>What happens while a video or app is full screen (borderless).</summary>
public enum FullscreenBehavior
{
    /// <summary>A thin glowing line on the screen edge, plus a short alert when something finishes or needs you.</summary>
    EdgeAndToast = 0,

    /// <summary>Only the short alert.</summary>
    ToastOnly = 1,

    /// <summary>A compact, click-through panel in a corner while there is activity.</summary>
    MiniPanel = 2,

    /// <summary>Nothing over full-screen content.</summary>
    Hide = 3,
}

/// <summary>How the notification sounds are voiced.</summary>
public enum SoundStyle
{
    /// <summary>The style that goes with the current theme.</summary>
    Auto = 0,

    /// <summary>Soft, warm two-note chime.</summary>
    Chime = 1,

    /// <summary>Square beeps of an old terminal.</summary>
    Retro = 2,

    /// <summary>Small glass bell.</summary>
    Bell = 3,
}

/// <summary>A panel position in device-independent pixels.</summary>
/// <param name="X">Taskbar modes: distance from the taskbar's left edge. Free: distance from the monitor's left edge.</param>
/// <param name="Y">Free mode only: distance from the monitor's top edge.</param>
/// <param name="Width">Panel width.</param>
public sealed record PanelPosition(double X, double Y, double Width);

/// <summary>User preferences for the panel.</summary>
public sealed record PanelSettings
{
    public MonitorMode MonitorMode { get; init; } = MonitorMode.Primary;

    /// <summary>Windows device name (e.g. <c>\\.\DISPLAY2</c>) when <see cref="MonitorMode"/> is Specific.</summary>
    public string? MonitorDeviceName { get; init; }

    public PanelPlacement Placement { get; init; } = PanelPlacement.Taskbar;

    /// <summary>Position used in <see cref="PanelPlacement.Taskbar"/> mode. Each mode remembers its own spot.</summary>
    public PanelPosition TaskbarPosition { get; init; } = new(12, 0, 420);

    /// <summary>Position used in <see cref="PanelPlacement.AboveTaskbar"/> mode.</summary>
    public PanelPosition AbovePosition { get; init; } = new(12, 0, 420);

    /// <summary>Position used in <see cref="PanelPlacement.Free"/> mode.</summary>
    public PanelPosition FreePosition { get; init; } = new(24, 24, 420);

    /// <summary>Gap between the panel and the taskbar in <see cref="PanelPlacement.AboveTaskbar"/> mode (0.5 rem).</summary>
    public double AboveGap { get; init; } = 8;

    public FullscreenBehavior Fullscreen { get; init; } = FullscreenBehavior.EdgeAndToast;

    /// <summary>Play a short sound when a session finishes while its app is out of sight.</summary>
    public bool SoundOnDone { get; init; } = true;

    /// <summary>Play a short sound when a session starts waiting for the user while its app is out of sight.</summary>
    public bool SoundOnAttention { get; init; } = true;

    public SoundStyle SoundStyle { get; init; } = SoundStyle.Auto;

    /// <summary>Hide the panel when the Claude window shows at least this fraction of itself.</summary>
    public double VisibleThreshold { get; init; } = 0.30;

    /// <summary>Hide the panel when no session is running, finished-unseen or waiting.</summary>
    public bool HideWhenIdle { get; init; } = true;

    /// <summary>Seconds each session stays in the feed before rotating.</summary>
    public double RotationSeconds { get; init; } = 8;

    /// <summary>Hours without events before a session is dropped.</summary>
    public double StaleSessionHours { get; init; } = 12;

    /// <summary>Theme used until the user picks one: the warm terminal in the spirit of Claude Code.</summary>
    public const string DefaultTheme = "ClaudeCode";

    /// <summary>Theme resource name (see Themes folder in the app).</summary>
    public string Theme { get; init; } = DefaultTheme;

    /// <summary>Position for the current <see cref="Placement"/>.</summary>
    public PanelPosition CurrentPosition => Placement switch
    {
        PanelPlacement.AboveTaskbar => AbovePosition,
        PanelPlacement.Free => FreePosition,
        _ => TaskbarPosition,
    };

    /// <summary>Returns a copy with <paramref name="position"/> stored for the current placement.</summary>
    public PanelSettings WithCurrentPosition(PanelPosition position) => Placement switch
    {
        PanelPlacement.AboveTaskbar => this with { AbovePosition = position },
        PanelPlacement.Free => this with { FreePosition = position },
        _ => this with { TaskbarPosition = position },
    };

    /// <summary>Validates and clamps values coming from a hand-edited settings file.</summary>
    public PanelSettings Normalize() => this with
    {
        TaskbarPosition = Clamp(TaskbarPosition ?? new(12, 0, 420)),
        AbovePosition = Clamp(AbovePosition ?? new(12, 0, 420)),
        FreePosition = Clamp(FreePosition ?? new(24, 24, 420)),
        AboveGap = Math.Clamp(AboveGap, 0, 200),
        VisibleThreshold = Math.Clamp(VisibleThreshold, 0.05, 0.95),
        RotationSeconds = Math.Clamp(RotationSeconds, 2, 120),
        StaleSessionHours = Math.Clamp(StaleSessionHours, 0.5, 168),
        Theme = string.IsNullOrWhiteSpace(Theme) ? DefaultTheme : Theme,
        SoundStyle = Enum.IsDefined(SoundStyle) ? SoundStyle : SoundStyle.Auto,
    };

    private static PanelPosition Clamp(PanelPosition p) =>
        new(Math.Max(0, p.X), Math.Max(0, p.Y), Math.Clamp(p.Width, 160, 2000));
}
