# CLAUDE.md

Context for AI agents working on TaskbarLed. Human docs: [README.md](README.md), [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md), [CONTRIBUTING.md](CONTRIBUTING.md). Why things are the way they are: [docs/REQUISITOS.md](docs/REQUISITOS.md) and [docs/PROPOSTAS.md](docs/PROPOSTAS.md) (Portuguese).

The maintainer speaks Brazilian Portuguese: talk to them in pt-BR. Code, comments, commit messages and the main README stay in English; keep README.pt-BR.md in sync with README.md.

## What it is

A WPF app (.NET 10, Windows 10/11) that draws a small terminal-style panel on the taskbar showing Claude Code sessions on Windows and several AI sources on macOS: tabs per session, a vertical typed-out feed, prompt timer. The default theme is `ClaudeCode` (Claude Code's terminal colors, a pulsing `✻`); `Matrix`, the original green LED look, is still the reference file for theme keys. It appears only while the app each session runs in (Claude desktop or VS Code) is out of sight; amber/gold blink = waiting for the user, green blink = done until that app is opened. Over borderless full screen it shows an edge glow and short alerts instead.

## Layout

| Project | Role | Rule |
|---|---|---|
| `src/TaskbarLed.Core` | Sessions, reducer, rotation, composer, alerts, layout math, settings model, IPC envelope | No Windows, no UI, no I/O. Everything here is unit-tested. |
| `src/TaskbarLed.Sources.ClaudeCode` | Hook payload → `SessionEvent`; chat title and interruptions from the transcript; hook installer | All Claude Code knowledge lives here. Never throw on unknown payloads. |
| `src/TaskbarLed.Sources.Octo`, `Sources.Codex`, `Sources.Antigravity` | Local polling integrations using Core’s `ISessionScanner` | All knowledge about agent X lives in `TaskbarLed.Sources.X`. |
| `src/TaskbarLed.Sources.Generic` | Provider-neutral `agent` hook | See [macOS guide](docs/MACOS.md#connect-agents) for payload fields. |
| `src/TaskbarLed.Platform.Windows` | Win32 (`LibraryImport`), visibility probe, monitors/taskbars, full-screen detection, auto-start, named-pipe server | |
| `src/TaskbarLed.Hook` | Exe the hooks call; forwards stdin to the pipe or a spool folder | Must never print, block or fail: always exit 0. |
| `src/TaskbarLed.App` | Composition root (`AppHost`), `PanelView`/`PanelWindow`, overlays, tray menu, themes, `DemoRenderer` | |
| `src/TaskbarLed.Platform.Mac` | CoreGraphics window list, `ClaudeWindowRules` (pure, tested), all-Spaces window, LaunchAgent, `~/Library` paths | Native calls only on macOS (`[SupportedOSPlatform("macos")]`). |
| `src/TaskbarLed.App.Avalonia` | macOS port: live Claude Code and orchestrator events in a floating panel centered above the Dock | Runs on Windows too, for development. Plan and next steps: `docs/PROPOSTAS.md` P4. |

## Commands

```powershell
$env:MSBUILDDISABLENODEREUSE=1                       # see "Gotchas"
dotnet build TaskbarLed.slnx -nodeReuse:false
dotnet test TaskbarLed.slnx --no-build
# Regenerate README screenshots after any visual change or new theme:
src/TaskbarLed.App/bin/Debug/net10.0-windows/TaskbarLed.exe --render-demo docs/images
# Update the copy the maintainer runs daily (it starts with Windows):
& "$env:LOCALAPPDATA\Programs\TaskbarLed\TaskbarLed.exe" --exit
dotnet publish src/TaskbarLed.App -c Release -o "$env:LOCALAPPDATA\Programs\TaskbarLed" -nodeReuse:false
Start-Process "$env:LOCALAPPDATA\Programs\TaskbarLed\TaskbarLed.exe"   # relaunches itself outside Claude's container
```

macOS port: `src/TaskbarLed.App.Avalonia/build-macos.sh` packages the live app and hook for arm64 or x64. See [the macOS guide](docs/MACOS.md). Use `--demo` to show sample data. The Avalonia tests cover the live spool and headless rendering.

Other CLI flags: `--install-hooks`, `--uninstall-hooks`, `--restart` (wait for the old instance; used by the language switch).

Release: push a tag `vX.Y.Z`; `.github/workflows/release.yml` builds, tests and attaches `TaskbarLed-win-x64.zip`, then a macOS job adds `TaskbarLed-osx-arm64` and `TaskbarLed-osx-x64` (`.zip` and `.pkg`) to the same release. Repo: github.com/llorenzojorge/TaskbarLed.

## Running a dev build safely

- **A dev build takes over the maintainer's install.** On a normal start (`dotnet run --project src/TaskbarLed.App`, or the Debug exe without flags), `AppHost.Start` repairs auto-start and hooks to point at *the exe that is running*: the `Run` value and every hook command in `~/.claude/settings.json` end up pointing into `bin/Debug`. After such a run, start the installed copy again (it repairs both back). `--render-demo`, `--install-hooks`, `--uninstall-hooks` and `--exit` return before that and are safe.
- **Single instance.** While the installed copy runs, a second start exits silently (mutex `Local\TaskbarLed.SingleInstance`). Run `--exit` on the installed copy first.
- **Feed it fake events** instead of waiting for real ones: pipe a hook payload into the hook exe. `session_id` and `hook_event_name` are required; `cwd` gives the tab name. Events: `SessionStart`, `UserPromptSubmit`, `PreToolUse`, `PostToolUse`, `Notification`, `Stop`, `SessionEnd`.

  ```powershell
  '{"session_id":"demo-1","hook_event_name":"UserPromptSubmit","cwd":"C:\\work\\webapp"}' | & src/TaskbarLed.Hook/bin/Debug/net10.0/TaskbarLed.Hook.exe claude-code
  '{"session_id":"demo-1","hook_event_name":"Stop","cwd":"C:\\work\\webapp","last_assistant_message":"Done."}' | & src/TaskbarLed.Hook/bin/Debug/net10.0/TaskbarLed.Hook.exe claude-code
  ```

  The panel only shows while the session's app is out of sight; a fake session has no captured environment, so it follows the Claude window.
- **`TaskbarLed.App` has no tests** (windows, tray, `AppHost` wiring). Put decisions in Core where they can be tested; check the rest with `--render-demo` (visuals) and fake events (behavior).
- **Where the agent runs matters.** A session in VS Code or a plain terminal is *not* inside the Claude desktop app's MSIX container: `%APPDATA%`, `%LOCALAPPDATA%` and `HKCU` are the real ones, and `Start-Process` works directly. The redirection gotcha below applies to sessions in the Claude desktop app.

## Status

- Released: `v1.3.1` (Windows x64, framework-dependent, unsigned).
- Versions: `MAJOR.MINOR.PATCH`. PATCH for fixes, MINOR for new features, and 2.0 only for a real new generation of the app. The tag is the version: the release workflow stamps it into the binaries and publishes a SHA-256 next to the zip. Local builds say `0.0.0-dev`. (v1.0.0 and v1.1.0 were built before this and say 1.0.0.)
- Supported: Windows 10 and 11. Tested by hand only on the maintainer's Windows 11; Windows 10, a taskbar on another edge and ARM64 are not verified.
- macOS: Preview tested on the contributor’s M2 with macOS 26.5.2, natively for arm64 and via Rosetta for x64. The maintainer has no Mac. See [the macOS guide](docs/MACOS.md) for requirements and differences.

## Product rules (read before changing what users see)

These are the maintainer's decisions about what TaskbarLed is. A pull request that breaks one is sent back, however good the code. When a change seems to need an exception, ask in the pull request first.

**The idea.** A small panel that tells you what your agent is doing *while its app is out of sight*, and gets out of the way when you look at the app. Most users run one agent at a time: the default experience is built for them.

**MUST NOT change by default** (a new behavior that touches one of these is an option, off by default):
- When the panel shows: only while the session's app (Claude desktop, VS Code) is out of sight and there is something to show. Opening the app hides it.
- The look: tabs (one per chat) plus the typed-out feed. Other layouts (a session list for sub-agents, a top bar) are a separate *panel style* the user picks, named by what it shows plus who it is for, for example "Tabs (default)" and "Session list (sub-agents)".
- Where it sits: in the taskbar, at the user's saved spot. No feature moves it to another placement on its own.
- The default theme (`ClaudeCode`), sound on, and the language the user picked.

**Interaction:**
- Clicks outside edit mode open things (a tab opens its session, the feed opens what finished). Moving and resizing happen **only in edit mode** (menu → Edit position and size / Edit position only), so a stray click never moves or resizes the panel.
- In edit mode the panel can be dragged anywhere; where it is dropped sets the placement (`PanelLayout.MoveTo`: on the taskbar, just above it, or free). Escape restores everything.
- A chat only opened and read, with no event for `IdleTabMinutes` (3), loses its tab until its next event (`PanelTabs.Shown`). Running, waiting and finished-but-unseen sessions always keep theirs.

**Both platforms:**
- Shared code (`Core`, `Sources.*`, the settings model, `src/TaskbarLed.App/Themes`) serves Windows and macOS. A Mac feature that needs a shared change keeps Windows' defaults identical, and the pull request says what Windows users see differently (ideally nothing) with `--render-demo` images.
- A platform-only feature lives in that platform's app project. Do not reach into the other app's folder (no file links from `TaskbarLed.App` into `TaskbarLed.App.Avalonia` or back); put shared files in a shared project.

**Themes:**
- One definition per theme for both platforms: values only, in the `Led.*` keys documented at the top of `Matrix.xaml` and one line in `ThemeCatalog`. Most themes change colors and typography, not the layout.
- Never branch on a theme id in code or XAML (`theme.Id == "LineMap"`). A theme that needs something different asks for it with a documented, optional key (for example a `Led.ShowRoutes` boolean or a `Led.RouteBrushes` list) that every other theme simply leaves out.
- No colors written in panel XAML or code: every color comes from a `Led.*` resource.

**Text and logic:**
- Every word a user reads comes from `TickerStrings` (Core), `ActivityStrings` (ClaudeCode), `UiStrings` (App) or `MacUiStrings` (Avalonia), in English and Portuguese. Never `culture == "pt" ? "…" : "…"` in a view model.
- Decisions (which sessions show, grouping, ordering, timers, countdowns, placement) go in Core as pure functions with sentence-named tests. App projects only wire them to windows.

**Every pull request that adds an input or a reader** (a new hook kind, a file or process the app reads) updates `SECURITY.md` in the same pull request: what is read, from where, and that it is only displayed.

## Security rules

The threat model and what users are promised are in [SECURITY.md](SECURITY.md); keep it true when behavior changes.

- **Payloads, spool files, settings and themes are untrusted input.** Check the JSON value kind before reading a value (`GetString()` on a number throws `InvalidOperationException`, which a `catch (JsonException)` misses). Nothing read at startup may throw out of `AppHost.Start`: one bad spool file would stop the panel from starting, every time.
- **The hook checks who owns the pipe before writing** (`IsOurPanel`): pipe names are machine-wide and another account could create ours first. Do not swap it for `PipeOptions.CurrentUserOnly`: that compares against the token's default owner, which on the maintainer's machine is Administrators for the panel and the user for the hook, so every event went to the spool. After touching the hook or the server, send fake events and check that the spool folder stays empty.
- **Only open paths the app chose, or validated ones.** Transcript paths come from payloads: `TranscriptTail.IsLocalTranscript` refuses network and non-`.jsonl` paths (a UNC path would send the user's Windows credentials to that server).
- **Start processes by full path** (`explorer.exe` from the Windows folder). A bare name is also searched in the current directory, which can be a project an agent is working in.
- **The hook command is run by a shell.** `ClaudeCodeHookInstaller` refuses an install path containing `"`, `$` or a backtick.
- **No network, no credentials, no payload contents in the log.**

## Conventions

- Warnings are errors with the .NET analyzers on. Fix them; suppress only with a written justification.
- Tests are named as sentences (`Approval_then_tool_result_resumes_running_without_restarting_the_timer`); CA1707 is off for tests only.
- Core produces natural-case text. Upper case is a theme choice (`ThemeInfo.Uppercase`), applied by `PanelViewModel.Cased`.
- Built-in themes are `Themes/*.xaml` with every `Led.*` key (documented at the top of `Matrix.xaml`) plus one line in `ThemeCatalog`. All colors are `DynamicResource`, so switching is live.
- Sounds are synthesized (`ToneSynth`, Core), never shipped as audio files: nothing to license, and the Mac port reuses them. Whether to play is `SoundCueSelector` (Core, tested); `AppHost` only asks it and hands the WAV to `WavePlayer`. Each built-in theme names its voice in `ThemeCatalog` (`ThemeInfo.Sound`); custom JSON themes use the chime. Sound is on by default (maintainer's decision, 2026-11-17) and plays over full screen, exclusive included.
- User themes are JSON files in `%APPDATA%\TaskbarLed\themes`, read by `CustomTheme.TryParse` (Core, tested) and turned into brushes by `ThemeCatalog.Build` on top of the default theme. They are reloaded whenever the menu opens. **Never load XAML (`XamlReader`) or font paths from user files**: XAML can instantiate arbitrary objects, and a `FontFamily` with a path or UNC location loads files from there. Keep the JSON values-only and validated. The example lives in both `Themes/ExampleTheme.cs` and `docs/themes/ocean.json`; keep them in sync.
- UI strings live in three records: `TickerStrings` (Core), `ActivityStrings` (ClaudeCode), `UiStrings` (App), each with English and Portuguese.
- Settings: `%APPDATA%\TaskbarLed\settings.json`. Log: `%LOCALAPPDATA%\TaskbarLed\taskbarled.log`. Check the log first when something misbehaves.
- Demo data uses generic project names (`webapp`, `api`, `site`); never put the maintainer's real project names in public files.

## Gotchas learned the hard way

- **Never block the UI thread on async work during shutdown.** `AppHost.Dispose` is synchronous on purpose; a `GetAwaiter().GetResult()` there deadlocked Exit and the language switch. Menu actions that exit go through `ShutdownAfterMenu` so the WinForms menu finishes closing first.
- **Stale MSBuild nodes can hang builds** after an interrupted command. Build with `-nodeReuse:false` / `MSBUILDDISABLENODEREUSE=1`.
- **The installed app locks its files.** Run `--exit` before `dotnet publish` to the install folder.
- **`DropShadowEffect` ignores the alpha of its color.** Glow strength comes from `Led.GlowOpacity`, not from a transparent `Led.GlowColor`.
- **An empty `<sys:String></sys:String>` crashes XAML loading** (NullReferenceException in the binder). For an empty string resource such as `Led.FeedPrefix`, use `<x:Static Member="sys:String.Empty" />`.
- **A fully transparent WPF window background is not clickable.** The Glass theme uses `#01000000`.
- **The hook drops payloads over 2 MiB.** A dropped `PostToolUse` can leave a tool showing as running until the next event.
- **Hooks stdin is UTF-8.** Read raw bytes; the hook also strips a BOM and converts UTF-16 so scripts can call it.
- **The Claude desktop app runs hooks but not the status line**, so plan limits (5 h / weekly) are not available; `IUsageLimitProvider` is the reserved seam (must be opt-in if it ever reads credentials).
- **The maintainer's `~/.claude/settings.json` has TaskbarLed's hooks installed.** Never overwrite it wholesale; use `ClaudeCodeHookInstaller`, which keeps user hooks and writes a backup.
- **The Claude desktop app is an MSIX package, and everything a Claude Code session starts runs inside its container**, even though those processes report no package identity. Writes to `%APPDATA%`, `%LOCALAPPDATA%` and `HKCU` are redirected to `%LOCALAPPDATA%\Packages\Claude_*\LocalCache` and a private hive, where Windows never looks (the auto-start `Run` value was lost this way). The app guards against it: `PackageContext.IsRedirected` probes with a file and, if redirected, relaunches through `explorer.exe`, which starts it outside. Hooks run by Claude Code are inside it too, so `HookEventServer` also drains `Packages\*\LocalCache\Local\TaskbarLed\spool`. Your shell is redirected as well: check the real state with WMI (`StdRegProv`, `CIM_DataFile`).
- **Claude Code runs no hook when the user interrupts a prompt** (Esc or the stop button). Without a check, the session stays "running" forever. `TranscriptInterruptionReader` looks for the `[Request interrupted by user` marker in quiet sessions' transcripts.
- **"Claude is in sight" depends on the session's app.** A VS Code session is not seen by opening the Claude app. The hook copies `CLAUDE_CODE_ENTRYPOINT`/`TERM_PROGRAM` into the envelope (`HookEnvelope.CapturedVariables`; identifiers only, never secrets). Sessions in the browser (claude.ai/code) run in the cloud, and their hooks never reach this machine.
- **Windows 11 centers the taskbar icons, so the Start button moves left as apps open** and slid under a panel saved next to it. Taskbar mode never covers a taskbar button: `TaskbarButtonWatcher` (Platform.Windows) reads the buttons' rectangles through UI Automation on a background timer (Windows 11 buttons are XAML, with no HWND), and `PanelLayout.AvoidItems` (Core, tested) shrinks or moves the panel with a 4 DIP clearance. The blinking halo is drawn inside the panel window, so it obeys the same rule. Only what is shown is trimmed; the saved position stays, so the panel grows back when apps close. On Windows 10 the watcher finds nothing and the panel keeps its saved spot.
- **Tabs are named from the transcript, not the folder.** The Claude app's chats all run in folders like `scratch-2026-11-21-53192f`, so the folder name tells them apart for nobody. `TranscriptInfoReader` reads `custom-title` (user rename, wins) or `ai-title` from the transcript tail. Tabs share the row through `TabStripPanel` + `TabStripLayout` (Core, tested): whole titles when they fit, the longest trimmed first when not. **A session first seen mid-prompt** (panel started with Windows, restarted, updated) catches up from its transcript on its first event: title, folder and the real prompt start (latest human `user` line), so the timer does not count from the restart.
- On Git Bash, multi-line `sed` inserts with `\` continuations collapse lines; prefer the Edit tool for code changes.
