namespace TaskbarLed.Core.Sessions;

/// <summary>
/// The app the user talks to the agent through. The panel hides only while the window of
/// <em>that</em> app is in sight: a session running in VS Code is not "seen" by opening the Claude app.
/// </summary>
public enum SessionHost
{
    /// <summary>Not reported (older hook, other source). Treated like the Claude desktop app.</summary>
    Unknown = 0,

    /// <summary>The Claude desktop app.</summary>
    ClaudeDesktop = 1,

    /// <summary>VS Code or a fork of it (Cursor, Windsurf...), through the extension or its terminal.</summary>
    VsCode = 2,

    /// <summary>A standalone terminal. Which window is not known, so it is treated like the desktop app.</summary>
    Terminal = 3,
}
