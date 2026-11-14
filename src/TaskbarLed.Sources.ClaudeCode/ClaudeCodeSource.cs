using System.Collections.Concurrent;
using System.Text.Json;
using TaskbarLed.Core.Sessions;
using TaskbarLed.Core.Sources;

namespace TaskbarLed.Sources.ClaudeCode;

/// <summary>
/// Translates Claude Code hook payloads (desktop app, CLI and IDE extensions share the same format)
/// into session events. Unknown events and fields are ignored, never thrown on.
/// </summary>
public sealed class ClaudeCodeSource : ISessionSource
{
    public const string Id = "claude-code";

    private readonly ToolActivityFormatter _formatter;
    private readonly ITranscriptUsageReader _usageReader;
    private readonly ITranscriptInterruptionReader _interruptionReader;
    private readonly ConcurrentDictionary<string, string> _transcripts = new(StringComparer.Ordinal);
    public ClaudeCodeSource(
        ToolActivityFormatter formatter,
        ITranscriptUsageReader usageReader,
        ITranscriptInterruptionReader? interruptionReader = null)
    {
        _formatter = formatter ?? throw new ArgumentNullException(nameof(formatter));
        _usageReader = usageReader ?? throw new ArgumentNullException(nameof(usageReader));
        _interruptionReader = interruptionReader ?? new TranscriptInterruptionReader();
    }

    public string SourceId => Id;

    /// <summary>Claude Code has no hook for an interrupted prompt; the transcript tells.</summary>
    public async ValueTask<IReadOnlyList<SessionEvent>> CheckQuietSessionsAsync(
        IReadOnlyList<Session> quietSessions,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(quietSessions);

        var events = new List<SessionEvent>();
        foreach (var session in quietSessions)
        {
            if (_transcripts.TryGetValue(session.Id, out var path)
                && await _interruptionReader.ReadInterruptionAsync(path, cancellationToken).ConfigureAwait(false) is { } at)
            {
                events.Add(new PromptInterrupted(session.Id, Id, at));
            }
        }

        return events;
    }

    public async ValueTask<IReadOnlyList<SessionEvent>> TranslateAsync(
        JsonElement payload,
        DateTimeOffset receivedAt,
        IReadOnlyDictionary<string, string> environment,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(environment);

        if (payload.ValueKind != JsonValueKind.Object
            || Text(payload, "session_id") is not { } sessionId
            || Text(payload, "hook_event_name") is not { } hookEvent)
        {
            return [];
        }

        if (Text(payload, "transcript_path") is { Length: > 0 } transcript)
        {
            _transcripts[sessionId] = transcript;
        }

        var project = ProjectName(Text(payload, "cwd"));
        var events = new List<SessionEvent>(2);

        switch (hookEvent)
        {
            case "SessionStart":
                events.Add(new SessionStarted(sessionId, Id, receivedAt, project));
                break;

            case "UserPromptSubmit":
                events.Add(new PromptSubmitted(sessionId, Id, receivedAt, Text(payload, "session_title"), project));
                break;

            case "PreToolUse":
                var toolName = Text(payload, "tool_name") ?? "tool";
                var input = payload.TryGetProperty("tool_input", out var toolInput) ? toolInput : default;
                var activity = _formatter.Describe(toolName, input);
                events.Add(ToolActivityFormatter.IsUserPrompt(toolName)
                    ? new AttentionRequested(sessionId, Id, receivedAt, activity)
                    : new ActivityStarted(sessionId, Id, receivedAt, activity));
                break;

            case "PostToolUse":
                events.Add(new ActivityFinished(sessionId, Id, receivedAt));
                await AddContextUsageAsync(events, payload, sessionId, receivedAt, cancellationToken).ConfigureAwait(false);
                break;

            case "Notification":
                if (IsAttentionNotification(payload))
                {
                    events.Add(new AttentionRequested(sessionId, Id, receivedAt, Text(payload, "message")));
                }

                break;

            case "Stop":
                events.Add(new ResponseCompleted(sessionId, Id, receivedAt, Text(payload, "last_assistant_message")));
                await AddContextUsageAsync(events, payload, sessionId, receivedAt, cancellationToken).ConfigureAwait(false);
                break;

            case "SessionEnd":
                events.Add(new SessionEnded(sessionId, Id, receivedAt));
                _transcripts.TryRemove(sessionId, out _);
                return events;

            default:
                break;
        }

        // Alongside every real event, so a session the panel pruned and sees again still knows its app.
        var host = HostOf(environment);
        if (events.Count > 0 && host != SessionHost.Unknown)
        {
            events.Insert(0, new HostIdentified(sessionId, Id, receivedAt, host));
        }

        return events;
    }

    /// <summary>
    /// Claude Code tells its hooks where it runs in <c>CLAUDE_CODE_ENTRYPOINT</c> ("claude-desktop",
    /// "claude-vscode", "cli"...). A CLI started in VS Code's integrated terminal says "cli", but the
    /// terminal sets <c>TERM_PROGRAM=vscode</c>, and VS Code is the window to watch then.
    /// </summary>
    public static SessionHost HostOf(IReadOnlyDictionary<string, string> environment)
    {
        ArgumentNullException.ThrowIfNull(environment);

        var entrypoint = environment.GetValueOrDefault("CLAUDE_CODE_ENTRYPOINT") ?? string.Empty;
        var terminal = environment.GetValueOrDefault("TERM_PROGRAM") ?? string.Empty;

        if (entrypoint.Contains("desktop", StringComparison.OrdinalIgnoreCase))
        {
            return SessionHost.ClaudeDesktop;
        }

        if (entrypoint.Contains("vscode", StringComparison.OrdinalIgnoreCase)
            || terminal.Equals("vscode", StringComparison.OrdinalIgnoreCase))
        {
            return SessionHost.VsCode;
        }

        return entrypoint.Equals("cli", StringComparison.OrdinalIgnoreCase) ? SessionHost.Terminal : SessionHost.Unknown;
    }

    // "Claude is waiting for your input" fires after a finished turn sits idle: not a new request.
    private static bool IsAttentionNotification(JsonElement payload)
    {
        var type = Text(payload, "notification_type");
        if (type is not null)
        {
            return type is not "idle_prompt";
        }

        var message = Text(payload, "message") ?? string.Empty;
        return !message.Contains("waiting for your input", StringComparison.OrdinalIgnoreCase);
    }

    private async Task AddContextUsageAsync(
        List<SessionEvent> events,
        JsonElement payload,
        string sessionId,
        DateTimeOffset at,
        CancellationToken cancellationToken)
    {
        if (Text(payload, "transcript_path") is { } path
            && await _usageReader.ReadContextUsageAsync(path, cancellationToken).ConfigureAwait(false) is { } usage)
        {
            events.Add(new ContextUsageMeasured(sessionId, Id, at, usage));
        }
    }

    private static string? ProjectName(string? cwd) =>
        string.IsNullOrWhiteSpace(cwd) ? null : PathText.LastSegment(cwd) is { Length: > 0 } name ? name : cwd;

    private static string? Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
