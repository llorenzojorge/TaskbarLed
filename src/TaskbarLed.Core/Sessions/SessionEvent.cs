namespace TaskbarLed.Core.Sessions;

/// <summary>
/// Something that happened in an agent session. Sources translate their own payloads
/// into these events; <see cref="SessionReducer"/> folds them into <see cref="Session"/> snapshots.
/// </summary>
public abstract record SessionEvent(string SessionId, string SourceId, DateTimeOffset Timestamp);

/// <summary>A session was opened or resumed.</summary>
public sealed record SessionStarted(string SessionId, string SourceId, DateTimeOffset Timestamp, string? ProjectName)
    : SessionEvent(SessionId, SourceId, Timestamp);

/// <summary>The user sent a prompt; the agent starts working.</summary>
public sealed record PromptSubmitted(string SessionId, string SourceId, DateTimeOffset Timestamp, string? Title, string? ProjectName)
    : SessionEvent(SessionId, SourceId, Timestamp);

/// <summary>The agent started an action (tool call), already described for display.</summary>
public sealed record ActivityStarted(string SessionId, string SourceId, DateTimeOffset Timestamp, string Activity)
    : SessionEvent(SessionId, SourceId, Timestamp);

/// <summary>An action finished; the agent keeps working (clears a pending approval).</summary>
public sealed record ActivityFinished(string SessionId, string SourceId, DateTimeOffset Timestamp)
    : SessionEvent(SessionId, SourceId, Timestamp);

/// <summary>The agent is blocked waiting for the user.</summary>
public sealed record AttentionRequested(string SessionId, string SourceId, DateTimeOffset Timestamp, string? Reason)
    : SessionEvent(SessionId, SourceId, Timestamp);

/// <summary>The agent finished answering the prompt.</summary>
public sealed record ResponseCompleted(string SessionId, string SourceId, DateTimeOffset Timestamp, string? LastMessage)
    : SessionEvent(SessionId, SourceId, Timestamp);

/// <summary>
/// The user stopped the prompt (Esc, stop button). Agents usually report no "finished" event for this,
/// so sources detect it on their own; <see cref="Timestamp"/> is when the interruption happened.
/// </summary>
public sealed record PromptInterrupted(string SessionId, string SourceId, DateTimeOffset Timestamp)
    : SessionEvent(SessionId, SourceId, Timestamp);

/// <summary>The source learned which app the session runs in.</summary>
public sealed record HostIdentified(string SessionId, string SourceId, DateTimeOffset Timestamp, SessionHost Host)
    : SessionEvent(SessionId, SourceId, Timestamp);

/// <summary>A new measurement of how full the context window is (0..1).</summary>
public sealed record ContextUsageMeasured(string SessionId, string SourceId, DateTimeOffset Timestamp, double Usage)
    : SessionEvent(SessionId, SourceId, Timestamp);

/// <summary>The session was closed and should leave the panel.</summary>
public sealed record SessionEnded(string SessionId, string SourceId, DateTimeOffset Timestamp)
    : SessionEvent(SessionId, SourceId, Timestamp);
