using TaskbarLed.Core.Sessions;
using TaskbarLed.Core.Visibility;
using static TaskbarLed.Core.Tests.Build;

namespace TaskbarLed.Core.Tests;

public class SessionVisibilityTests
{
    private const double Threshold = 0.5;

    private static readonly EditorWindow ProspectWindow = new("progresso.md - prospect - Visual Studio Code", 1, 1);
    private static readonly EditorWindow WebappWindow = new("app.ts - webapp - Visual Studio Code", 0, 2);

    [Fact]
    public void Vs_code_session_is_out_of_sight_while_only_the_Claude_app_is_on_screen()
    {
        var sessions = new[] { Running("s1", SessionHost.VsCode, "prospect") };
        var windows = new HostWindows(1, [ProspectWindow with { VisibleFraction = 0 }]);

        Assert.True(SessionVisibility.AnyOutOfSight(sessions, windows, Threshold));
    }

    [Fact]
    public void Vs_code_session_is_in_sight_when_its_window_is_visible_even_with_Claude_minimized()
    {
        var sessions = new[] { Running("s1", SessionHost.VsCode, "prospect") };
        var windows = new HostWindows(0, [ProspectWindow, WebappWindow]);

        Assert.False(SessionVisibility.AnyOutOfSight(sessions, windows, Threshold));
    }

    [Fact]
    public void The_window_of_another_project_does_not_count()
    {
        var session = Running("s1", SessionHost.VsCode, "webapp");
        var windows = new HostWindows(0, [ProspectWindow, WebappWindow]);

        Assert.False(SessionVisibility.IsInSight(session, windows, Threshold));
        Assert.Equal(WebappWindow, SessionVisibility.EditorFor(session, windows.Editors));
    }

    [Fact]
    public void Minimizing_one_of_two_vs_code_windows_shows_the_panel_for_the_session_running_in_it()
    {
        var sessions = new[]
        {
            Running("s1", SessionHost.VsCode, "prospect"),
            Running("s2", SessionHost.VsCode, "webapp"),
        };
        var windows = new HostWindows(0, [ProspectWindow, WebappWindow]); // webapp minimized, prospect on screen.

        Assert.True(SessionVisibility.AnyOutOfSight(sessions, windows, Threshold));
        Assert.True(SessionVisibility.IsInSight(sessions[0], windows, Threshold));
        Assert.False(SessionVisibility.IsInSight(sessions[1], windows, Threshold));
    }

    [Fact]
    public void Without_a_title_match_the_most_visible_editor_counts()
    {
        var session = Running("s1", SessionHost.VsCode, "unknown-folder");

        Assert.Equal(ProspectWindow, SessionVisibility.EditorFor(session, [WebappWindow, ProspectWindow]));
    }

    [Theory]
    [InlineData(SessionHost.ClaudeDesktop)]
    [InlineData(SessionHost.Unknown)]
    [InlineData(SessionHost.Terminal)]
    public void Other_hosts_follow_the_Claude_app(SessionHost host)
    {
        var session = Running("s1", host, "prospect");

        Assert.True(SessionVisibility.IsInSight(session, new HostWindows(1, [ProspectWindow]), Threshold));
        Assert.False(SessionVisibility.IsInSight(session, new HostWindows(0, [ProspectWindow]), Threshold));
    }

    [Fact]
    public void Panel_shows_when_any_active_session_is_out_of_sight()
    {
        var sessions = new[]
        {
            Running("desktop", SessionHost.ClaudeDesktop, "api"),
            Running("editor", SessionHost.VsCode, "prospect"),
        };

        Assert.True(SessionVisibility.AnyOutOfSight(sessions, new HostWindows(1, []), Threshold));
        Assert.True(SessionVisibility.AnyOutOfSight(sessions, new HostWindows(0, [ProspectWindow]), Threshold));
        Assert.False(SessionVisibility.AnyOutOfSight(sessions, new HostWindows(1, [ProspectWindow]), Threshold));
    }

    [Fact]
    public void Idle_sessions_do_not_keep_the_panel_up()
    {
        var sessions = new[] { Session("old", SessionStatus.Idle) with { Host = SessionHost.VsCode } };

        Assert.False(SessionVisibility.AnyOutOfSight(sessions, new HostWindows(1, []), Threshold));
        Assert.True(SessionVisibility.AnyOutOfSight(sessions, new HostWindows(0, []), Threshold));
    }

    private static Session Running(string id, SessionHost host, string project) =>
        Session(id, SessionStatus.Running) with { Host = host, ProjectName = project };
}
