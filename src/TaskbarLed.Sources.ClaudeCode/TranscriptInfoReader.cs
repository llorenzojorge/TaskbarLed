using System.Globalization;
using System.Text.Json;

namespace TaskbarLed.Sources.ClaudeCode;

/// <summary>What the panel takes from a conversation's transcript.</summary>
/// <param name="Title">The chat's title, as the Claude app shows it in its chat list; null when it has none yet.</param>
/// <param name="LastPromptAt">When the user sent the latest prompt; null when not found in the tail.</param>
public sealed record TranscriptInfo(string? Title, DateTimeOffset? LastPromptAt);

/// <summary>Reads a Claude Code conversation's title and latest prompt time from its transcript.</summary>
public interface ITranscriptInfoReader
{
    /// <summary>What the transcript tells; null when it cannot be read.</summary>
    ValueTask<TranscriptInfo?> ReadAsync(string transcriptPath, CancellationToken cancellationToken);
}

/// <summary>
/// Claude Code writes the title into the JSONL transcript: <c>{"type":"custom-title","customTitle":...}</c>
/// when the user renames the chat, <c>{"type":"ai-title","aiTitle":...}</c> when it names it itself. Both are
/// repeated as the conversation goes on, so the tail is enough. A user's name wins over a generated one.
/// Hook payloads only give the working folder, and the Claude app's scratch chats all live in folders
/// like <c>scratch-2026-11-21-53192f</c>, which tell the chats apart for nobody.
/// The latest human prompt's timestamp lets a panel that started mid-prompt (or was restarted) show
/// the real prompt duration instead of counting from the first event it happened to see.
/// </summary>
public sealed class TranscriptInfoReader : ITranscriptInfoReader
{
    // A long turn writes a lot of tool output after the prompt; 1 MB covers it and is read rarely.
    private const int TailBytes = 1024 * 1024;
    private const int MaxTitleLength = 200;

    public async ValueTask<TranscriptInfo?> ReadAsync(string transcriptPath, CancellationToken cancellationToken)
    {
        if (!TranscriptTail.IsLocalTranscript(transcriptPath) || !File.Exists(transcriptPath))
        {
            return null;
        }

        try
        {
            var tail = await TranscriptTail.ReadAsync(transcriptPath, TailBytes, cancellationToken).ConfigureAwait(false);
            return FromJsonLines(tail);
        }
        catch (IOException)
        {
            return null; // The agent is writing the file; the next event will try again.
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Parses transcript lines (newest last). Public for tests.</summary>
    public static TranscriptInfo FromJsonLines(string jsonLines)
    {
        ArgumentNullException.ThrowIfNull(jsonLines);

        string? custom = null;
        string? generated = null;
        DateTimeOffset? lastPrompt = null;

        var lines = jsonLines.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (var i = lines.Length - 1; i >= 0 && (custom is null || lastPrompt is null); i--)
        {
            var line = lines[i];
            if (!line.StartsWith('{'))
            {
                continue;
            }

            // Cheap filters first: most lines are large messages that are neither.
            var maybeTitle = custom is null && line.Contains("-title\"", StringComparison.Ordinal);
            var maybePrompt = lastPrompt is null && line.Contains("\"type\":\"user\"", StringComparison.Ordinal);
            if (!maybeTitle && !maybePrompt)
            {
                continue;
            }

            try
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                switch (String(root, "type"))
                {
                    case "custom-title" when custom is null:
                        custom = Title(root, "customTitle");
                        break;
                    case "ai-title" when generated is null:
                        generated = Title(root, "aiTitle");
                        break;
                    case "user" when lastPrompt is null && IsHumanPrompt(root):
                        lastPrompt = Timestamp(root);
                        break;
                }
            }
            catch (JsonException)
            {
                // A line cut while being written; skip it.
            }
        }

        return new TranscriptInfo(custom ?? generated, lastPrompt);
    }

    // A prompt the user typed, not a tool result, a sub-agent's message or a note Claude Code injects.
    private static bool IsHumanPrompt(JsonElement root)
    {
        if (root.TryGetProperty("isSidechain", out var sidechain) && sidechain.ValueKind == JsonValueKind.True)
        {
            return false;
        }

        if (root.TryGetProperty("isMeta", out var meta) && meta.ValueKind == JsonValueKind.True)
        {
            return false;
        }

        if (root.TryGetProperty("origin", out var origin) && origin.ValueKind == JsonValueKind.Object)
        {
            return String(origin, "kind") == "human";
        }

        // Older transcripts have no origin: a typed prompt is plain text, a tool result is an array.
        return root.TryGetProperty("message", out var message)
            && message.ValueKind == JsonValueKind.Object
            && message.TryGetProperty("content", out var content)
            && content.ValueKind == JsonValueKind.String;
    }

    private static DateTimeOffset? Timestamp(JsonElement root) =>
        String(root, "timestamp") is { } text
        && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at)
            ? at
            : null;

    private static string? Title(JsonElement root, string property) =>
        String(root, property)?.Trim() is { Length: > 0 } title
            ? title.Length > MaxTitleLength ? title[..MaxTitleLength] : title
            : null;

    private static string? String(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
