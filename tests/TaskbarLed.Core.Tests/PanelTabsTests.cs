using TaskbarLed.Core.Presentation;
using TaskbarLed.Core.Sessions;

namespace TaskbarLed.Core.Tests;

public class PanelTabsTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 28, 15, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(3);

    private static Session At(string id, SessionStatus status, double minutesAgo) =>
        Session.Create(id, "claude-code", Now.AddMinutes(-minutesAgo)) with { Status = status };

    [Fact]
    public void An_idle_chat_loses_its_tab_after_three_quiet_minutes()
    {
        var shown = PanelTabs.Shown([At("read", SessionStatus.Idle, 3.1), At("fresh", SessionStatus.Idle, 2.9)], Now, Timeout);

        Assert.Equal(["fresh"], shown.Select(s => s.Id));
    }

    [Theory]
    [InlineData(SessionStatus.Running)]
    [InlineData(SessionStatus.AwaitingInput)]
    [InlineData(SessionStatus.Completed)]
    public void Running_waiting_and_unseen_finished_sessions_keep_their_tab_however_quiet(SessionStatus status)
    {
        var shown = PanelTabs.Shown([At("busy", status, 90)], Now, Timeout);

        Assert.Single(shown);
    }

    [Fact]
    public void Tabs_keep_their_order()
    {
        var shown = PanelTabs.Shown(
            [At("a", SessionStatus.Running, 10), At("b", SessionStatus.Idle, 30), At("c", SessionStatus.Idle, 1)], Now, Timeout);

        Assert.Equal(["a", "c"], shown.Select(s => s.Id));
    }
}
