# CLAUDE.md

Context for AI agents working on TaskbarLed. Human docs: [README.md](README.md), [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md), [CONTRIBUTING.md](CONTRIBUTING.md). Why things are the way they are: [docs/REQUISITOS.md](docs/REQUISITOS.md) and [docs/PROPOSTAS.md](docs/PROPOSTAS.md) (Portuguese).

The maintainer speaks Brazilian Portuguese: talk to them in pt-BR. Code, comments, commit messages and the main README stay in English; keep README.pt-BR.md in sync with README.md.

## What it is

A WPF app (.NET 10, Windows 10/11) that draws an LED panel on the taskbar showing Claude Code sessions: tabs per session, a vertical typed-out feed, timer, context usage. It appears only while the Claude desktop window is out of sight; amber blink = waiting for the user, green blink = done until Claude is opened. Over borderless full screen it shows an edge glow and short alerts instead.

## Layout

| Project | Role | Rule |
|---|---|---|
| `src/TaskbarLed.Core` | Sessions, reducer, rotation, composer, alerts, layout math, settings model, IPC envelope | No Windows, no UI, no I/O. Everything here is unit-tested. |
| `src/TaskbarLed.Sources.ClaudeCode` | Hook payload → `SessionEvent`; transcript context usage; hook installer | All Claude Code knowledge lives here. Never throw on unknown payloads. |
| `src/TaskbarLed.Platform.Windows` | Win32 (`LibraryImport`), visibility probe, monitors/taskbars, full-screen detection, auto-start, named-pipe server | |
| `src/TaskbarLed.Hook` | Exe the hooks call; forwards stdin to the pipe or a spool folder | Must never print, block or fail: always exit 0. |
| `src/TaskbarLed.App` | Composition root (`AppHost`), `PanelView`/`PanelWindow`, overlays, tray menu, themes, `DemoRenderer` | |
| `src/TaskbarLed.Platform.Mac` | CoreGraphics window list, `ClaudeWindowRules` (pure, tested), all-Spaces window, LaunchAgent, `~/Library` paths | Native calls only on macOS (`[SupportedOSPlatform("macos")]`). |
| `src/TaskbarLed.App.Avalonia` | macOS port, in progress: `FloatingPanelWindow` centered above the Dock (`PanelLayout.AboveDock`), demo feed only | Runs on Windows too, for development. Plan and next steps: `docs/PROPOSTAS.md` P4. |

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

macOS port: `dotnet run --project src/TaskbarLed.App.Avalonia` shows the floating panel with demo data (on Windows it stays above the taskbar). `tests/TaskbarLed.App.Avalonia.Tests` renders it headless to `bin/.../screenshots/mac-floating-panel.png`. There is no Mac here: CI's `macos` job is the check, and Dock, Spaces, Claude detection and the LaunchAgent still need a real Mac.

Other CLI flags: `--install-hooks`, `--uninstall-hooks`, `--restart` (wait for the old instance; used by the language switch).

Release: push a tag `vX.Y.Z`; `.github/workflows/release.yml` builds, tests and attaches `TaskbarLed-win-x64.zip`. Repo: github.com/llorenzojorge/TaskbarLed.

## Conventions

- Warnings are errors with the .NET analyzers on. Fix them; suppress only with a written justification.
- Tests are named as sentences (`Approval_then_tool_result_resumes_running_without_restarting_the_timer`); CA1707 is off for tests only.
- Core produces natural-case text. Upper case is a theme choice (`ThemeInfo.Uppercase`), applied by `PanelViewModel.Cased`.
- Themes are `Themes/*.xaml` with every `Led.*` key (documented at the top of `Matrix.xaml`) plus one line in `ThemeCatalog`. All colors are `DynamicResource`, so switching is live.
- UI strings live in three records: `TickerStrings` (Core), `ActivityStrings` (ClaudeCode), `UiStrings` (App), each with English and Portuguese.
- Settings: `%APPDATA%\TaskbarLed\settings.json`. Log: `%LOCALAPPDATA%\TaskbarLed\taskbarled.log`. Check the log first when something misbehaves.
- Demo data uses generic project names (`webapp`, `api`, `site`); never put the maintainer's real project names in public files.

## Gotchas learned the hard way

- **Never block the UI thread on async work during shutdown.** `AppHost.Dispose` is synchronous on purpose; a `GetAwaiter().GetResult()` there deadlocked Exit and the language switch. Menu actions that exit go through `ShutdownAfterMenu` so the WinForms menu finishes closing first.
- **Stale MSBuild nodes can hang builds** after an interrupted command. Build with `-nodeReuse:false` / `MSBUILDDISABLENODEREUSE=1`.
- **The installed app locks its files.** Run `--exit` before `dotnet publish` to the install folder.
- **`DropShadowEffect` ignores the alpha of its color.** Glow strength comes from `Led.GlowOpacity`, not from a transparent `Led.GlowColor`.
- **A fully transparent WPF window background is not clickable.** The Glass theme uses `#01000000`.
- **Hooks stdin is UTF-8.** Read raw bytes; the hook also strips a BOM and converts UTF-16 so scripts can call it.
- **The Claude desktop app runs hooks but not the status line**, so plan limits (5 h / weekly) are not available; `IUsageLimitProvider` is the reserved seam (must be opt-in if it ever reads credentials).
- **The maintainer's `~/.claude/settings.json` has TaskbarLed's hooks installed.** Never overwrite it wholesale; use `ClaudeCodeHookInstaller`, which keeps user hooks and writes a backup.
- **The Claude desktop app is an MSIX package, and everything a Claude Code session starts runs inside its container**, even though those processes report no package identity. Writes to `%APPDATA%`, `%LOCALAPPDATA%` and `HKCU` are redirected to `%LOCALAPPDATA%\Packages\Claude_*\LocalCache` and a private hive, where Windows never looks (the auto-start `Run` value was lost this way). The app guards against it: `PackageContext.IsRedirected` probes with a file and, if redirected, relaunches through `explorer.exe`, which starts it outside. Hooks run by Claude Code are inside it too, so `HookEventServer` also drains `Packages\*\LocalCache\Local\TaskbarLed\spool`. Your shell is redirected as well: check the real state with WMI (`StdRegProv`, `CIM_DataFile`).
- **Claude Code runs no hook when the user interrupts a prompt** (Esc or the stop button). Without a check, the session stays "running" forever. `TranscriptInterruptionReader` looks for the `[Request interrupted by user` marker in quiet sessions' transcripts.
- **"Claude is in sight" depends on the session's app.** A VS Code session is not seen by opening the Claude app. The hook copies `CLAUDE_CODE_ENTRYPOINT`/`TERM_PROGRAM` into the envelope (`HookEnvelope.CapturedVariables`; identifiers only, never secrets). Sessions in the browser (claude.ai/code) run in the cloud, and their hooks never reach this machine.
- On Git Bash, multi-line `sed` inserts with `\` continuations collapse lines; prefer the Edit tool for code changes.
