using TaskbarLed.Sources.ClaudeCode;

namespace TaskbarLed.Sources.ClaudeCode.Tests;

public class TranscriptInfoReaderTests
{
    [Fact]
    public void Title_the_user_gave_the_chat_wins_over_the_generated_one()
    {
        const string lines = """
            {"type":"custom-title","customTitle":"Multi-agents com Hermes e Orca","sessionId":"s1"}
            {"type":"assistant","message":{"content":"oi"}}
            {"type":"ai-title","aiTitle":"Generated name","sessionId":"s1"}
            """;

        Assert.Equal("Multi-agents com Hermes e Orca", TranscriptInfoReader.FromJsonLines(lines).Title);
    }

    [Fact]
    public void Generated_title_is_used_when_the_chat_was_never_renamed_and_the_newest_wins()
    {
        const string lines = """
            {"type":"ai-title","aiTitle":"Old name","sessionId":"s1"}
            {"type":"ai-title","aiTitle":"Bug visual da taskbar","sessionId":"s1"}
            """;

        Assert.Equal("Bug visual da taskbar", TranscriptInfoReader.FromJsonLines(lines).Title);
    }

    [Theory]
    [InlineData("""{"type":"assistant","message":{"content":"my-title\" is not a title"}}""")]
    [InlineData("""{"type":"ai-title","aiTitle":42}""")]
    [InlineData("""{"type":"custom-title","customTitle":"   "}""")]
    [InlineData("""{"type":"ai-title", broken""")]
    [InlineData("")]
    public void Anything_else_gives_no_title(string lines)
    {
        Assert.Null(TranscriptInfoReader.FromJsonLines(lines).Title);
    }

    [Fact]
    public void Last_prompt_is_the_latest_message_a_human_typed()
    {
        const string lines = """
            {"type":"user","origin":{"kind":"human"},"timestamp":"2026-11-21T05:32:48.955Z","message":{"content":"first"}}
            {"type":"user","origin":{"kind":"human"},"timestamp":"2026-11-21T05:57:25.801Z","message":{"content":"second"}}
            {"type":"assistant","timestamp":"2026-11-21T05:57:30Z","message":{"content":"..."}}
            {"type":"user","timestamp":"2026-11-21T05:58:00Z","message":{"content":[{"type":"tool_result"}]}}
            {"type":"user","isSidechain":true,"origin":{"kind":"human"},"timestamp":"2026-11-21T05:59:00Z","message":{"content":"agent"}}
            {"type":"user","isMeta":true,"timestamp":"2026-11-21T05:59:30Z","message":{"content":"note"}}
            """;

        Assert.Equal(DateTimeOffset.Parse("2026-11-21T05:57:25.801Z", null), TranscriptInfoReader.FromJsonLines(lines).LastPromptAt);
    }

    [Fact]
    public void Old_transcripts_without_origin_use_plain_text_user_messages()
    {
        const string lines = """
            {"type":"user","timestamp":"2026-11-21T01:00:00Z","message":{"content":"typed"}}
            {"type":"user","timestamp":"2026-11-21T01:01:00Z","message":{"content":[{"type":"tool_result"}]}}
            """;

        Assert.Equal(DateTimeOffset.Parse("2026-11-21T01:00:00Z", null), TranscriptInfoReader.FromJsonLines(lines).LastPromptAt);
    }

    [Fact]
    public async Task Reads_a_transcript_file_and_refuses_network_paths()
    {
        var path = Path.Combine(Path.GetTempPath(), $"title-{Guid.NewGuid():N}.jsonl");
        await File.WriteAllTextAsync(path, """{"type":"ai-title","aiTitle":"README do plugin"}""" + "\n");
        try
        {
            var reader = new TranscriptInfoReader();
            Assert.Equal("README do plugin", (await reader.ReadAsync(path, CancellationToken.None))?.Title);
            Assert.Null(await reader.ReadAsync(@"\\server\share\t.jsonl", CancellationToken.None));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
