using System.Globalization;
using System.Text;
using TaskbarLed.Core.Sessions;

namespace TaskbarLed.Core.Presentation;

/// <summary>Turns session snapshots into a <see cref="PanelFrame"/>. Pure apart from the rotation state.</summary>
public sealed class PanelComposer
{
    private const string Separator = "  ›  ";
    private const int MaxMessageLength = 140;
    // Tabs trim themselves to the room they get (TabStripLayout); this only caps absurdly long titles.
    private const int MaxTabNameLength = 40;

    private readonly SessionRotation _rotation;
    private readonly TickerStrings _strings;
    private readonly CultureInfo _culture;

    public PanelComposer(SessionRotation rotation, TickerStrings strings, CultureInfo culture)
    {
        _rotation = rotation ?? throw new ArgumentNullException(nameof(rotation));
        _strings = strings ?? throw new ArgumentNullException(nameof(strings));
        _culture = culture ?? throw new ArgumentNullException(nameof(culture));
    }

    /// <summary>The rotation driving the feed (e.g. to skip to the next session).</summary>
    public SessionRotation Rotation => _rotation;

    public PanelFrame Compose(IReadOnlyList<Session> sessions, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(sessions);

        var selected = _rotation.Select(sessions, now);
        if (selected is null)
        {
            return PanelFrame.Empty(_strings.Idle);
        }

        var channels = sessions
            .Select((s, i) => new ChannelState(s.Id, TabLabel(i + 1, s), s.Status, s.Id == selected.Id))
            .ToList();

        var elapsed = selected.Elapsed(now);

        return new PanelFrame(
            channels,
            FeedLine(selected, elapsed),
            elapsed is { } e ? DurationFormat.Clock(e) : string.Empty,
            selected.Status,
            selected.Id,
            HasUnseenCompletion: sessions.Any(s => s.Status == SessionStatus.Completed),
            NeedsAttention: sessions.Any(s => s.Status == SessionStatus.AwaitingInput),
            HasActivity: sessions.Any(s => s.Status != SessionStatus.Idle));
    }

    private static string TabLabel(int position, Session session) =>
        string.Create(CultureInfo.InvariantCulture, $"{position} {Clean(session.DisplayName, MaxTabNameLength)}");

    // The tab names the chat (its whole title when there is room), so the feed only says what is happening.
    private string FeedLine(Session session, TimeSpan? elapsed)
    {
        var builder = new StringBuilder();
        switch (session.Status)
        {
            case SessionStatus.AwaitingInput:
                builder.Append(_strings.WaitingForYou);
                AppendIfAny(builder, session.Activity);
                break;

            case SessionStatus.Completed:
                builder.Append(elapsed is { } e
                    ? string.Format(_culture, _strings.DoneIn, DurationFormat.Human(e))
                    : _strings.Done);
                AppendIfAny(builder, session.LastMessage);
                break;

            case SessionStatus.Running:
                var activity = Clean(session.Activity, MaxMessageLength);
                builder.Append(activity.Length > 0 ? activity : _strings.Thinking);
                break;

            case SessionStatus.Idle:
            default:
                var last = Clean(session.LastMessage, MaxMessageLength);
                builder.Append(last.Length > 0 ? last : _strings.Idle);
                break;
        }

        return builder.ToString();
    }

    private static void AppendIfAny(StringBuilder builder, string? text)
    {
        var cleaned = Clean(text, MaxMessageLength);
        if (cleaned.Length > 0)
        {
            builder.Append(Separator).Append(cleaned);
        }
    }

    /// <summary>Flattens markdown-ish multi-line text into one short display line.</summary>
    internal static string Clean(string? text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(Math.Min(text.Length, maxLength + 1));
        var lastWasSpace = false;
        foreach (var ch in text)
        {
            // Markdown emphasis/code/heading markers; keep '_' (file names) and '>' (arrows).
            if (ch is '*' or '`' or '#')
            {
                continue;
            }

            if (char.IsWhiteSpace(ch))
            {
                if (!lastWasSpace && builder.Length > 0)
                {
                    builder.Append(' ');
                }

                lastWasSpace = true;
                continue;
            }

            builder.Append(ch);
            lastWasSpace = false;
            if (builder.Length > maxLength)
            {
                break;
            }
        }

        var result = builder.ToString().TrimEnd();
        return result.Length > maxLength ? string.Concat(result.AsSpan(0, maxLength - 1).TrimEnd(), "…") : result;
    }
}
