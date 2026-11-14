using TaskbarLed.Core.Sessions;

namespace TaskbarLed.Core.Visibility;

/// <summary>A VS Code (or fork) window and how much of it the user can see.</summary>
/// <param name="Title">Window title, e.g. <c>app.ts - webapp - Visual Studio Code</c>.</param>
/// <param name="VisibleFraction">Share (0..1) of the window that is uncovered and on screen.</param>
/// <param name="Handle">Native handle, for activation.</param>
public sealed record EditorWindow(string Title, double VisibleFraction, nint Handle);

/// <summary>What is on screen right now, for every app a session can run in.</summary>
/// <param name="ClaudeVisibleFraction">Share (0..1) of the Claude desktop window in sight (0 when closed).</param>
/// <param name="Editors">Every VS Code window, in sight or not.</param>
public sealed record HostWindows(double ClaudeVisibleFraction, IReadOnlyList<EditorWindow> Editors)
{
    public static HostWindows None { get; } = new(0, []);
}

/// <summary>
/// Decides, per session, whether the user is looking at the app it runs in. The panel exists to show
/// what the user cannot see, so it hides only when every active session's own app is in sight.
/// </summary>
public static class SessionVisibility
{
    private const string TitleSeparator = " - ";

    /// <summary>True when the window of the session's app is visible above <paramref name="threshold"/>.</summary>
    public static bool IsInSight(Session session, HostWindows windows, double threshold)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(windows);

        return session.Host == SessionHost.VsCode
            ? EditorFor(session, windows.Editors) is { } editor && editor.VisibleFraction >= threshold
            : windows.ClaudeVisibleFraction >= threshold; // Desktop app; also the fallback for unknown hosts and terminals.
    }

    /// <summary>
    /// True when at least one active (not idle) session's app is out of sight. With no active session
    /// the Claude desktop window decides, as it always has.
    /// </summary>
    public static bool AnyOutOfSight(IReadOnlyList<Session> sessions, HostWindows windows, double threshold)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(windows);

        var active = sessions.Where(s => s.Status != SessionStatus.Idle).ToList();
        return active.Count == 0
            ? windows.ClaudeVisibleFraction < threshold
            : active.Any(s => !IsInSight(s, windows, threshold));
    }

    /// <summary>
    /// The VS Code window of the session: the one whose title names the project folder
    /// (<c>file - folder - Visual Studio Code</c>), or, if no title does, the most visible one.
    /// </summary>
    public static EditorWindow? EditorFor(Session session, IReadOnlyList<EditorWindow> editors)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(editors);

        if (editors.Count == 0)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(session.ProjectName))
        {
            var named = editors
                .Where(e => NamesProject(e.Title, session.ProjectName))
                .MaxBy(e => e.VisibleFraction);
            if (named is not null)
            {
                return named;
            }
        }

        return editors.MaxBy(e => e.VisibleFraction);
    }

    private static bool NamesProject(string title, string project) =>
        title.Split(TitleSeparator, StringSplitOptions.TrimEntries)
            .Any(part => part.Equals(project, StringComparison.OrdinalIgnoreCase)
                || part.Equals($"{project} (Workspace)", StringComparison.OrdinalIgnoreCase));
}
