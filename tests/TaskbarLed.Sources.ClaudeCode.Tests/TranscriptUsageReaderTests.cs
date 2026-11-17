using System.Text.Json.Nodes;

namespace TaskbarLed.Sources.ClaudeCode.Tests;

public class TranscriptUsageReaderTests
{
    private const string User = """{"type":"user","message":{"role":"user","content":"hi"}}""";

    private static string Assistant(int input, int cacheRead, int output, string model = "claude-opus-5-5", bool sidechain = false) =>
        new JsonObject
        {
            ["type"] = "assistant",
            ["isSidechain"] = sidechain,
            ["message"] = new JsonObject
            {
                ["model"] = model,
                ["usage"] = new JsonObject
                {
                    ["input_tokens"] = input,
                    ["cache_creation_input_tokens"] = 0,
                    ["cache_read_input_tokens"] = cacheRead,
                    ["output_tokens"] = output,
                },
            },
        }.ToJsonString();

    [Fact]
    public void Uses_the_latest_main_thread_assistant_usage()
    {
        var lines = string.Join('\n', Assistant(10, 0, 0), User, Assistant(1_000, 79_000, 20_000), Assistant(5, 5, 5, sidechain: true));

        Assert.Equal(0.5, TranscriptUsageReader.FromJsonLines(lines));
    }

    [Fact]
    public void Extended_context_models_use_the_one_million_window()
    {
        Assert.Equal(0.25, TranscriptUsageReader.FromJsonLines(Assistant(0, 250_000, 0, "claude-opus-5-5[1m]")));
    }

    [Fact]
    public void Usage_above_the_standard_window_implies_the_extended_one()
    {
        Assert.Equal(0.3, TranscriptUsageReader.FromJsonLines(Assistant(0, 300_000, 0)));
    }

    [Theory]
    [InlineData("")]
    [InlineData(User)]
    [InlineData("{broken json")]
    public void Returns_null_without_usage(string lines)
    {
        Assert.Null(TranscriptUsageReader.FromJsonLines(lines));
    }

    [Fact]
    public async Task Reads_a_transcript_file_shared_with_a_writer()
    {
        var path = Path.Combine(Path.GetTempPath(), $"taskbarled-{Guid.NewGuid():N}.jsonl");
        try
        {
            await File.WriteAllTextAsync(path, User + "\n" + Assistant(0, 100_000, 0) + "\n");
            await using var writer = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);

            var usage = await new TranscriptUsageReader().ReadContextUsageAsync(path, CancellationToken.None);

            Assert.Equal(0.5, usage);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Only_a_jsonl_file_is_opened()
    {
        var path = Path.Combine(Path.GetTempPath(), $"taskbarled-{Guid.NewGuid():N}.txt");
        try
        {
            await File.WriteAllTextAsync(path, User + "\n" + Assistant(0, 100_000, 0) + "\n");

            Assert.Null(await new TranscriptUsageReader().ReadContextUsageAsync(path, CancellationToken.None));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(@"\\server\share\transcript.jsonl")]
    [InlineData("//server/share/transcript.jsonl")]
    [InlineData(@"relative\transcript.jsonl")]
    public async Task Network_and_relative_paths_are_never_opened(string path)
    {
        Assert.Null(await new TranscriptUsageReader().ReadContextUsageAsync(path, CancellationToken.None));
        Assert.Null(await new TranscriptInterruptionReader().ReadInterruptionAsync(path, CancellationToken.None));
    }

    [Fact]
    public async Task Missing_file_gives_null()
    {
        Assert.Null(await new TranscriptUsageReader().ReadContextUsageAsync(@"C:\nope\missing.jsonl", CancellationToken.None));
    }
}
