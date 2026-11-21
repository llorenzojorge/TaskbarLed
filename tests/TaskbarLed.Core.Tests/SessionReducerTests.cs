using TaskbarLed.Core.Sessions;
using static TaskbarLed.Core.Tests.Build;

namespace TaskbarLed.Core.Tests;

public class SessionReducerTests
{
    [Fact]
    public void Prompt_starts_running_and_records_title_project_and_start_time()
    {
        var session = SessionReducer.Apply(null, new PromptSubmitted("s1", Source, At(0), "Fix login", "api"));

        Assert.NotNull(session);
        Assert.Equal(SessionStatus.Running, session.Status);
        Assert.Equal("Fix login", session.Title);
        Assert.Equal("api", session.ProjectName);
        Assert.Equal(At(0), session.PromptStartedAt);
        Assert.Null(session.PromptEndedAt);
    }

    [Fact]
    public void Conversation_title_replaces_the_folder_name_and_keeps_the_status()
    {
        var session = Fold(
            new PromptSubmitted("s1", Source, At(0), null, "scratch-2026-11-21-53192f"),
            new TitleIdentified("s1", Source, At(1), "  Multi-agents com Hermes e Orca "));

        Assert.Equal("Multi-agents com Hermes e Orca", session.DisplayName);
        Assert.Equal(SessionStatus.Running, session.Status);

        var blank = SessionReducer.Apply(session, new TitleIdentified("s1", Source, At(2), " "))!;
        Assert.Equal("Multi-agents com Hermes e Orca", blank.Title);
    }

    [Fact]
    public void Activity_is_shown_while_running_and_cleared_on_completion()
    {
        var session = Fold(
            new PromptSubmitted("s1", Source, At(0), null, null),
            new ActivityStarted("s1", Source, At(5), "Editing a.cs"));
        Assert.Equal("Editing a.cs", session.Activity);

        session = SessionReducer.Apply(session, new ResponseCompleted("s1", Source, At(30), "All done"))!;

        Assert.Equal(SessionStatus.Completed, session.Status);
        Assert.Null(session.Activity);
        Assert.Equal("All done", session.LastMessage);
        Assert.Equal(TimeSpan.FromSeconds(30), session.Elapsed(At(999)));
    }

    [Fact]
    public void Approval_then_tool_result_resumes_running_without_restarting_the_timer()
    {
        var session = Fold(
            new PromptSubmitted("s1", Source, At(0), null, null),
            new AttentionRequested("s1", Source, At(10), "Run npm install?"));
        Assert.Equal(SessionStatus.AwaitingInput, session.Status);

        session = SessionReducer.Apply(session, new ActivityFinished("s1", Source, At(20)))!;

        Assert.Equal(SessionStatus.Running, session.Status);
        Assert.Equal(At(0), session.PromptStartedAt);
    }

    [Fact]
    public void Tool_event_for_an_unseen_prompt_starts_the_timer_at_that_event()
    {
        var session = SessionReducer.Apply(null, new ActivityStarted("s1", Source, At(42), "Reading x"))!;

        Assert.Equal(SessionStatus.Running, session.Status);
        Assert.Equal(At(42), session.PromptStartedAt);
    }

    [Fact]
    public void Acknowledge_turns_completed_into_idle_and_leaves_other_states_alone()
    {
        Assert.Equal(SessionStatus.Idle, SessionReducer.Acknowledge(Session("a", SessionStatus.Completed)).Status);
        Assert.Equal(SessionStatus.Running, SessionReducer.Acknowledge(Session("a", SessionStatus.Running)).Status);
        Assert.Equal(SessionStatus.AwaitingInput, SessionReducer.Acknowledge(Session("a", SessionStatus.AwaitingInput)).Status);
    }

    [Fact]
    public void Session_end_removes_the_session()
    {
        var session = Fold(new PromptSubmitted("s1", Source, At(0), null, null));
        Assert.Null(SessionReducer.Apply(session, new SessionEnded("s1", Source, At(1))));
    }

    [Fact]
    public void Host_is_recorded_without_changing_the_status()
    {
        var session = Fold(
            new HostIdentified("s1", Source, At(0), SessionHost.VsCode),
            new PromptSubmitted("s1", Source, At(0), null, null));

        Assert.Equal(SessionHost.VsCode, session.Host);
        Assert.Equal(SessionStatus.Running, session.Status);
    }

    [Fact]
    public void Interrupted_prompt_goes_idle_and_stops_the_timer()
    {
        var session = Fold(
            new PromptSubmitted("s1", Source, At(0), null, null),
            new ActivityStarted("s1", Source, At(2), "Editing a.cs"),
            new PromptInterrupted("s1", Source, At(3)));

        Assert.Equal(SessionStatus.Idle, session.Status);
        Assert.Null(session.Activity);
        Assert.Equal(TimeSpan.FromSeconds(3), session.Elapsed(At(999)));
    }

    [Fact]
    public void Interrupting_a_pending_approval_clears_the_attention_state()
    {
        var session = Fold(
            new PromptSubmitted("s1", Source, At(0), null, null),
            new AttentionRequested("s1", Source, At(2), "Needs permission"),
            new PromptInterrupted("s1", Source, At(5)));

        Assert.Equal(SessionStatus.Idle, session.Status);
    }

    [Fact]
    public void Interruption_older_than_the_current_prompt_is_ignored()
    {
        var session = Fold(
            new PromptSubmitted("s1", Source, At(0), null, null),
            new PromptSubmitted("s1", Source, At(10), null, null),
            new PromptInterrupted("s1", Source, At(4)));

        Assert.Equal(SessionStatus.Running, session.Status);
        Assert.Equal(At(10), session.PromptStartedAt);
    }

    [Fact]
    public void Interruption_does_not_touch_a_completed_session()
    {
        var completed = Fold(
            new PromptSubmitted("s1", Source, At(0), null, null),
            new ResponseCompleted("s1", Source, At(5), null));

        Assert.Same(completed, SessionReducer.Apply(completed, new PromptInterrupted("s1", Source, At(6))));
    }

    private static Session Fold(params SessionEvent[] events) =>
        events.Aggregate<SessionEvent, Session?>(null, SessionReducer.Apply)!;
}
