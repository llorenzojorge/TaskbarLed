using System.Globalization;
using System.Text.Json;

namespace TaskbarLed.Sources.ClaudeCode;

/// <summary>Finds out from the transcript whether the user interrupted the prompt in flight.</summary>
public interface ITranscriptInterruptionReader
{
    /// <summary>When the user interrupted, if that is the latest thing in the conversation; otherwise null.</summary>
    ValueTask<DateTimeOffset?> ReadInterruptionAsync(string transcriptPath, CancellationToken cancellationToken);
}

/// <summary>
/// Claude Code runs no Stop hook when the user presses Esc or the stop button. It does write a user
/// message "[Request interrupted by user]" (or "... for tool use") to the transcript, so a quiet
/// session whose latest main-thread message is that marker was interrupted, not still working.
/// </summary>
public sealed class TranscriptInterruptionReader : ITranscriptInterruptionReader
{
    private const string Marker = "[Request interrupted by user";
    private const int TailBytes = 64 * 1024;

    public async ValueTask<DateTimeOffset?> ReadInterruptionAsync(string transcriptPath, CancellationToken cancellationToken)
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
            return null; // The agent is writing the file; the next check will try again.
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Parses transcript lines (newest last). Public for tests.</summary>
    public static DateTimeOffset? FromJsonLines(string jsonLines)
    {
        ArgumentNullException.ThrowIfNull(jsonLines);

        var lines = jsonLines.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (var i = lines.Length - 1; i >= 0; i--)
        {
            switch (Classify(lines[i], out var at))
            {
                case Entry.Interruption:
                    return at;
                case Entry.Message:
                    return null;
                default:
                    continue; // Bookkeeping lines (hook summaries, snapshots, queue operations...).
            }
        }

        return null;
    }

    private enum Entry
    {
        Other,
        Message,
        Interruption,
    }

    private static Entry Classify(string line, out DateTimeOffset? at)
    {
        at = null;
        if (!line.StartsWith('{'))
        {
            return Entry.Other;
        }

        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            var type = root.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
            if (type is not ("user" or "assistant")
                || (root.TryGetProperty("isSidechain", out var sidechain) && sidechain.ValueKind == JsonValueKind.True))
            {
                return Entry.Other;
            }

            if (type == "user" && root.TryGetProperty("message", out var message) && IsMarker(message))
            {
                at = root.TryGetProperty("timestamp", out var ts)
                    && ts.ValueKind == JsonValueKind.String
                    && DateTimeOffset.TryParse(ts.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
                        ? parsed
                        : null;
                return at is null ? Entry.Message : Entry.Interruption;
            }

            return Entry.Message;
        }
        catch (JsonException)
        {
            return Entry.Other;
        }
    }

    private static bool IsMarker(JsonElement message)
    {
        if (!message.TryGetProperty("content", out var content))
        {
            return false;
        }

        if (content.ValueKind == JsonValueKind.String)
        {
            return content.GetString()?.StartsWith(Marker, StringComparison.Ordinal) == true;
        }

        return content.ValueKind == JsonValueKind.Array
            && content.EnumerateArray().Any(part =>
                part.ValueKind == JsonValueKind.Object
                && part.TryGetProperty("text", out var text)
                && text.ValueKind == JsonValueKind.String
                && text.GetString()?.StartsWith(Marker, StringComparison.Ordinal) == true);
    }
}
