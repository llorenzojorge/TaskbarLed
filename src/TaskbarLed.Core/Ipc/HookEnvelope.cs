using System.Buffers;
using System.Text.Json;

namespace TaskbarLed.Core.Ipc;

/// <summary>
/// What the hook process hands to the panel: the tool's raw payload plus where and when it came from.
/// The hook does not interpret the payload, so new tool versions never require a new hook binary.
/// </summary>
/// <param name="Environment">
/// The few environment variables of the hook process that say which app runs the agent
/// (<see cref="CapturedVariables"/>); empty for envelopes from older hooks.
/// </param>
public sealed record HookEnvelope(
    int Version,
    string Source,
    DateTimeOffset ReceivedAt,
    JsonElement Payload,
    IReadOnlyDictionary<string, string> Environment)
{
    public const int CurrentVersion = 1;

    /// <summary>
    /// Variables the hook copies into the envelope. Hook payloads do not say where the agent runs
    /// (desktop app, VS Code, a terminal), but the environment the agent gives its hooks does.
    /// Values only, never secrets: keep this list to identifiers like these.
    /// </summary>
    public static IReadOnlyList<string> CapturedVariables { get; } = ["CLAUDE_CODE_ENTRYPOINT", "TERM_PROGRAM"];

    /// <summary>Serializes an envelope around an already-encoded UTF-8 JSON payload.</summary>
    /// <exception cref="JsonException">The payload is not valid JSON.</exception>
    public static byte[] Encode(
        string source,
        DateTimeOffset receivedAt,
        ReadOnlySpan<byte> utf8JsonPayload,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);

        var buffer = new ArrayBufferWriter<byte>(utf8JsonPayload.Length + 256);
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteNumber("v", CurrentVersion);
            writer.WriteString("source", source);
            writer.WriteString("receivedAt", receivedAt);
            if (environment is { Count: > 0 })
            {
                writer.WriteStartObject("env");
                foreach (var (name, value) in environment)
                {
                    writer.WriteString(name, value);
                }

                writer.WriteEndObject();
            }

            writer.WritePropertyName("payload");
            writer.WriteRawValue(utf8JsonPayload, skipInputValidation: false);
            writer.WriteEndObject();
        }

        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>Parses an envelope; returns false for anything malformed or from a newer protocol.</summary>
    public static bool TryDecode(ReadOnlySpan<byte> utf8, out HookEnvelope? envelope)
    {
        envelope = null;
        try
        {
            var reader = new Utf8JsonReader(utf8);
            using var document = JsonDocument.ParseValue(ref reader);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("v", out var version) || !version.TryGetInt32(out var v) || v > CurrentVersion
                || !root.TryGetProperty("source", out var source) || source.GetString() is not { Length: > 0 } sourceId
                || !root.TryGetProperty("receivedAt", out var receivedAt) || !receivedAt.TryGetDateTimeOffset(out var at)
                || !root.TryGetProperty("payload", out var payload))
            {
                return false;
            }

            var environment = new Dictionary<string, string>(StringComparer.Ordinal);
            if (root.TryGetProperty("env", out var env) && env.ValueKind == JsonValueKind.Object)
            {
                foreach (var variable in env.EnumerateObject())
                {
                    if (variable.Value.ValueKind == JsonValueKind.String && variable.Value.GetString() is { } value)
                    {
                        environment[variable.Name] = value;
                    }
                }
            }

            envelope = new HookEnvelope(v, sourceId, at, payload.Clone(), environment);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
