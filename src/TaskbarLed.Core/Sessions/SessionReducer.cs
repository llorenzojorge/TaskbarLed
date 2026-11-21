namespace TaskbarLed.Core.Sessions;

/// <summary>Pure state transitions: <c>(session, event) → session</c>. No I/O, no clock.</summary>
public static class SessionReducer
{
    /// <summary>Applies <paramref name="evt"/> to <paramref name="current"/>.</summary>
    /// <returns>The new snapshot, or <c>null</c> when the session should be removed.</returns>
    public static Session? Apply(Session? current, SessionEvent evt)
    {
        ArgumentNullException.ThrowIfNull(evt);

        if (evt is SessionEnded)
        {
            return null;
        }

        // Only a prompt still in flight can be interrupted, and not by a stop older than the prompt
        // (the check that found it may race with a new prompt).
        if (evt is PromptInterrupted
            && (current is not { Status: SessionStatus.Running or SessionStatus.AwaitingInput }
                || evt.Timestamp < current.PromptStartedAt))
        {
            return current;
        }

        var session = (current ?? Session.Create(evt.SessionId, evt.SourceId, evt.Timestamp))
            with { LastUpdated = evt.Timestamp };

        return evt switch
        {
            SessionStarted e => session with { ProjectName = e.ProjectName ?? session.ProjectName },

            PromptSubmitted e => session with
            {
                Status = SessionStatus.Running,
                Title = e.Title ?? session.Title,
                ProjectName = e.ProjectName ?? session.ProjectName,
                PromptStartedAt = e.Timestamp,
                PromptEndedAt = null,
                Activity = null,
            },

            ActivityStarted e => EnsureRunning(session, e.Timestamp) with { Activity = e.Activity },

            ActivityFinished e => EnsureRunning(session, e.Timestamp),

            AttentionRequested e => session with
            {
                Status = SessionStatus.AwaitingInput,
                PromptStartedAt = session.PromptStartedAt ?? e.Timestamp,
                PromptEndedAt = null,
                Activity = e.Reason ?? session.Activity,
            },

            ResponseCompleted e => session with
            {
                Status = SessionStatus.Completed,
                PromptEndedAt = e.Timestamp,
                LastMessage = e.LastMessage ?? session.LastMessage,
                Activity = null,
            },

            // The user stopped it themselves, so there is nothing new to look at: idle, not completed.
            PromptInterrupted e => session with
            {
                Status = SessionStatus.Idle,
                PromptEndedAt = e.Timestamp,
                Activity = null,
            },

            HostIdentified e => session with { Host = e.Host },

            // The conversation's own title beats the folder name, which is meaningless for scratch chats.
            TitleIdentified e when !string.IsNullOrWhiteSpace(e.Title) => session with { Title = e.Title.Trim() },

            _ => session,
        };
    }

    /// <summary>The user has seen the result: a completed session goes back to idle.</summary>
    public static Session Acknowledge(Session session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return session.Status == SessionStatus.Completed ? session with { Status = SessionStatus.Idle } : session;
    }

    // Tool events also arrive after an approval, or for a prompt the panel never saw
    // (it started mid-prompt). Keep the original start time when the prompt is still in flight.
    private static Session EnsureRunning(Session session, DateTimeOffset at)
    {
        if (session.Status == SessionStatus.Running)
        {
            return session;
        }

        var promptInFlight = session.Status == SessionStatus.AwaitingInput && session.PromptStartedAt is not null;
        return session with
        {
            Status = SessionStatus.Running,
            PromptStartedAt = promptInFlight ? session.PromptStartedAt : at,
            PromptEndedAt = null,
        };
    }
}
