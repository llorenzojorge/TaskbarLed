using System.Globalization;
using System.Text.Json;
using TaskbarLed.Core.Sessions;

namespace TaskbarLed.Sources.ClaudeCode.Tests;

public class ClaudeCodeSourceTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 14, 3, 28, 0, TimeSpan.Zero);
    private readonly FakeInterruptionReader _interruptions = new();
    private readonly ClaudeCodeSource _source;

    public ClaudeCodeSourceTests()
    {
        var culture = CultureInfo.GetCultureInfo("en-US");
        _source = new ClaudeCodeSource(new ToolActivityFormatter(ActivityStrings.English, culture), _interruptions);
    }

    [Fact]
    public async Task UserPromptSubmit_becomes_PromptSubmitted_with_title_and_project()
    {
        var events = await Translate("""
            {"session_id":"s1","hook_event_name":"UserPromptSubmit","cwd":"C:\\Projetos\\TaskbarLed","prompt":"hi","session_title":"LED panel"}
            """);

        var prompt = Assert.IsType<PromptSubmitted>(Assert.Single(events));
        Assert.Equal("s1", prompt.SessionId);
        Assert.Equal("LED panel", prompt.Title);
        Assert.Equal("TaskbarLed", prompt.ProjectName);
        Assert.Equal(At, prompt.Timestamp);
    }

    [Fact]
    public async Task Prompt_and_stop_name_the_chat_from_its_transcript_title()
    {
        var path = Path.Combine(Path.GetTempPath(), $"chat-{Guid.NewGuid():N}.jsonl");
        await File.WriteAllTextAsync(path, """{"type":"custom-title","customTitle":"Bug visual da taskbar"}""" + "\n");
        try
        {
            var json = JsonSerializer.Serialize(path);
            foreach (var hook in new[] { "UserPromptSubmit", "Stop" })
            {
                var events = await Translate(
                    $$"""{"session_id":"s1","hook_event_name":"{{hook}}","cwd":"C:\\scratch-2026-11-21-53192f","transcript_path":{{json}}}""");

                Assert.Equal("Bug visual da taskbar", Assert.Single(events.OfType<TitleIdentified>()).Title);
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task A_chat_first_seen_mid_prompt_gets_its_title_folder_and_real_prompt_start()
    {
        var path = Path.Combine(Path.GetTempPath(), $"chat-{Guid.NewGuid():N}.jsonl");
        await File.WriteAllTextAsync(path, """
            {"type":"user","origin":{"kind":"human"},"timestamp":"2026-11-14T03:20:00Z","message":{"content":"faz o portfólio"}}
            {"type":"custom-title","customTitle":"Portfólio pessoal minimalista"}
            """ + "\n");
        try
        {
            var json = JsonSerializer.Serialize(path);
            var tool = $$"""{"session_id":"s9","hook_event_name":"PreToolUse","tool_name":"Read","tool_input":{},"cwd":"C:\\scratch-1","transcript_path":{{json}}}""";

            var events = await Translate(tool);

            var prompt = Assert.Single(events.OfType<PromptSubmitted>());
            Assert.Equal(new DateTimeOffset(2026, 11, 14, 3, 20, 0, TimeSpan.Zero), prompt.Timestamp);
            Assert.Equal("Portfólio pessoal minimalista", Assert.Single(events.OfType<TitleIdentified>()).Title);
            Assert.Equal("scratch-1", Assert.Single(events.OfType<SessionStarted>()).ProjectName);
            Assert.IsType<ActivityStarted>(events[^1]);

            // Only once: later events of the same chat are translated as usual.
            Assert.IsType<ActivityStarted>(Assert.Single(await Translate(tool)));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task PreToolUse_describes_the_tool()
    {
        var events = await Translate("""
            {"session_id":"s1","hook_event_name":"PreToolUse","tool_name":"Edit","tool_input":{"file_path":"C:\\src\\scraper.py"}}
            """);

        Assert.Equal("Editing scraper.py", Assert.IsType<ActivityStarted>(Assert.Single(events)).Activity);
    }

    [Theory]
    [InlineData("AskUserQuestion")]
    [InlineData("ExitPlanMode")]
    public async Task Tools_that_ask_the_user_request_attention(string tool)
    {
        var events = await Translate("""{"session_id":"s1","hook_event_name":"PreToolUse","tool_input":{},"tool_name":""" + $"\"{tool}\"}}");

        Assert.IsType<AttentionRequested>(Assert.Single(events));
    }

    [Fact]
    public async Task Stop_completes_with_the_last_message()
    {
        var events = await Translate("""
            {"session_id":"s1","hook_event_name":"Stop","transcript_path":"C:\\t.jsonl","last_assistant_message":"Done ✓"}
            """);

        Assert.Equal("Done ✓", Assert.IsType<ResponseCompleted>(Assert.Single(events)).LastMessage);
    }

    [Fact]
    public async Task Permission_notification_requests_attention()
    {
        var events = await Translate("""
            {"session_id":"s1","hook_event_name":"Notification","notification_type":"permission_prompt","message":"Claude needs your permission to use Bash"}
            """);

        Assert.Equal("Claude needs your permission to use Bash", Assert.IsType<AttentionRequested>(Assert.Single(events)).Reason);
    }

    [Theory]
    [InlineData("""{"session_id":"s1","hook_event_name":"Notification","notification_type":"idle_prompt","message":"x"}""")]
    [InlineData("""{"session_id":"s1","hook_event_name":"Notification","message":"Claude is waiting for your input"}""")]
    public async Task Idle_reminders_are_ignored(string json)
    {
        Assert.Empty(await Translate(json));
    }

    [Theory]
    [InlineData("""{"hook_event_name":"Stop"}""")]
    [InlineData("""{"session_id":"s1"}""")]
    [InlineData("""{"session_id":"s1","hook_event_name":"SomethingNew"}""")]
    [InlineData("[]")]
    public async Task Unknown_or_incomplete_payloads_produce_nothing(string json)
    {
        Assert.Empty(await Translate(json));
    }

    [Fact]
    public async Task SessionEnd_ends_the_session()
    {
        var events = await Translate("""{"session_id":"s1","hook_event_name":"SessionEnd"}""");
        Assert.IsType<SessionEnded>(Assert.Single(events));
    }

    [Fact]
    public async Task Quiet_session_whose_transcript_shows_an_interruption_is_reported_interrupted()
    {
        var interruptedAt = At.AddSeconds(3);
        _interruptions.Results["C:\\t.jsonl"] = interruptedAt;
        await Translate("""{"session_id":"s1","hook_event_name":"UserPromptSubmit","transcript_path":"C:\\t.jsonl"}""");
        await Translate("""{"session_id":"s2","hook_event_name":"UserPromptSubmit","transcript_path":"C:\\other.jsonl"}""");

        var events = await _source.CheckQuietSessionsAsync(
            [Session.Create("s1", ClaudeCodeSource.Id, At), Session.Create("s2", ClaudeCodeSource.Id, At), Session.Create("unknown", ClaudeCodeSource.Id, At)],
            CancellationToken.None);

        var interrupted = Assert.IsType<PromptInterrupted>(Assert.Single(events));
        Assert.Equal("s1", interrupted.SessionId);
        Assert.Equal(interruptedAt, interrupted.Timestamp);
    }

    [Theory]
    [InlineData("claude-desktop", null, SessionHost.ClaudeDesktop)]
    [InlineData("claude-vscode", null, SessionHost.VsCode)]
    [InlineData("cli", "vscode", SessionHost.VsCode)]
    [InlineData("cli", "WindowsTerminal", SessionHost.Terminal)]
    [InlineData("cli", null, SessionHost.Terminal)]
    [InlineData(null, null, SessionHost.Unknown)]
    public void Host_comes_from_the_entrypoint_and_the_terminal(string? entrypoint, string? terminal, SessionHost expected)
    {
        var environment = new Dictionary<string, string>();
        if (entrypoint is not null)
        {
            environment["CLAUDE_CODE_ENTRYPOINT"] = entrypoint;
        }

        if (terminal is not null)
        {
            environment["TERM_PROGRAM"] = terminal;
        }

        Assert.Equal(expected, ClaudeCodeSource.HostOf(environment));
    }

    [Fact]
    public async Task Events_from_a_known_app_carry_the_host_first()
    {
        var events = await Translate(
            """{"session_id":"s1","hook_event_name":"UserPromptSubmit"}""",
            new Dictionary<string, string> { ["CLAUDE_CODE_ENTRYPOINT"] = "claude-vscode" });

        Assert.Equal(2, events.Count);
        Assert.Equal(SessionHost.VsCode, Assert.IsType<HostIdentified>(events[0]).Host);
        Assert.IsType<PromptSubmitted>(events[1]);
    }

    [Fact]
    public async Task Ignored_payloads_do_not_create_a_session_just_to_report_the_host()
    {
        var events = await Translate(
            """{"session_id":"s1","hook_event_name":"Notification","notification_type":"idle_prompt"}""",
            new Dictionary<string, string> { ["CLAUDE_CODE_ENTRYPOINT"] = "claude-vscode" });

        Assert.Empty(events);
    }

    private async Task<IReadOnlyList<SessionEvent>> Translate(string json, IReadOnlyDictionary<string, string>? environment = null)
    {
        using var document = JsonDocument.Parse(json);
        return await _source.TranslateAsync(document.RootElement, At, environment ?? new Dictionary<string, string>(), CancellationToken.None);
    }

    private sealed class FakeInterruptionReader : ITranscriptInterruptionReader
    {
        public Dictionary<string, DateTimeOffset> Results { get; } = [];

        public ValueTask<DateTimeOffset?> ReadInterruptionAsync(string transcriptPath, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Results.TryGetValue(transcriptPath, out var at) ? at : (DateTimeOffset?)null);
    }
}
