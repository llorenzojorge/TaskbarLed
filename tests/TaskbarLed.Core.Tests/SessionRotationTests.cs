using TaskbarLed.Core.Presentation;
using TaskbarLed.Core.Sessions;
using static TaskbarLed.Core.Tests.Build;

namespace TaskbarLed.Core.Tests;

public class SessionRotationTests
{
    private readonly SessionRotation _rotation = new(TimeSpan.FromSeconds(8));

    [Fact]
    public void Nothing_to_show_returns_null()
    {
        Assert.Null(_rotation.Select([], At(0)));
    }

    [Fact]
    public void Stays_on_a_session_for_the_dwell_time_then_rotates()
    {
        Session[] sessions = [Session("a", SessionStatus.Running), Session("b", SessionStatus.Completed)];

        Assert.Equal("a", _rotation.Select(sessions, At(0))!.Id);
        Assert.Equal("a", _rotation.Select(sessions, At(7))!.Id);
        Assert.Equal("b", _rotation.Select(sessions, At(8))!.Id);
        Assert.Equal("a", _rotation.Select(sessions, At(16))!.Id);
    }

    [Fact]
    public void A_clicked_tab_stays_in_the_feed_for_a_full_dwell()
    {
        Session[] sessions = [Session("a", SessionStatus.Running), Session("b", SessionStatus.Running), Session("c", SessionStatus.Completed)];

        Assert.Equal("a", _rotation.Select(sessions, At(0))!.Id);
        _rotation.Show("c", At(3));
        Assert.Equal("c", _rotation.Select(sessions, At(3))!.Id);
        Assert.Equal("c", _rotation.Select(sessions, At(10))!.Id);
        Assert.Equal("a", _rotation.Select(sessions, At(11))!.Id);
    }

    [Fact]
    public void A_clicked_tab_does_not_push_aside_a_session_waiting_for_the_user()
    {
        Session[] sessions = [Session("a", SessionStatus.Running), Session("b", SessionStatus.AwaitingInput)];

        _rotation.Show("a", At(0));
        Assert.Equal("b", _rotation.Select(sessions, At(0))!.Id);
    }

    [Fact]
    public void A_session_waiting_for_the_user_preempts_the_rotation()
    {
        Session[] sessions =
        [
            Session("a", SessionStatus.Running),
            Session("b", SessionStatus.AwaitingInput),
            Session("c", SessionStatus.Completed),
        ];

        Assert.Equal("b", _rotation.Select(sessions, At(0))!.Id);
        Assert.Equal("b", _rotation.Select(sessions, At(60))!.Id);
    }

    [Fact]
    public void Idle_only_shows_the_most_recently_used_session()
    {
        Session[] sessions = [Session("a", SessionStatus.Idle, 10), Session("b", SessionStatus.Idle, 50)];

        Assert.Equal("b", _rotation.Select(sessions, At(0))!.Id);
    }

    [Fact]
    public void Skip_moves_to_the_next_session_immediately()
    {
        Session[] sessions = [Session("a", SessionStatus.Running), Session("b", SessionStatus.Running)];
        _rotation.Select(sessions, At(0));

        _rotation.Skip();

        Assert.Equal("b", _rotation.Select(sessions, At(1))!.Id);
    }
}
