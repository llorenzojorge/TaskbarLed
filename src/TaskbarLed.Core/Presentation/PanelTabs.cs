using TaskbarLed.Core.Sessions;

namespace TaskbarLed.Core.Presentation;

/// <summary>Which sessions get a tab on the panel.</summary>
public static class PanelTabs
{
    /// <summary>
    /// The sessions worth a tab: every one that is running, waiting for the user or finished but not seen yet,
    /// plus idle ones (finished and seen, or never prompted) that had an event in the last
    /// <paramref name="idleTimeout"/>. A chat the user only opened and read stops taking room; its next prompt
    /// brings the tab back, because that event makes it active again. The store keeps the session either way.
    /// </summary>
    public static IReadOnlyList<Session> Shown(IReadOnlyList<Session> sessions, DateTimeOffset now, TimeSpan idleTimeout)
    {
        ArgumentNullException.ThrowIfNull(sessions);

        return [.. sessions.Where(s => s.Status != SessionStatus.Idle || now - s.LastUpdated < idleTimeout)];
    }
}
