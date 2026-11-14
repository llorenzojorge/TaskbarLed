namespace TaskbarLed.Sources.ClaudeCode.Tests;

public class TranscriptInterruptionReaderTests
{
    private const string Prompt =
        """{"type":"user","timestamp":"2026-11-14T21:57:50.700Z","message":{"role":"user","content":[{"type":"text","text":"1: -1,2,3,4;"}]}}""";

    private const string Interrupted =
        """{"type":"user","timestamp":"2026-11-14T21:57:53.039Z","message":{"role":"user","content":[{"type":"text","text":"[Request interrupted by user]"}]}}""";

    private const string InterruptedToolUse =
        """{"type":"user","timestamp":"2026-11-14T21:57:53.039Z","message":{"role":"user","content":"[Request interrupted by user for tool use]"}}""";

    private const string Answer =
        """{"type":"assistant","timestamp":"2026-11-14T21:58:10.000Z","message":{"role":"assistant","content":[{"type":"text","text":"Done"}]}}""";

    private const string Bookkeeping = """{"type":"file-history-snapshot","messageId":"x"}""";

    private static readonly DateTimeOffset InterruptedAt = new(2026, 11, 14, 21, 57, 53, 39, TimeSpan.Zero);

    [Fact]
    public void Interruption_marker_as_the_latest_message_reports_when_it_happened()
    {
        Assert.Equal(InterruptedAt, TranscriptInterruptionReader.FromJsonLines(Lines(Prompt, Interrupted, Bookkeeping)));
    }

    [Fact]
    public void Interruption_during_a_tool_call_is_detected_too()
    {
        Assert.Equal(InterruptedAt, TranscriptInterruptionReader.FromJsonLines(Lines(Prompt, InterruptedToolUse)));
    }

    [Fact]
    public void Work_after_the_interruption_means_the_session_moved_on()
    {
        Assert.Null(TranscriptInterruptionReader.FromJsonLines(Lines(Interrupted, Prompt, Answer)));
    }

    [Fact]
    public void Prompt_still_in_flight_is_not_an_interruption()
    {
        Assert.Null(TranscriptInterruptionReader.FromJsonLines(Lines(Prompt, Bookkeeping)));
    }

    [Fact]
    public void Subagent_lines_are_skipped()
    {
        const string sidechain =
            """{"type":"assistant","isSidechain":true,"timestamp":"2026-11-14T21:57:54.000Z","message":{"content":[{"type":"text","text":"sub"}]}}""";

        Assert.Equal(InterruptedAt, TranscriptInterruptionReader.FromJsonLines(Lines(Prompt, Interrupted, sidechain)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json\n{broken")]
    public void Empty_or_garbled_transcripts_report_nothing(string text)
    {
        Assert.Null(TranscriptInterruptionReader.FromJsonLines(text));
    }

    [Fact]
    public async Task Missing_file_reports_nothing()
    {
        var reader = new TranscriptInterruptionReader();
        Assert.Null(await reader.ReadInterruptionAsync(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".jsonl"), CancellationToken.None));
    }

    private static string Lines(params string[] lines) => string.Join('\n', lines);
}
