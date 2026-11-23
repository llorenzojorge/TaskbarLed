using TaskbarLed.Core.Presentation;
using TaskbarLed.Core.Sessions;
using static TaskbarLed.Core.Tests.Build;

namespace TaskbarLed.Core.Tests;

public class ClickTargetTests
{
    private static Session Finished(string id, int endedAt) =>
        Session(id, SessionStatus.Completed) with { PromptEndedAt = At(endedAt) };

    [Fact]
    public void A_feed_click_opens_a_finished_session_before_the_running_one_in_the_feed()
    {
        Session[] sessions = [Session("running", SessionStatus.Running), Finished("done", 10)];

        Assert.Equal("done", ClickTarget.ForFeed(sessions, "running")!.Id);
    }

    [Fact]
    public void A_feed_click_keeps_the_finished_session_already_in_the_feed()
    {
        Session[] sessions = [Finished("first", 5), Finished("second", 10)];

        Assert.Equal("first", ClickTarget.ForFeed(sessions, "first")!.Id);
    }

    [Fact]
    public void Among_finished_sessions_a_feed_click_opens_the_latest_to_finish()
    {
        Session[] sessions = [Finished("older", 5), Session("running", SessionStatus.Running), Finished("newer", 20)];

        Assert.Equal("newer", ClickTarget.ForFeed(sessions, "running")!.Id);
    }

    [Fact]
    public void With_nothing_finished_a_feed_click_opens_the_session_in_the_feed()
    {
        Session[] sessions = [Session("a", SessionStatus.Running), Session("b", SessionStatus.AwaitingInput)];

        Assert.Equal("b", ClickTarget.ForFeed(sessions, "b")!.Id);
        Assert.Null(ClickTarget.ForFeed([], null));
    }

    [Fact]
    public void A_tab_click_opens_that_tab_whatever_its_state()
    {
        Session[] sessions = [Finished("done", 10), Session("running", SessionStatus.Running)];

        Assert.Equal("running", ClickTarget.ForTab(sessions, "running")!.Id);
        Assert.Equal("done", ClickTarget.ForTab(sessions, "done")!.Id);
        Assert.Null(ClickTarget.ForTab(sessions, "gone"));
    }
}
