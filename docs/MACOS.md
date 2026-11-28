# TaskbarLed on macOS

[Português do Brasil](MACOS.pt-BR.md) · [Theme gallery](MACOS-THEMES.md)

## Requirements

The macOS preview requires macOS 14 or later. Apple Silicon (arm64, M1 or newer) was tested natively on an M2 with macOS 26.5.2. Intel (x64) was built for Intel and tested through Rosetta on that M2. Physical Intel hardware and other macOS versions still need direct verification.

## Install

When macOS packages are published, get the appropriate `.pkg` from the [upstream releases](https://github.com/llorenzojorge/TaskbarLed/releases). Select the arm64 package for an Apple chip or x64 for Intel. Open the package and follow the installer. Open **TaskbarLed.app** in Applications. For a ZIP build, extract it and move the app to Applications first. Each package includes the .NET runtime.

The preview is unsigned and not notarized. If macOS says Apple cannot verify the installer, compare its SHA-256 with the release checksum. After trying to open the package, use **System Settings → Privacy & Security → Open Anyway**. You can also inspect the source at the release tag and build it locally. [Apple instructions](https://support.apple.com/en-us/102445)

## Build from source

Install the .NET SDK from `global.json` and Apple's command-line tools. From the repository root, run the command for your Mac:

```sh
src/TaskbarLed.App.Avalonia/build-macos.sh osx-arm64
src/TaskbarLed.App.Avalonia/build-macos.sh osx-x64
```

The script creates a `.pkg` and a `.zip` in `artifacts/TaskbarLed-<rid>/`. Quit any earlier copy before installing an update.

## Use the panel

TaskbarLed floats above the Dock. Activity opens the panel. The menu bar icon can keep it visible when idle. Right-click for **Themes**, **Edit position and size** or **Quit**. Choose automatic sizing or drag the right edge, bottom edge or corner in manual mode. Opacity affects the theme surface while text remains legible. Completed sessions are shown for ten seconds. Line Map keeps each route letter stable while its session is visible. Liquid Glass uses a frosted native material on macOS 26. Choose **Crystal Liquid** for a clearer, less tinted surface. Earlier versions use a translucent surface.

Each session and subagent shows elapsed time when a start is known. A tilde marks time measured since the panel first saw an Antigravity worker.

Enable **Dynamic Island mode** from the panel or menu bar to dock it to the top edge of the primary display, centered over the camera cutout when present. It opens as a compact bar with the app mark and activity count. Click the bar to expand or collapse the sessions. It keeps the selected theme. Turn the mode off to restore your saved placement.

## Connect agents

The panel reads local Octo, Codex and Antigravity activity. Subagents are shown below their parent even when it is idle. The local executable probe can identify a running tool without reporting individual sessions. Add a custom process name and display label in `~/Library/Application Support/TaskbarLed/agents.json`.

For Claude Code, select **Enable Claude Code integration…** in the app menu and restart your Claude Code sessions. The app preserves existing settings and hooks.

Other orchestrators can forward events to the included hook. Run the bridge on the Mac, including when it relays events from a remote worker. Each invocation reads one JSON object from stdin:

```sh
hook="/Applications/TaskbarLed.app/Contents/MacOS/TaskbarLed.Hook"
printf '%s\n' '{"agent":"pipeline","session_id":"job-42","status":"started","title":"Build"}' | "$hook" agent
```

Use a stable `agent` and a unique `session_id` for each concurrent job. Supported statuses are `started`, `running`, `waiting`, `done` and `ended`. The optional `title` and `message` fields control the text. Set `parent_session_id` to group a subagent under a parent with the same `agent`. Input over 2 MiB is dropped.

## Uninstall

Disable Claude Code integration from the menu if enabled, then choose **Quit**. Move the app from Applications to the Trash in Finder. The installer may require administrator authorization to remove it. Preferences remain under `~/Library/Application Support/TaskbarLed`.

## Release

The macOS job builds a `.pkg` and `.zip` for each architecture and uploads them with SHA-256 files to the same `vX.Y.Z` release as Windows.
