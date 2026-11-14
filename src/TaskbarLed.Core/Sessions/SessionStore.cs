namespace TaskbarLed.Core.Sessions;

/// <summary>Thread-safe, in-memory collection of sessions fed by <see cref="SessionEvent"/>s.</summary>
public sealed class SessionStore
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, Session> _sessions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _firstSeen = new(StringComparer.Ordinal);
    private readonly TimeProvider _time;
    private long _sequence;

    public SessionStore(TimeProvider? time = null) => _time = time ?? TimeProvider.System;

    /// <summary>Raised on the caller's thread after any change.</summary>
    public event EventHandler? Changed;

    /// <summary>Snapshot of all sessions in a stable order (first seen first).</summary>
    public IReadOnlyList<Session> Snapshot()
    {
        lock (_gate)
        {
            return [.. _sessions.Values.OrderBy(s => _firstSeen[s.Id])];
        }
    }

    public void Apply(SessionEvent evt)
    {
        ArgumentNullException.ThrowIfNull(evt);
        lock (_gate)
        {
            _sessions.TryGetValue(evt.SessionId, out var current);
            var next = SessionReducer.Apply(current, evt);
            if (next is null)
            {
                Remove(evt.SessionId);
            }
            else
            {
                _sessions[evt.SessionId] = next;
                _firstSeen.TryAdd(evt.SessionId, _sequence++);
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void ApplyRange(IEnumerable<SessionEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        foreach (var evt in events)
        {
            Apply(evt);
        }
    }

    /// <summary>Marks completed sessions as seen (all of them, or those <paramref name="seen"/> accepts). Returns true if anything changed.</summary>
    public bool AcknowledgeCompleted(Func<Session, bool>? seen = null)
    {
        var changed = false;
        lock (_gate)
        {
            foreach (var session in _sessions.Values.ToList())
            {
                if (session.Status == SessionStatus.Completed && (seen is null || seen(session)))
                {
                    _sessions[session.Id] = SessionReducer.Acknowledge(session);
                    changed = true;
                }
            }
        }

        if (changed)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }

        return changed;
    }

    /// <summary>
    /// Drops sessions with no activity for <paramref name="maxAge"/>, except those waiting on the user.
    /// Sources cannot always report that a session closed (crash, window closed), so this keeps the panel honest.
    /// </summary>
    public bool PruneStale(TimeSpan maxAge)
    {
        var cutoff = _time.GetUtcNow() - maxAge;
        int removed;
        lock (_gate)
        {
            var stale = _sessions.Values
                .Where(s => s.LastUpdated < cutoff && s.Status != SessionStatus.AwaitingInput)
                .Select(s => s.Id)
                .ToList();
            stale.ForEach(Remove);
            removed = stale.Count;
        }

        if (removed > 0)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }

        return removed > 0;
    }

    // Caller holds _gate.
    private void Remove(string id)
    {
        _sessions.Remove(id);
        _firstSeen.Remove(id);
    }
}
