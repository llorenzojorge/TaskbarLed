using System.Diagnostics;
using TaskbarLed.Core.Visibility;

namespace TaskbarLed.Platform.Windows.Windowing;

/// <summary>Result of one visibility check.</summary>
/// <param name="WindowFound">A Claude desktop window exists.</param>
/// <param name="VisibleFraction">Share (0..1) of that window the user can see right now.</param>
/// <param name="Handle">The Claude window, for activation.</param>
/// <param name="Editors">Every VS Code (or fork) window and how much of each is visible.</param>
public readonly record struct ClaudeVisibility(bool WindowFound, double VisibleFraction, nint Handle, IReadOnlyList<EditorWindow> Editors)
{
    /// <summary>The same facts in the platform-neutral shape <see cref="SessionVisibility"/> uses.</summary>
    public HostWindows ToHostWindows() => new(WindowFound ? VisibleFraction : 0, Editors ?? []);
}

/// <summary>Answers "can the user see Claude right now?", for the desktop app and for VS Code.</summary>
public interface IClaudeVisibilityProbe
{
    ClaudeVisibility Check();
}

/// <summary>
/// Finds the Claude desktop window and the VS Code windows, and measures how much of each is
/// uncovered by walking the z-order above it. Handles minimized, cloaked (other virtual desktop),
/// covered and snapped windows.
/// </summary>
public sealed class ClaudeVisibilityProbe : IClaudeVisibilityProbe
{
    // Shell surfaces that sit "above" everything but never hide an app.
    private static readonly HashSet<string> ShellClasses = new(StringComparer.Ordinal)
    {
        "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "Progman", "WorkerW", "NotifyIconOverflowWindow",
        "TopLevelWindowForOverflowXamlIsland", "XamlExplorerHostIslandWindow",
    };

    // VS Code and the forks that run the same Claude Code extension.
    private static readonly HashSet<string> EditorProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Code", "Code - Insiders", "VSCodium", "Cursor", "Windsurf",
    };

    private readonly Dictionary<uint, string> _processNames = [];
    private readonly uint _ownProcessId = (uint)Environment.ProcessId;

    public ClaudeVisibility Check()
    {
        var windows = WindowReader.InZOrder();
        PruneProcessCache(windows);
        var screens = Monitors.All().Select(m => m.Bounds).ToList();

        var editors = new List<EditorWindow>();
        for (var i = 0; i < windows.Count; i++)
        {
            if (IsEditorMainWindow(windows[i]))
            {
                editors.Add(new EditorWindow(windows[i].Title, VisibleFraction(windows, i, screens), windows[i].Handle));
            }
        }

        var index = windows.FindIndex(IsClaudeMainWindow);
        return index < 0
            ? new ClaudeVisibility(false, 0, 0, editors)
            : new ClaudeVisibility(true, VisibleFraction(windows, index, screens), windows[index].Handle, editors);
    }

    private double VisibleFraction(List<WindowInfo> windows, int index, List<PixelRect> screens)
    {
        var target = windows[index];
        if (!target.IsOnScreen)
        {
            return 0;
        }

        var occluders = new List<PixelRect>();
        for (var i = 0; i < index; i++)
        {
            var w = windows[i];
            if (w.IsOnScreen && !w.IsToolWindow && !w.IsClickThrough
                && w.ProcessId != _ownProcessId && w.ProcessId != target.ProcessId
                && !ShellClasses.Contains(w.ClassName))
            {
                occluders.Add(w.Bounds);
            }
        }

        return OcclusionCalculator.VisibleFraction(target.Bounds, screens, occluders);
    }

    // The desktop app is an Electron window titled "Claude" owned by a claude.exe process.
    // Matching on both avoids picking up a browser tab or folder that happens to be called "Claude".
    private bool IsClaudeMainWindow(WindowInfo window) =>
        window.Title == "Claude"
        && !window.IsToolWindow
        && window.ClassName.StartsWith("Chrome_WidgetWin", StringComparison.Ordinal)
        && string.Equals(ProcessName(window.ProcessId), "claude", StringComparison.OrdinalIgnoreCase);

    // VS Code is Electron too; its main windows have a title, its helper windows do not.
    private bool IsEditorMainWindow(WindowInfo window) =>
        window.Title.Length > 0
        && window.IsVisible
        && !window.IsToolWindow
        && window.ClassName.StartsWith("Chrome_WidgetWin", StringComparison.Ordinal)
        && EditorProcesses.Contains(ProcessName(window.ProcessId));

    private string ProcessName(uint processId)
    {
        if (_processNames.TryGetValue(processId, out var known))
        {
            return known;
        }

        string name;
        try
        {
            using var process = Process.GetProcessById((int)processId);
            name = process.ProcessName;
        }
        catch (ArgumentException)
        {
            name = string.Empty; // Process already exited.
        }
        catch (InvalidOperationException)
        {
            name = string.Empty;
        }

        _processNames[processId] = name;
        return name;
    }

    // Process ids get reused; forget the ones whose windows are gone.
    private void PruneProcessCache(List<WindowInfo> windows)
    {
        if (_processNames.Count < 512)
        {
            return;
        }

        var alive = windows.Select(w => w.ProcessId).ToHashSet();
        foreach (var pid in _processNames.Keys.Where(pid => !alive.Contains(pid)).ToList())
        {
            _processNames.Remove(pid);
        }
    }
}
