# Architecture

TaskbarLed is split so that almost all behavior is plain, testable C# and only a thin layer touches Windows or WPF.

```
src/
  TaskbarLed.Core               Domain and presentation logic. No Windows, no UI, no I/O.
  TaskbarLed.Sources.ClaudeCode Claude Code hook payloads → session events; hook installer.
  TaskbarLed.Platform.Windows   Win32: windows, monitors, taskbar, full screen, auto-start, IPC server.
  TaskbarLed.Hook               The executable the hooks call. Forwards stdin to the app.
  TaskbarLed.App                WPF composition root: panel windows, theme, tray, settings.
  TaskbarLed.Platform.Mac       macOS: Claude window visibility, all-Spaces window, LaunchAgent, data folders.
  TaskbarLed.App.Avalonia       macOS port (work in progress): floating panel above the Dock, demo feed for now.
tests/
  TaskbarLed.Core.Tests
  TaskbarLed.Sources.ClaudeCode.Tests
  TaskbarLed.Platform.Mac.Tests
  TaskbarLed.App.Avalonia.Tests Headless render of the floating panel (xunit v3); saves mac-floating-panel.png.
```

Dependencies only point inward: `App → Platform.Windows, Sources.* → Core` and `App.Avalonia → Platform.Mac → Core`. `Hook → Core` (for the shared IPC contract only). The macOS plan and its remaining steps are in [PROPOSTAS.md](PROPOSTAS.md) (P4).

## Data flow

1. **Hook.** Claude Code runs `TaskbarLed.Hook.exe claude-code` and writes the hook JSON to its stdin. The hook wraps it in a `HookEnvelope` (`source`, `receivedAt`, raw `payload`) and sends it over a named pipe restricted to the current user. If the app is not running, the hook writes the envelope to a spool folder instead. The hook never prints and always exits 0, so it can never break the agent.
2. **Server.** `HookEventServer` (Platform.Windows) accepts envelopes from the pipe and replays the spool on start.
3. **Translation.** `EventPump` routes each envelope to the `ISessionSource` whose `SourceId` matches. `ClaudeCodeSource` turns hook events into `SessionEvent`s and reads context usage from the tail of the transcript.
4. **State.** `SessionStore` applies events through `SessionReducer`, a pure `(Session, Event) → Session` function.
5. **Presentation.** Once per second, or right after a change, `PanelComposer` builds a `PanelFrame`: one tab per session, the feed line, timer and context. `SessionRotation` decides which session the feed shows. The view pushes a new feed line, sliding down and typing itself out, only when the feed text actually changes.
6. **Visibility.** Every 400 ms, `ClaudeVisibilityProbe` walks the window z-order. `OcclusionCalculator` then estimates how much of the Claude window, and of each VS Code window, is visible. Each session knows its app (`SessionHost`, from the `CLAUDE_CODE_ENTRYPOINT` and `TERM_PROGRAM` variables the hook copies into the envelope), and `SessionVisibility` checks that app's window: for VS Code, the window whose title names the project folder. The panels show while any active session's app is out of sight, except on monitors running something full screen. Sessions are also checked every 2 s for a prompt the user interrupted, which runs no hook (`TranscriptInterruptionReader`).
7. **View.** `PanelManager` keeps one `PanelWindow` per target monitor. It places the window with `PanelLayout` for the chosen placement (on the taskbar, above it, or free), in physical pixels and DPI-aware.
8. **Full screen.** `FullscreenDetector` tells *borderless* full screen (video in a browser, which can be drawn over) from *exclusive* full screen (games, which cannot). For borderless, `OverlayManager` shows an `EdgeGlowWindow` while something needs a look. `AlertDetector` emits a one-off `PanelAlert` when a known session finishes or starts waiting, and `AlertWindow` shows it for 4 seconds. Alternatively, the panel itself shrinks into a click-through corner mini panel.
9. **Sound.** The same `PanelAlert`s feed `SoundCueSelector` (Core), which decides whether to play and which cue: only for a session whose app is out of sight, "waiting" over "done", bursts merged. `ToneSynth` (Core) renders the cue as a WAV in memory, in the voice of the theme or the one the user picked, and `WavePlayer` (Platform.Windows) plays it. No audio files.

## Extension points

| You want to… | Implement / edit | Notes |
|---|---|---|
| Support another agent CLI | `ISessionSource` in a new `TaskbarLed.Sources.<Name>` project | Point that tool's hooks at `TaskbarLed.Hook.exe <your-source-id>`. Register the source in `AppHost`. Never throw on unknown payloads. |
| Use your own colors | A JSON file in `%APPDATA%\TaskbarLed\themes` | No build. Values only, validated by `CustomTheme` (Core). See the README. |
| Add a built-in theme | Copy `src/TaskbarLed.App/Themes/Matrix.xaml`, add it to `ThemeCatalog` | Keep every `Led.*` key. Preview with `--render-demo`, which renders the real `PanelView` to PNG (`DemoRenderer`). |
| Translate | `TickerStrings`, `ActivityStrings`, `UiStrings` | One factory per language, chosen by the UI culture. |
| Show plan limits | `IUsageLimitProvider` | Must be opt-in if it reads credentials. |
| Add a sound voice | `SoundStyle` + a `Voice` and notes in `ToneSynth`, a label in `UiStrings`, an item in `TrayIcon.SoundMenu` | Keep it under half a second and quiet; the tests check both. |
| Change when the panel hides | `IClaudeVisibilityProbe`, `VisibleThreshold` | |

## Design decisions

- **Raw payload forwarding.** The hook does not parse anything, so the protocol between Claude Code and TaskbarLed lives in one place (`ClaudeCodeSource`) and is fully unit-tested.
- **Pipe plus spool.** The pipe gives instant updates. The spool means a panel started mid-prompt still knows what happened.
- **Sampling instead of region math.** For occlusion, a 24×24 grid is allocation-free and accurate to about 0.2%, which is plenty to tell "covered" from "snapped side by side".
- **Pure DI.** The object graph is small. Explicit wiring in `AppHost` keeps startup fast and the flow readable.
- **Update channels in place.** `PanelViewModel` mutates existing channel view models, so blink animations do not restart every second.
- **Physical pixels for placement.** The app is per-monitor DPI aware (`app.manifest`). Settings are stored in DIP and converted per monitor.
