using TaskbarLed.Core.Sessions;

namespace TaskbarLed.Core.Presentation;

/// <summary>Which session a click on the panel opens.</summary>
public static class ClickTarget
{
    /// <summary>
    /// A click on the feed (or on a full-screen alert) opens a finished session the user has not looked at
    /// yet: the one in the feed if it is one, else the latest to finish. With none, the session in the feed.
    /// </summary>
    public static Session? ForFeed(IReadOnlyList<Session> sessions, string? selectedId)
    {
        ArgumentNullException.ThrowIfNull(sessions);

        var selected = sessions.FirstOrDefault(s => s.Id == selectedId);
        if (selected is { Status: SessionStatus.Completed })
        {
            return selected;
        }

        return sessions
            .Where(s => s.Status == SessionStatus.Completed)
            .MaxBy(s => s.PromptEndedAt ?? s.LastUpdated) ?? selected;
    }

    /// <summary>A click on a tab opens that tab's session, whatever its state.</summary>
    public static Session? ForTab(IReadOnlyList<Session> sessions, string sessionId)
    {
        ArgumentNullException.ThrowIfNull(sessions);

        return sessions.FirstOrDefault(s => s.Id == sessionId);
    }
}
