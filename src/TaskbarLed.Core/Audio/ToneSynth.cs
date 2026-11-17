using TaskbarLed.Core.Settings;

namespace TaskbarLed.Core.Audio;

/// <summary>What a sound says.</summary>
public enum SoundCue
{
    /// <summary>A session finished.</summary>
    Done = 0,

    /// <summary>A session is waiting for the user.</summary>
    Attention = 1,
}

/// <summary>
/// Makes the notification sounds from arithmetic: no audio files to license or ship, and the same sound
/// on every platform. Each is a fraction of a second. "Done" is two rising notes; "attention" is the
/// same note twice, so the two are told apart without looking.
/// </summary>
public static class ToneSynth
{
    public const int SampleRate = 44100;

    private const double Silence = 0.02;
    private const double FadeOut = 0.010;

    private readonly record struct Note(double Frequency, double Start, double Length);

    /// <param name="Partials">Multiples of the note's frequency and how loud each one is.</param>
    /// <param name="Attack">Seconds to reach full volume (a hard start clicks).</param>
    /// <param name="Decay">Seconds for the note to fall to about a third; large for a flat, beep-like note.</param>
    /// <param name="Gain">Overall volume, 0..1. Kept low: this plays over whatever the user is listening to.</param>
    private sealed record Voice((double Multiple, double Level)[] Partials, double Attack, double Decay, double Gain);

    // Soft and warm: a sine with a little second and third harmonic that rings out.
    private static readonly Voice Chime = new([(1, 1.0), (2, 0.25), (3, 0.08)], Attack: 0.006, Decay: 0.09, Gain: 0.30);

    // Square-ish beep of an old terminal: odd harmonics, flat volume.
    private static readonly Voice Retro = new([(1, 1.0), (3, 1 / 3d), (5, 1 / 5d), (7, 1 / 7d)], Attack: 0.004, Decay: 10, Gain: 0.10);

    // Small glass bell: partials that are not whole multiples, longer ring.
    private static readonly Voice Bell = new([(1, 1.0), (2.76, 0.35), (5.4, 0.15)], Attack: 0.003, Decay: 0.14, Gain: 0.20);

    /// <summary>A complete WAV file (16-bit mono PCM) for <paramref name="cue"/> in <paramref name="style"/>.</summary>
    public static byte[] Render(SoundStyle style, SoundCue cue)
    {
        var (voice, notes) = (style, cue) switch
        {
            (SoundStyle.Retro, SoundCue.Done) => (Retro, new Note[] { new(987.77, 0, 0.07), new(1318.51, 0.075, 0.11) }),
            (SoundStyle.Retro, _) => (Retro, [new(1174.66, 0, 0.055), new(1174.66, 0.11, 0.055)]),
            (SoundStyle.Bell, SoundCue.Done) => (Bell, [new(1046.50, 0, 0.30), new(1318.51, 0.09, 0.42)]),
            (SoundStyle.Bell, _) => (Bell, [new(1174.66, 0, 0.20), new(1174.66, 0.14, 0.30)]),
            (_, SoundCue.Done) => (Chime, [new(783.99, 0, 0.22), new(1046.50, 0.085, 0.30)]),
            _ => (Chime, [new(987.77, 0, 0.14), new(987.77, 0.13, 0.22)]),
        };

        var seconds = notes.Max(n => n.Start + n.Length) + Silence;
        var samples = new double[(int)(seconds * SampleRate)];
        foreach (var note in notes)
        {
            Add(samples, note, voice);
        }

        return ToWav(samples);
    }

    private static void Add(double[] samples, Note note, Voice voice)
    {
        var first = (int)(note.Start * SampleRate);
        var count = Math.Min((int)(note.Length * SampleRate), samples.Length - first);
        for (var i = 0; i < count; i++)
        {
            var t = i / (double)SampleRate;
            var envelope = Math.Min(1, t / voice.Attack)
                * Math.Exp(-t / voice.Decay)
                * Math.Min(1, (note.Length - t) / FadeOut);

            var wave = 0d;
            foreach (var (multiple, level) in voice.Partials)
            {
                wave += level * Math.Sin(2 * Math.PI * note.Frequency * multiple * t);
            }

            samples[first + i] += voice.Gain * envelope * wave;
        }
    }

    private static byte[] ToWav(double[] samples)
    {
        const int HeaderBytes = 44;
        var dataBytes = samples.Length * 2;
        var wav = new byte[HeaderBytes + dataBytes];
        using var writer = new BinaryWriter(new MemoryStream(wav));

        writer.Write("RIFF"u8);
        writer.Write(36 + dataBytes);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);              // Size of the format block.
        writer.Write((short)1);        // PCM.
        writer.Write((short)1);        // Mono.
        writer.Write(SampleRate);
        writer.Write(SampleRate * 2);  // Bytes per second.
        writer.Write((short)2);        // Bytes per sample.
        writer.Write((short)16);       // Bits per sample.
        writer.Write("data"u8);
        writer.Write(dataBytes);
        foreach (var sample in samples)
        {
            writer.Write((short)(Math.Clamp(sample, -1, 1) * short.MaxValue));
        }

        return wav;
    }
}
