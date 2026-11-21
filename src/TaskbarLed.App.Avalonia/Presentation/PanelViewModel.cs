using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using TaskbarLed.Core.Presentation;
using TaskbarLed.Core.Sessions;

namespace TaskbarLed.App.Avalonia.Presentation;

/// <summary>One session tab on the panel.</summary>
public sealed partial class ChannelViewModel : ObservableObject
{
    [ObservableProperty]
    public partial string Label { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Glyph), nameof(IsRunning), nameof(IsCompleted), nameof(IsAwaiting))]
    public partial SessionStatus Status { get; set; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    public bool IsRunning => Status == SessionStatus.Running;

    public bool IsCompleted => Status == SessionStatus.Completed;

    public bool IsAwaiting => Status == SessionStatus.AwaitingInput;

    /// <summary>Status marker after the label, readable even without color.</summary>
    public string Glyph => Status switch
    {
        SessionStatus.Running => "▸",
        SessionStatus.Completed => "✓",
        SessionStatus.AwaitingInput => "!",
        _ => "·",
    };
}

/// <summary>
/// State bound by the floating panel. Same contract as the WPF <c>PanelViewModel</c> (minus edit mode);
/// both apply a Core <see cref="PanelFrame"/>, so the composer stays the single source of truth.
/// </summary>
public sealed partial class PanelViewModel : ObservableObject
{
    private readonly CultureInfo _culture;
    private PanelFrame? _frame;

    public PanelViewModel(CultureInfo culture) => _culture = culture ?? throw new ArgumentNullException(nameof(culture));

    /// <summary>Tabs are updated in place so their blink animations do not restart every second.</summary>
    public ObservableCollection<ChannelViewModel> Channels { get; } = [];

    [ObservableProperty]
    public partial string Feed { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ElapsedText { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FeedIsAttention))]
    public partial SessionStatus SelectedStatus { get; set; }

    /// <summary>A session finished and the user has not opened Claude yet: the outline blinks green.</summary>
    [ObservableProperty]
    public partial bool IsBlinking { get; set; }

    /// <summary>A session waits for the user: the outline blinks amber.</summary>
    [ObservableProperty]
    public partial bool NeedsAttention { get; set; }

    /// <summary>Any session is running, finished-unseen or waiting.</summary>
    [ObservableProperty]
    public partial bool HasActivity { get; set; }

    /// <summary>Theme preference: LED themes shout in capitals.</summary>
    [ObservableProperty]
    public partial bool Uppercase { get; set; }

    public bool FeedIsAttention => SelectedStatus == SessionStatus.AwaitingInput;

    public void Apply(PanelFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        _frame = frame;

        Feed = Cased(frame.Feed);
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
            Channels[i].Label = Cased(frame.Channels[i].Label);
            Channels[i].Status = frame.Channels[i].Status;
            Channels[i].IsSelected = frame.Channels[i].IsSelected;
        }
    }

    private string Cased(string text) => Uppercase ? text.ToUpper(_culture) : text;

    partial void OnUppercaseChanged(bool value)
    {
        if (_frame is not null)
        {
            Apply(_frame);
        }
    }
}
