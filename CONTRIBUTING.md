# Contributing

Thanks for helping! Start with [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md), which maps the projects and the extension points.

## Build and test

```powershell
dotnet build TaskbarLed.slnx
dotnet test TaskbarLed.slnx
dotnet run --project src/TaskbarLed.App
```

The build treats warnings as errors and runs the .NET analyzers. Fix the warning rather than suppressing it. If a suppression is truly right, add a justification next to it.

## Create a theme

Just want your own colors? You don't need to build anything: drop a JSON file in `%APPDATA%\TaskbarLed\themes` (see "Make your own theme" in the README). The steps below are for a theme that ships with TaskbarLed.

1. Copy `src/TaskbarLed.App/Themes/Matrix.xaml` to `Themes/YourTheme.xaml`. The comment at the top explains every key.
2. Change the colors. Keep every `Led.*` key, and make `Led.AttentionBrush` clearly different from `Led.ForegroundBrush`.
3. Add one line to `Themes/ThemeCatalog.cs`.
4. Render the README images and check yours:

   ```powershell
   dotnet build src/TaskbarLed.App
   src/TaskbarLed.App/bin/Debug/net10.0-windows/TaskbarLed.exe --render-demo docs/images
   ```

5. Add your `docs/images/theme-YourTheme.png` to the theme tables in both READMEs.

## Add a language

Text lives in three records: `TickerStrings` (Core), `ActivityStrings` (Sources.ClaudeCode) and `UiStrings` (App). Add one factory per record, map the two-letter language code in its `For` method, and add the language to the **Language** menu in `TrayIcon`.

## Support another agent CLI

Implement `ISessionSource` in a new `TaskbarLed.Sources.<Name>` project, with tests. Point that tool's hooks at `TaskbarLed.Hook.exe <your-source-id>` and register the source in `AppHost`. Return an empty list for any payload you do not understand: never throw.

## Guidelines

- **Read the product rules first.** [CLAUDE.md → Product rules](CLAUDE.md#product-rules-read-before-changing-what-users-see) lists what must not change by default, how themes and text work, and what every pull request must include. AI coding agents read that file too.
- **Keep logic in Core.** If it can be tested without Windows, it belongs in `TaskbarLed.Core` or a `Sources.*` project, with tests.
- **Never break the agent.** Hook-side code must not print, block or fail.
- **Stay light.** No polling faster than the existing timers and no heavy dependencies: the panel runs all day.
- **Privacy.** Nothing leaves the machine. Anything that reads credentials must be opt-in and off by default.
- **Tests read as sentences:** `Approval_then_tool_result_resumes_running_without_restarting_the_timer`.

## Pull requests

Describe what changed and how you verified it. For visual changes, include before-and-after images; `--render-demo` makes them for you.
