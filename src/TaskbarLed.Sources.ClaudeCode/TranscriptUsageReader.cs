using System.Text.Json;

namespace TaskbarLed.Sources.ClaudeCode;

/// <summary>Reads how full a Claude Code conversation's context window is, from its transcript.</summary>
public interface ITranscriptUsageReader
{
    /// <summary>Fraction (0..1) of the context window in use, or null when it cannot be determined.</summary>
    ValueTask<double?> ReadContextUsageAsync(string transcriptPath, CancellationToken cancellationToken);
}

/// <summary>
/// Reads the tail of the JSONL transcript and finds the latest main-thread assistant message with
/// token usage. Only the tail is read, so cost stays constant however long the conversation gets.
/// </summary>
public sealed class TranscriptUsageReader : ITranscriptUsageReader
{
    /// <summary>Standard context window.</summary>
    public const long DefaultContextWindow = 200_000;

    /// <summary>Extended context window (models with the <c>[1m]</c> suffix).</summary>
    public const long ExtendedContextWindow = 1_000_000;

    private const int TailBytes = 512 * 1024;

    public async ValueTask<double?> ReadContextUsageAsync(string transcriptPath, CancellationToken cancellationToken)
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
    public static double? FromJsonLines(string jsonLines)
    {
        ArgumentNullException.ThrowIfNull(jsonLines);

        var lines = jsonLines.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (var i = lines.Length - 1; i >= 0; i--)
        {
            if (TryReadUsage(lines[i], out var tokens, out var model))
            {
                var window = model.Contains("[1m]", StringComparison.OrdinalIgnoreCase) || tokens > DefaultContextWindow
                    ? ExtendedContextWindow
                    : DefaultContextWindow;
                return Math.Clamp(tokens / (double)window, 0, 1);
            }
        }

        return null;
    }

    private static bool TryReadUsage(string line, out long tokens, out string model)
    {
        tokens = 0;
        model = string.Empty;
        if (!line.StartsWith('{'))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (!root.TryGetProperty("type", out var type) || type.GetString() != "assistant"
                || (root.TryGetProperty("isSidechain", out var sidechain) && sidechain.ValueKind == JsonValueKind.True)
                || !root.TryGetProperty("message", out var message)
                || !message.TryGetProperty("usage", out var usage))
            {
                return false;
            }

            tokens = Number(usage, "input_tokens")
                + Number(usage, "cache_creation_input_tokens")
                + Number(usage, "cache_read_input_tokens")
                + Number(usage, "output_tokens");
            model = message.TryGetProperty("model", out var m) && m.GetString() is { } name ? name : string.Empty;
            return tokens > 0;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static long Number(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.TryGetInt64(out var number) ? number : 0;
}
