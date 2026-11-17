using System.Runtime.InteropServices;
using static TaskbarLed.Platform.Windows.Native.NativeMethods;

namespace TaskbarLed.Platform.Windows.Audio;

/// <summary>
/// Plays a short WAV held in memory through the Windows mixer, without blocking. It follows the system
/// volume and plays over whatever else is sounding. A new sound replaces one still playing.
/// </summary>
public sealed class WavePlayer : IDisposable
{
    // Windows reads the sound while it plays, so the bytes live in unmanaged memory until the next sound or Dispose.
    private nint _playing;

    /// <param name="wav">A complete WAV file.</param>
    /// <returns>False when Windows could not play it (no audio device, for example).</returns>
    public bool Play(byte[] wav)
    {
        ArgumentNullException.ThrowIfNull(wav);

        Stop();
        _playing = Marshal.AllocHGlobal(wav.Length);
        Marshal.Copy(wav, 0, _playing, wav.Length);
        if (PlaySound(_playing, 0, SND_MEMORY | SND_ASYNC | SND_NODEFAULT))
        {
            return true;
        }

        Stop();
        return false;
    }

    public void Dispose() => Stop();

    private void Stop()
    {
        if (_playing == 0)
        {
            return;
        }

        PlaySound(0, 0, 0); // Stops the sound, so Windows no longer reads the buffer.
        Marshal.FreeHGlobal(_playing);
        _playing = 0;
    }
}
