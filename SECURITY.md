# Security

## Reporting a vulnerability

Please do not open a public issue for a security problem. Use **[Report a vulnerability](https://github.com/llorenzojorge/TaskbarLed/security/advisories/new)** on GitHub, which opens a private report only the maintainer can read.

Say what you found, how to reproduce it and which version you used. This is a one-person project maintained in spare time: expect a first answer within about a week. Fixes go into the next release, and the release notes credit you unless you prefer otherwise.

Only the latest release gets security fixes.

## What TaskbarLed touches

TaskbarLed runs as you, without administrator rights, and makes no network requests. It does not update itself.

| It reads | Why |
|---|---|
| Hook events from Claude Code: prompt titles, tool names and inputs, the last reply | To show what each session is doing |
| The end of each session's transcript (`.jsonl` under `~/.claude`) | Context usage, and to notice a prompt you interrupted |
| Titles and positions of windows on screen | To know whether Claude or VS Code is in sight |
| JSON theme files in `%APPDATA%\TaskbarLed\themes` | Your own themes |

| It writes | Why |
|---|---|
| `~/.claude/settings.json` (after asking) | Adds its hooks. Your own hooks and settings are kept, and a backup is saved next to the file |
| `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` | Start with Windows; can be turned off in the menu |
| `%APPDATA%\TaskbarLed` | Settings and themes |
| `%LOCALAPPDATA%\TaskbarLed` | Log and spool (see below) |

**Events at rest.** While the panel is closed, the hook saves events in `%LOCALAPPDATA%\TaskbarLed\spool` so the panel can catch up when it starts. These files hold the same content Claude Code already keeps in its transcripts, including commands and file contents passed to tools. At most 500 are kept; they are deleted when the panel next starts. The log holds errors and theme problems, never event contents.

## What it protects against

- **Other accounts on the same computer.** Events travel over a named pipe that only your Windows user may open. The hook also checks who created the pipe before writing to it, so another account cannot set up a look-alike pipe and collect your events.
- **Theme files from the internet.** A theme is values only: colors, numbers, font names and a short glyph, each validated. TaskbarLed never loads XAML or code from a theme, and a font must be a name, not a path or network location.
- **Malformed input.** Unknown or broken events, spool files and settings are skipped. A hook can never print, block or fail, so it cannot disturb Claude Code.
- **Paths in events.** Only local `.jsonl` transcripts are opened; network paths are refused.

## What it does not protect against

- **Programs running as you.** Anything already running under your account can read your files and Claude Code's transcripts and edit `~/.claude/settings.json` directly. TaskbarLed adds nothing to that and cannot prevent it.
- **Administrators** of the computer.
- **A tampered download.** Releases are not code-signed yet, so Windows SmartScreen warns on first run. Download only from the [releases page](https://github.com/llorenzojorge/TaskbarLed/releases) and compare the zip with the published SHA-256:

  ```powershell
  Get-FileHash TaskbarLed-win-x64.zip -Algorithm SHA256
  ```

Do not run TaskbarLed as administrator: it does not need it.
