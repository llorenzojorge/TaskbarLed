using System.Globalization;
using TaskbarLed.Core.Presentation;
using TaskbarLed.Core.Sessions;
using static TaskbarLed.Core.Tests.Build;

namespace TaskbarLed.Core.Tests;

public class PanelComposerTests
{
    private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");

    private readonly PanelComposer _composer = new(new SessionRotation(TimeSpan.FromSeconds(8)), TickerStrings.Portuguese, Pt);

    [Fact]
    public void Empty_store_shows_the_idle_message()
    {
        var frame = _composer.Compose([], At(0));

        Assert.Equal("Claude ocioso", frame.Feed);
        Assert.False(frame.HasActivity);
        Assert.Empty(frame.Channels);
    }

    [Fact]
    public void Each_session_gets_a_numbered_named_tab_and_the_feed_shows_the_selected_activity()
    {
        Session[] sessions =
        [
            Session("a", SessionStatus.Running) with
            {
                Title = "webapp", PromptStartedAt = At(0), Activity = "Editando scraper.py",
            },
            Session("b", SessionStatus.Running) with { ProjectName = "api" },
        ];

        var frame = _composer.Compose(sessions, At(252));

        Assert.Equal(["1 webapp", "2 api"], frame.Channels.Select(c => c.Label));
        Assert.Equal([true, false], frame.Channels.Select(c => c.IsSelected));
        Assert.Equal("Editando scraper.py", frame.Feed);
        Assert.Equal("04:12", frame.ElapsedText);
        Assert.True(frame.HasActivity);
    }

    [Fact]
    public void Tabs_carry_the_whole_title_and_only_absurd_ones_are_capped()
    {
        var session = Session("a", SessionStatus.Running) with { Title = "Indicador visual de progresso do Claude" };
        var huge = Session("b", SessionStatus.Running) with { Title = new string('x', 300) };

        var frame = _composer.Compose([session, huge], At(0));

        Assert.Equal("1 Indicador visual de progresso do Claude", frame.Channels[0].Label);
        Assert.Equal(2 + 40, frame.Channels[1].Label.Length);
    }

    [Fact]
    public void Running_without_activity_says_thinking()
    {
        var frame = _composer.Compose([Session("a", SessionStatus.Running)], At(0));
        Assert.Equal("Pensando…", frame.Feed);
    }

    [Fact]
    public void Completed_session_shows_duration_and_last_message_and_blinks()
    {
        var session = Session("a", SessionStatus.Completed) with
        {
            ProjectName = "api", PromptStartedAt = At(0), PromptEndedAt = At(767), LastMessage = "**Pronto.**\nTestes passando.",
        };

        var frame = _composer.Compose([session], At(2000));

        Assert.Equal("Concluído em 12m47s  ›  Pronto. Testes passando.", frame.Feed);
        Assert.Equal("12:47", frame.ElapsedText);
        Assert.True(frame.HasUnseenCompletion);
    }

    [Fact]
    public void Waiting_session_says_so()
    {
        var session = Session("a", SessionStatus.AwaitingInput) with { ProjectName = "site", Activity = "Rodar npm install?" };

        var frame = _composer.Compose([session], At(0));

        Assert.Equal("Aguardando você  ›  Rodar npm install?", frame.Feed);
        Assert.True(frame.NeedsAttention);
    }

    [Fact]
    public void The_chat_is_named_on_its_tab_and_the_feed_keeps_its_room_for_what_happened()
    {
        var done = Session("a", SessionStatus.Completed) with
        {
            Title = "Bug visual da taskbar", PromptStartedAt = At(0), PromptEndedAt = At(28), LastMessage = "Pronto.",
        };

        var frame = _composer.Compose([done], At(60));

        Assert.Equal("1 Bug visual da taskbar", frame.Channels.Single().Label);
        Assert.Equal("Concluído em 28s  ›  Pronto.", frame.Feed);
    }

    [Theory]
    [InlineData("  linha 1\n\n  linha `2`  ", 100, "linha 1 linha 2")]
    [InlineData("my_file.py -> ok", 100, "my_file.py -> ok")]
    [InlineData("abcdefghij", 5, "abcd…")]
    [InlineData(null, 10, "")]
    public void Clean_flattens_text_for_one_line(string? input, int max, string expected)
    {
        Assert.Equal(expected, PanelComposer.Clean(input, max));
    }
}
