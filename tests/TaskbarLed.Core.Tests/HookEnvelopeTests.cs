using System.Text;
using TaskbarLed.Core.Ipc;

namespace TaskbarLed.Core.Tests;

public class HookEnvelopeTests
{
    [Fact]
    public void Round_trips_source_time_and_payload_including_accents()
    {
        var at = new DateTimeOffset(2026, 11, 14, 3, 28, 9, TimeSpan.FromHours(-3));
        var payload = Encoding.UTF8.GetBytes("""{"session_id":"abc","message":"Configurações"}""");

        var bytes = HookEnvelope.Encode("claude-code", at, payload);

        Assert.True(HookEnvelope.TryDecode(bytes, out var envelope));
        Assert.Equal("claude-code", envelope!.Source);
        Assert.Equal(at, envelope.ReceivedAt);
        Assert.Equal("Configurações", envelope.Payload.GetProperty("message").GetString());
    }

    [Fact]
    public void Round_trips_the_captured_environment()
    {
        var env = new Dictionary<string, string> { ["CLAUDE_CODE_ENTRYPOINT"] = "claude-vscode", ["TERM_PROGRAM"] = "vscode" };

        var bytes = HookEnvelope.Encode("claude-code", DateTimeOffset.Now, Encoding.UTF8.GetBytes("{}"), env);

        Assert.True(HookEnvelope.TryDecode(bytes, out var envelope));
        Assert.Equal(env, envelope!.Environment);
    }

    [Fact]
    public void Envelopes_from_older_hooks_decode_with_an_empty_environment()
    {
        var text = """{"v":1,"source":"claude-code","receivedAt":"2026-01-01T00:00:00Z","payload":{}}""";

        Assert.True(HookEnvelope.TryDecode(Encoding.UTF8.GetBytes(text), out var envelope));
        Assert.Empty(envelope!.Environment);
    }

    [Fact]
    public void Encode_rejects_invalid_json()
    {
        Assert.ThrowsAny<System.Text.Json.JsonException>(
            () => HookEnvelope.Encode("x", DateTimeOffset.Now, Encoding.UTF8.GetBytes("{not json")));
    }

    [Theory]
    [InlineData("garbage")]
    [InlineData("""{"v":1,"source":"x","receivedAt":"2026-01-01T00:00:00Z"}""")]
    [InlineData("""{"v":99,"source":"x","receivedAt":"2026-01-01T00:00:00Z","payload":{}}""")]
    [InlineData("""{"v":"1","source":"x","receivedAt":"2026-01-01T00:00:00Z","payload":{}}""")]
    [InlineData("""{"v":1,"source":5,"receivedAt":"2026-01-01T00:00:00Z","payload":{}}""")]
    [InlineData("""{"v":1,"source":"x","receivedAt":20260101,"payload":{}}""")]
    [InlineData("""[1,2,3]""")]
    public void Decode_rejects_malformed_or_future_envelopes(string text)
    {
        Assert.False(HookEnvelope.TryDecode(Encoding.UTF8.GetBytes(text), out _));
    }
}
