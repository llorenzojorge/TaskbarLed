using TaskbarLed.Core.Sessions;

namespace TaskbarLed.Core.Presentation;

/// <summary>One session tab: its label and status at a glance.</summary>
/// <param name="SessionId">Session shown by the tab.</param>
/// <param name="Label">Short, numbered name, e.g. <c>1 WEBAPP</c>.</param>
/// <param name="Status">Session status (drives color and blinking).</param>
/// <param name="IsSelected">The feed is showing this session.</param>
public sealed record ChannelState(string SessionId, string Label, SessionStatus Status, bool IsSelected);

/// <summary>Everything the panel needs to draw one frame. UI-framework agnostic.</summary>
/// <param name="Channels">One tab per session, in stable order.</param>
/// <param name="Feed">Latest line for the selected session; a change means "new line in the feed".</param>
/// <param name="ElapsedText">Prompt timer for the selected session (e.g. <c>04:12</c>).</param>
/// <param name="SelectedStatus">Status of the session in the feed.</param>
/// <param name="SelectedSessionId">Id of the session in the feed.</param>
/// <param name="HasUnseenCompletion">At least one session finished and the user has not looked yet.</param>
/// <param name="NeedsAttention">At least one session is blocked on the user.</param>
/// <param name="HasActivity">Any session is running, finished-unseen or waiting.</param>
public sealed record PanelFrame(
    IReadOnlyList<ChannelState> Channels,
    string Feed,
    string ElapsedText,
    SessionStatus SelectedStatus,
    string? SelectedSessionId,
    bool HasUnseenCompletion,
    bool NeedsAttention,
    bool HasActivity)
{
    public static PanelFrame Empty(string feed) =>
        new([], feed, string.Empty, SessionStatus.Idle, null, false, false, false);
}
