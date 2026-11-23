using TaskbarLed.Core.Sessions;

namespace TaskbarLed.Core.Presentation;

/// <summary>
/// Decides which session the ticker shows. Sessions that need the user preempt everything;
/// otherwise the ticker rotates through finished and running sessions, one <c>dwell</c> each.
/// </summary>
public sealed class SessionRotation
{
    private readonly TimeSpan _dwell;
    private string? _currentId;
    private DateTimeOffset _shownSince;

    public SessionRotation(TimeSpan dwell)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(dwell, TimeSpan.Zero);
        _dwell = dwell;
    }

    /// <summary>Picks the session to display now, or null when there is nothing worth showing.</summary>
    public Session? Select(IReadOnlyList<Session> sessions, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(sessions);

        var candidates = Candidates(sessions);
        if (candidates.Count == 0)
        {
            _currentId = null;
            return null;
        }

        var currentIndex = _currentId is null ? -1 : candidates.FindIndex(s => s.Id == _currentId);
        if (currentIndex >= 0 && now - _shownSince < _dwell)
        {
            return candidates[currentIndex];
        }

        // Dwell expired (or the current session left the candidate set): advance and restart the clock.
        var next = candidates[(currentIndex + 1) % candidates.Count];
        _currentId = next.Id;
        _shownSince = now;
        return next;
    }

    /// <summary>Jumps to the next candidate right away (e.g. the user scrolls the panel).</summary>
    public void Skip() => _shownSince = DateTimeOffset.MinValue;

    /// <summary>
    /// Puts <paramref name="sessionId"/> in the feed for one dwell (e.g. the user clicks its tab). A session
    /// outside the current candidates (one waiting for the user preempts the rest) is skipped by
    /// <see cref="Select"/> as usual.
    /// </summary>
    public void Show(string sessionId, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(sessionId);
        _currentId = sessionId;
        _shownSince = now;
    }

    private static List<Session> Candidates(IReadOnlyList<Session> sessions)
    {
        var awaiting = sessions.Where(s => s.Status == SessionStatus.AwaitingInput).ToList();
        if (awaiting.Count > 0)
        {
            return awaiting;
        }

        var active = sessions.Where(s => s.Status is SessionStatus.Running or SessionStatus.Completed).ToList();
        if (active.Count > 0)
        {
            return active;
        }

        // Nothing active: show the most recently used idle session, if any.
        var lastIdle = sessions.MaxBy(s => s.LastUpdated);
        return lastIdle is null ? [] : [lastIdle];
    }
}
