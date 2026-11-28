namespace TaskbarLed.Core.Sessions;

/// <summary>Immutable snapshot of one agent session as shown on the panel.</summary>
/// <param name="Id">Stable identifier given by the source (for Claude Code, the session id).</param>
/// <param name="SourceId">Which <see cref="Sources.ISessionSource"/> produced the session.</param>
/// <param name="Title">Human title of the conversation, when the source provides one.</param>
/// <param name="ProjectName">Short name of the working folder.</param>
/// <param name="Status">Current lifecycle state.</param>
/// <param name="PromptStartedAt">When the prompt in flight (or the last one) was submitted.</param>
/// <param name="PromptEndedAt">When the last prompt finished; null while running.</param>
/// <param name="Activity">What the agent is doing right now, already phrased for display.</param>
/// <param name="LastMessage">Last text the agent wrote, if known.</param>
/// <param name="LastUpdated">Timestamp of the last event applied.</param>
/// <param name="Host">The app the session runs in, which decides what "Claude is in sight" means for it.</param>
public sealed record Session(
    string Id,
    string SourceId,
    string? Title,
    string? ProjectName,
    SessionStatus Status,
    DateTimeOffset? PromptStartedAt,
    DateTimeOffset? PromptEndedAt,
    string? Activity,
    string? LastMessage,
    DateTimeOffset LastUpdated,
    SessionHost Host = SessionHost.Unknown)
{
    /// <summary>The parent session ID for a worker.</summary>
    public string? ParentId { get; init; }

    /// <summary>The provider name reported by the integration.</summary>
    public string? EngineName { get; init; }

    /// <summary>The provider conversation identifier, when available.</summary>
    public Guid? ConversationId { get; init; }

    /// <summary>The PID reported for the local provider process.</summary>
    public int? ProcessId { get; init; }

    /// <summary>Whether timing starts at the first local observation rather than a reported turn start.</summary>
    public bool IsElapsedApproximate { get; init; }

    /// <summary>Best label to identify the session on a tiny display.</summary>
    public string DisplayName =>
        !string.IsNullOrWhiteSpace(Title) ? Title
        : !string.IsNullOrWhiteSpace(ProjectName) ? ProjectName
        : Id.Length > 8 ? Id[..8] : Id;

    /// <summary>Creates an idle session with no history.</summary>
    public static Session Create(string id, string sourceId, DateTimeOffset at) =>
        new(id, sourceId, null, null, SessionStatus.Idle, null, null, null, null, at);

    /// <summary>Time spent on the current prompt, or on the last one once it finished.</summary>
    public TimeSpan? Elapsed(DateTimeOffset now)
    {
        if (PromptStartedAt is not { } start)
        {
            return null;
        }

        var end = PromptEndedAt is { } finished && finished >= start ? finished : now;
        return end < start ? TimeSpan.Zero : end - start;
    }
}
