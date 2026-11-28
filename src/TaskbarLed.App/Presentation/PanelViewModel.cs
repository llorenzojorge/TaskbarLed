using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using TaskbarLed.Core.Presentation;
using TaskbarLed.Core.Sessions;

namespace TaskbarLed.App.Presentation;

/// <summary>One session tab on the panel.</summary>
public sealed partial class ChannelViewModel : ObservableObject
{
    /// <summary>Session the tab stands for, so a click on it can open that session.</summary>
    public string SessionId { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Label { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Glyph))]
    public partial SessionStatus Status { get; set; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    /// <summary>Status marker after the label, readable even without color.</summary>
    public string Glyph => Status switch
    {
        SessionStatus.Running => "▸",
        SessionStatus.Completed => "✓",
        SessionStatus.AwaitingInput => "!",
        _ => "·",
    };
}

/// <summary>State bound by every panel window. Shared across monitors so they always agree.</summary>
public sealed partial class PanelViewModel : ObservableObject
{
    private readonly string _editHint;
    private readonly CultureInfo _culture;
    private PanelFrame? _frame;

    public PanelViewModel(string editHint, CultureInfo culture)
    {
        _editHint = editHint;
        _culture = culture;
    }

    /// <summary>Tabs are updated in place so their blink animations do not restart every second.</summary>
    public ObservableCollection<ChannelViewModel> Channels { get; } = [];

    /// <summary>Feed line while editing the position only (no resize grip).</summary>
    public string MoveHint { get; init; } = "Drag to move";

    /// <summary>Label of the edit-mode button that keeps the new position.</summary>
    public string SaveLabel { get; init; } = "Save";

    /// <summary>Label of the edit-mode button that restores the previous position.</summary>
    public string CancelLabel { get; init; } = "Cancel";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayText))]
    public partial string Feed { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ElapsedText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial SessionStatus SelectedStatus { get; set; }

    /// <summary>A session finished and the user has not opened Claude yet: the panel blinks.</summary>
    [ObservableProperty]
    public partial bool IsBlinking { get; set; }

    [ObservableProperty]
    public partial bool NeedsAttention { get; set; }

    /// <summary>Any session is running, finished-unseen or waiting.</summary>
    [ObservableProperty]
    public partial bool HasActivity { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayText))]
    [NotifyPropertyChangedFor(nameof(ShowResizeGrip))]
    public partial bool IsEditing { get; set; }

    /// <summary>The current edit may change the width ("position and size"), not only the position.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayText))]
    [NotifyPropertyChangedFor(nameof(ShowResizeGrip))]
    public partial bool CanResize { get; set; } = true;

    /// <summary>The width handle on the right edge is shown.</summary>
    public bool ShowResizeGrip => IsEditing && CanResize;

    /// <summary>One-off tip shown in the feed (e.g. the first-run "right-click to edit"); null when none.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayText))]
    public partial string? Hint { get; set; }

    /// <summary>Theme preference: LED themes shout in capitals, modern ones keep normal case.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayText))]
    public partial bool Uppercase { get; set; }

    /// <summary>Current feed line: edit instructions, a one-off tip, or the latest event. A change pushes a new line.</summary>
    public string DisplayText => Cased(IsEditing ? (CanResize ? _editHint : MoveHint) : Hint ?? Feed);

    /// <summary>Applies the theme's letter case to any text shown by the app (alerts, too).</summary>
    public string Cased(string text) => Uppercase ? text.ToUpper(_culture) : text;

    public void Apply(PanelFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        _frame = frame;

        Feed = frame.Feed;
        ElapsedText = frame.ElapsedText;
        SelectedStatus = frame.SelectedStatus;
        IsBlinking = frame.HasUnseenCompletion && !frame.NeedsAttention;
        NeedsAttention = frame.NeedsAttention;
        HasActivity = frame.HasActivity;

        while (Channels.Count > frame.Channels.Count)
        {
            Channels.RemoveAt(Channels.Count - 1);
        }

        while (Channels.Count < frame.Channels.Count)
        {
            Channels.Add(new ChannelViewModel());
        }

        for (var i = 0; i < frame.Channels.Count; i++)
        {
            Channels[i].SessionId = frame.Channels[i].SessionId;
            Channels[i].Label = Cased(frame.Channels[i].Label);
            Channels[i].Status = frame.Channels[i].Status;
            Channels[i].IsSelected = frame.Channels[i].IsSelected;
        }
    }

    // Re-case tab labels when the theme changes.
    partial void OnUppercaseChanged(bool value)
    {
        if (_frame is not null)
        {
            Apply(_frame);
        }
    }
}
