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

public sealed record SessionListRow(string Label, string Status, bool IsChild, string Route, bool IsDone)
{
    public string RouteColor => RouteColors.For(Route);
    public string RouteLabel => IsChild ? "●" : Route;
}

/// <summary>State bound by every panel window. Shared across monitors so they always agree.</summary>
public sealed partial class PanelViewModel : ObservableObject
{
    private readonly string _editHint;
    private readonly CultureInfo _culture;
    private PanelFrame? _frame;
    private IReadOnlyList<Session> _sessions = [];
    private DateTimeOffset _lastFrameAt;
    private readonly Dictionary<string, int> _routes = new(StringComparer.Ordinal);

    [ObservableProperty]
    public partial bool IsLineMap { get; set; }

    public PanelViewModel(string editHint, CultureInfo culture)
    {
        _editHint = editHint;
        _culture = culture;
    }

    /// <summary>Tabs are updated in place so their blink animations do not restart every second.</summary>
    public ObservableCollection<ChannelViewModel> Channels { get; } = [];

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
    [NotifyPropertyChangedFor(nameof(ShowRows))]
    public partial bool IsEditing { get; set; }

    /// <summary>One-off tip shown in the feed (e.g. the first-run "right-click to edit"); null when none.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayText))]
    public partial string? Hint { get; set; }

    /// <summary>Theme preference: LED themes shout in capitals, modern ones keep normal case.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayText))]
    public partial bool Uppercase { get; set; }

    /// <summary>Current feed line: edit instructions, a one-off tip, or the latest event. A change pushes a new line.</summary>
    public string DisplayText => Cased(IsEditing ? _editHint : Hint ?? Feed);

    /// <summary>Applies the theme's letter case to any text shown by the app (alerts, too).</summary>
    public string Cased(string text) => Uppercase ? text.ToUpper(_culture) : text;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowRows))]
    public partial bool ShowSessionList { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowRows))]
    public partial IReadOnlyList<SessionListRow> Rows { get; set; } = [];

    [ObservableProperty]
    public partial int ActiveCount { get; set; }

    public bool ShowRows => ShowSessionList && !IsEditing && Rows.Count > 0;

    public void Apply(PanelFrame frame, IReadOnlyList<Session>? sessions = null, DateTimeOffset now = default)
    {
        ArgumentNullException.ThrowIfNull(frame);
        _frame = frame;
        _sessions = sessions ?? [];
        _lastFrameAt = now;
        ActiveCount = _sessions.Count(session => session.Status is SessionStatus.Running or SessionStatus.AwaitingInput);
        var rows = BuildRows(_sessions, now).ToArray();
        if (!Rows.SequenceEqual(rows))
        {
            Rows = rows;
        }

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

    private IEnumerable<SessionListRow> BuildRows(IReadOnlyList<Session> sessions, DateTimeOffset now)
    {
        var known = sessions.ToDictionary(session => session.Id, StringComparer.Ordinal);
        var portuguese = _culture.TwoLetterISOLanguageName == "pt";
        var groups = sessions.Where(session => session.Status != SessionStatus.Idle
            && (session.Status != SessionStatus.Completed || (now - session.LastUpdated).TotalSeconds < 10))
            .GroupBy(session => session.ParentId ?? session.Id, StringComparer.Ordinal).ToArray();
        var activeRoutes = groups.Select(group => group.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var expired in _routes.Keys.Where(id => !activeRoutes.Contains(id)).ToArray())
        {
            _routes.Remove(expired);
        }

        var used = _routes.Values.ToHashSet();
        foreach (var group in groups)
        {
            if (!_routes.TryGetValue(group.Key, out var index))
            {
                index = 0;
                while (!used.Add(index))
                {
                    index++;
                }

                _routes.Add(group.Key, index);
            }
            var route = RouteLabel(index);
            if (known.TryGetValue(group.Key, out var parent))
            {
                yield return Row(parent, false, route);
            }
            else
            {
                var worker = group.First();
                yield return new SessionListRow(Cased(worker.SourceId), string.Empty, false, route, false);
            }

            foreach (var worker in group.Where(session => session.ParentId is not null))
            {
                yield return Row(worker, true, route);
            }
        }

        SessionListRow Row(Session session, bool child, string route)
        {
            var status = session.Status switch
            {
                SessionStatus.Running => portuguese ? "▸ Rodando" : "▸ Running",
                SessionStatus.AwaitingInput => portuguese ? "! Aguardando" : "! Waiting",
                SessionStatus.Completed => portuguese ? "✓ Concluído" : "✓ Done",
                _ => portuguese ? "Ocioso" : "Idle",
            };
            var label = session.DisplayName;
            if (session.Status != SessionStatus.Idle && session.Elapsed(now) is { } elapsed)
            {
                status += " · " + DurationFormat.Clock(elapsed);
            }

            var done = session.Status == SessionStatus.Completed;
            if (done)
            {
                var remaining = Math.Clamp((int)Math.Ceiling(10 - (now - session.LastUpdated).TotalSeconds), 0, 10);
                status += $" · {remaining}s";
            }
            return new SessionListRow(Cased(label), status, child, route, done);
        }
    }

    private static string RouteLabel(int index)
    {
        var label = string.Empty;
        do
        {
            label = (char)('A' + index % 26) + label;
            index = index / 26 - 1;
        }
        while (index >= 0);

        return label;
    }

    // Re-case tab labels when the theme changes.
    partial void OnUppercaseChanged(bool value)
    {
        if (_frame is not null)
        {
            Apply(_frame, _sessions, _lastFrameAt);
        }
    }
}
