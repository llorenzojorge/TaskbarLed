using TaskbarLed.Core.Presentation;
using TaskbarLed.Core.Sessions;
using TaskbarLed.Core.Settings;

namespace TaskbarLed.Core.Audio;

/// <summary>
/// Decides whether the alerts of one frame deserve a sound, and which. A sound is for a session whose
/// app the user cannot see: looking at Claude is already knowing. Several sessions changing together
/// make one sound, and "waiting for you" wins over "done".
/// </summary>
public sealed class SoundCueSelector
{
    /// <summary>Sounds closer together than this are merged, so a burst of sessions does not rattle.</summary>
    public static readonly TimeSpan MinimumGap = TimeSpan.FromSeconds(1.5);

    private DateTimeOffset _lastAt = DateTimeOffset.MinValue;
    private SoundCue _lastCue;

    /// <param name="alerts">What <see cref="AlertDetector"/> reported for this frame.</param>
    /// <param name="isOutOfSight">Whether the app of the session with this id is hidden from the user.</param>
    /// <returns>The sound to play now, or null for none.</returns>
    public SoundCue? Pick(
        IReadOnlyList<PanelAlert> alerts,
        Func<string, bool> isOutOfSight,
        PanelSettings settings,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(alerts);
        ArgumentNullException.ThrowIfNull(isOutOfSight);
        ArgumentNullException.ThrowIfNull(settings);

        SoundCue? cue = null;
        foreach (var alert in alerts)
        {
            if (alert.Status == SessionStatus.AwaitingInput && settings.SoundOnAttention && isOutOfSight(alert.SessionId))
            {
                cue = SoundCue.Attention;
                break;
            }

            if (alert.Status == SessionStatus.Completed && settings.SoundOnDone && isOutOfSight(alert.SessionId))
            {
                cue = SoundCue.Done;
            }
        }

        if (cue is not { } chosen)
        {
            return null;
        }

        // A request for the user right after a "done" is still played: it is the one that must not be missed.
        var tooSoon = now - _lastAt < MinimumGap && now >= _lastAt;
        if (tooSoon && !(chosen == SoundCue.Attention && _lastCue == SoundCue.Done))
        {
            return null;
        }

        _lastAt = now;
        _lastCue = chosen;
        return chosen;
    }
}
