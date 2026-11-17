using System.Text;
using TaskbarLed.Core.Audio;
using TaskbarLed.Core.Presentation;
using TaskbarLed.Core.Sessions;
using TaskbarLed.Core.Settings;
using static TaskbarLed.Core.Tests.Build;

namespace TaskbarLed.Core.Tests;

public class SoundCueSelectorTests
{
    private readonly SoundCueSelector _selector = new();
    private readonly PanelSettings _settings = new();

    private static PanelAlert Done(string id = "a") => new(id, SessionStatus.Completed, "done");

    private static PanelAlert Waiting(string id = "a") => new(id, SessionStatus.AwaitingInput, "waiting");

    private static bool Hidden(string id) => true;

    [Fact]
    public void Sound_is_on_for_both_cues_by_default()
    {
        Assert.Equal(SoundCue.Done, _selector.Pick([Done()], Hidden, _settings, At(0)));
        Assert.Equal(SoundCue.Attention, _selector.Pick([Waiting()], Hidden, _settings, At(10)));
    }

    [Fact]
    public void No_alert_means_no_sound()
    {
        Assert.Null(_selector.Pick([], Hidden, _settings, At(0)));
    }

    [Fact]
    public void A_session_whose_app_is_in_sight_makes_no_sound()
    {
        Assert.Null(_selector.Pick([Done("seen")], id => id != "seen", _settings, At(0)));
    }

    [Fact]
    public void Each_cue_can_be_turned_off_on_its_own()
    {
        var onlyAttention = _settings with { SoundOnDone = false };
        Assert.Null(_selector.Pick([Done()], Hidden, onlyAttention, At(0)));
        Assert.Equal(SoundCue.Attention, _selector.Pick([Waiting()], Hidden, onlyAttention, At(0)));

        var onlyDone = _settings with { SoundOnAttention = false };
        Assert.Null(_selector.Pick([Waiting()], Hidden, onlyDone, At(10)));
        Assert.Equal(SoundCue.Done, _selector.Pick([Done()], Hidden, onlyDone, At(10)));
    }

    [Fact]
    public void Waiting_for_the_user_wins_when_both_happen_in_the_same_frame()
    {
        Assert.Equal(SoundCue.Attention, _selector.Pick([Done("a"), Waiting("b")], Hidden, _settings, At(0)));
    }

    [Fact]
    public void Sessions_finishing_one_after_the_other_make_one_sound()
    {
        Assert.Equal(SoundCue.Done, _selector.Pick([Done("a")], Hidden, _settings, At(0)));
        Assert.Null(_selector.Pick([Done("b")], Hidden, _settings, At(1)));
        Assert.Equal(SoundCue.Done, _selector.Pick([Done("c")], Hidden, _settings, At(2)));
    }

    [Fact]
    public void A_request_right_after_a_done_sound_is_still_played()
    {
        Assert.Equal(SoundCue.Done, _selector.Pick([Done("a")], Hidden, _settings, At(0)));
        Assert.Equal(SoundCue.Attention, _selector.Pick([Waiting("b")], Hidden, _settings, At(1)));
        Assert.Null(_selector.Pick([Waiting("c")], Hidden, _settings, At(1)));
    }
}

public class ToneSynthTests
{
    public static TheoryData<SoundStyle, SoundCue> Every()
    {
        var data = new TheoryData<SoundStyle, SoundCue>();
        foreach (var style in Enum.GetValues<SoundStyle>())
        {
            data.Add(style, SoundCue.Done);
            data.Add(style, SoundCue.Attention);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Every))]
    public void Every_sound_is_a_short_mono_16_bit_wav(SoundStyle style, SoundCue cue)
    {
        var wav = ToneSynth.Render(style, cue);

        Assert.Equal("RIFF", Encoding.ASCII.GetString(wav, 0, 4));
        Assert.Equal("WAVE", Encoding.ASCII.GetString(wav, 8, 4));
        Assert.Equal(wav.Length - 8, BitConverter.ToInt32(wav, 4));
        Assert.Equal(1, BitConverter.ToInt16(wav, 22));
        Assert.Equal(ToneSynth.SampleRate, BitConverter.ToInt32(wav, 24));
        Assert.Equal(16, BitConverter.ToInt16(wav, 34));
        Assert.Equal(wav.Length - 44, BitConverter.ToInt32(wav, 40));

        var seconds = (wav.Length - 44) / 2.0 / ToneSynth.SampleRate;
        Assert.InRange(seconds, 0.1, 0.6);
    }

    [Theory]
    [MemberData(nameof(Every))]
    public void Every_sound_is_audible_but_never_loud_or_clipped(SoundStyle style, SoundCue cue)
    {
        var wav = ToneSynth.Render(style, cue);
        var peak = 0;
        for (var i = 44; i < wav.Length; i += 2)
        {
            peak = Math.Max(peak, Math.Abs((int)BitConverter.ToInt16(wav, i)));
        }

        Assert.InRange(peak / (double)short.MaxValue, 0.05, 0.5);
    }

    [Theory]
    [MemberData(nameof(Every))]
    public void Every_sound_ends_in_silence_so_it_does_not_click(SoundStyle style, SoundCue cue)
    {
        var wav = ToneSynth.Render(style, cue);

        Assert.Equal(0, BitConverter.ToInt16(wav, wav.Length - 2));
    }

    [Fact]
    public void Done_and_attention_sound_different_in_every_style()
    {
        foreach (var style in Enum.GetValues<SoundStyle>())
        {
            Assert.NotEqual(ToneSynth.Render(style, SoundCue.Done), ToneSynth.Render(style, SoundCue.Attention));
        }
    }

    [Fact]
    public void Styles_sound_different_and_auto_falls_back_to_the_chime()
    {
        var chime = ToneSynth.Render(SoundStyle.Chime, SoundCue.Done);

        Assert.Equal(chime, ToneSynth.Render(SoundStyle.Auto, SoundCue.Done));
        Assert.NotEqual(chime, ToneSynth.Render(SoundStyle.Retro, SoundCue.Done));
        Assert.NotEqual(chime, ToneSynth.Render(SoundStyle.Bell, SoundCue.Done));
    }

    [Fact]
    public void An_unknown_style_in_a_hand_edited_settings_file_becomes_auto()
    {
        Assert.Equal(SoundStyle.Auto, (new PanelSettings() with { SoundStyle = (SoundStyle)42 }).Normalize().SoundStyle);
    }
}
