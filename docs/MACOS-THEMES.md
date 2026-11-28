# TaskbarLed themes on macOS

Screenshots of the panel's 19 themes, rendered by TaskbarLed with two sessions and four subagents. Pick a theme from the **Themes** menu in the menu bar.

[Português do Brasil](MACOS-THEMES.pt-BR.md) · [Install on Mac](MACOS.md) · [Back to the README](../README.md)

## Shared themes

| Crystal Liquid |
| --- |
| [![Crystal Liquid](images/macos-themes/CrystalLiquid.png)](images/macos-themes/CrystalLiquid.png) |

| Line Map | Liquid Glass |
| --- | --- |
| [![Line Map](images/macos-themes/LineMap.png)](images/macos-themes/LineMap.png) | [![Liquid Glass](images/macos-themes/LiquidGlass.png)](images/macos-themes/LiquidGlass.png) |

| Editorial Index | Cobalt Window |
| --- | --- |
| [![Editorial Index](images/macos-themes/EditorialIndex.png)](images/macos-themes/EditorialIndex.png) | [![Cobalt Window](images/macos-themes/CobaltWindow.png)](images/macos-themes/CobaltWindow.png) |

| Violet Phosphor | Olive LCD |
| --- | --- |
| [![Violet Phosphor](images/macos-themes/VioletPhosphor.png)](images/macos-themes/VioletPhosphor.png) | [![Olive LCD](images/macos-themes/OliveLcd.png)](images/macos-themes/OliveLcd.png) |

| Amber Tree | Compact Charcoal |
| --- | --- |
| [![Amber Tree](images/macos-themes/AmberTree.png)](images/macos-themes/AmberTree.png) | [![Compact Charcoal](images/macos-themes/CompactCharcoal.png)](images/macos-themes/CompactCharcoal.png) |

| Amber Rail | Clear List |
| --- | --- |
| [![Amber Rail](images/macos-themes/AmberRail.png)](images/macos-themes/AmberRail.png) | [![Clear List](images/macos-themes/ClearList.png)](images/macos-themes/ClearList.png) |

The gallery screenshot shows the panel's gradient. On macOS 26, the native AppKit glass lets the color behind the window show through. A finished row gets a neutral pulse and disappears after ten seconds.

### Position, size and opacity editor

![TaskbarLed editor on macOS 26 with Liquid Glass, Manual mode and 81% opacity](images/macos-themes/LiquidGlassEditor.png)

Screenshot of the packaged app on macOS 26.5.2. The finished row uses test data.

## Other included themes

| Claude Code | Modern Terminal |
| --- | --- |
| [![Claude Code](images/macos-themes/ClaudeCode.png)](images/macos-themes/ClaudeCode.png) | [![Modern Terminal](images/macos-themes/ModernTerminal.png)](images/macos-themes/ModernTerminal.png) |

| Matrix | Mono |
| --- | --- |
| [![Matrix](images/macos-themes/Matrix.png)](images/macos-themes/Matrix.png) | [![Mono](images/macos-themes/Mono.png)](images/macos-themes/Mono.png) |

| Minimal | Glass |
| --- | --- |
| [![Minimal](images/macos-themes/Minimal.png)](images/macos-themes/Minimal.png) | [![Glass](images/macos-themes/Glass.png)](images/macos-themes/Glass.png) |

| Paper | Octo |
| --- | --- |
| [![Paper](images/macos-themes/Paper.png)](images/macos-themes/Paper.png) | [![Octo](images/macos-themes/Octo.png)](images/macos-themes/Octo.png) |

## Update the screenshots

From the repository root, run the rendering tests to recreate the images.

~~~sh
TASKBARLED_THEME_GALLERY="$PWD/docs/images/macos-themes" dotnet test tests/TaskbarLed.App.Avalonia.Tests -c Release --filter FullyQualifiedName~ThemeGalleryTests
~~~
