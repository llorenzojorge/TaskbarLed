using System.Runtime.InteropServices;
using TaskbarLed.Core.Visibility;
using static TaskbarLed.Platform.Windows.Native.NativeMethods;

namespace TaskbarLed.Platform.Windows.Windowing;

/// <summary>A display and its taskbar.</summary>
/// <param name="Handle">HMONITOR.</param>
/// <param name="DeviceName">Stable-ish name such as <c>\\.\DISPLAY1</c>.</param>
/// <param name="IsPrimary">Primary monitor.</param>
/// <param name="Bounds">Monitor rectangle in physical pixels.</param>
/// <param name="Taskbar">Taskbar rectangle on this monitor, if it has one.</param>
/// <param name="Dpi">Effective DPI (96 = 100%).</param>
/// <param name="TaskbarItems">Buttons on this monitor's taskbar (Start, apps, tray), from <see cref="TaskbarButtonWatcher"/>.</param>
public sealed record MonitorInfo(nint Handle, string DeviceName, bool IsPrimary, PixelRect Bounds, PixelRect? Taskbar, uint Dpi, IReadOnlyList<PixelRect>? TaskbarItems = null)
{
    public double Scale => Dpi / 96d;
}

/// <summary>Enumerates monitors and pairs them with their taskbars.</summary>
public static unsafe class Monitors
{
    public static IReadOnlyList<MonitorInfo> All()
    {
        var handles = new List<nint>();
        var gc = GCHandle.Alloc(handles);
        try
        {
            EnumDisplayMonitors(0, 0, &Collect, GCHandle.ToIntPtr(gc));
        }
        finally
        {
            gc.Free();
        }

        var taskbars = TaskbarRects();
        return [.. handles.Select(h => Describe(h, taskbars))];
    }

    /// <summary>Monitor that contains the largest part of <paramref name="hwnd"/>.</summary>
    public static nint FromWindow(nint hwnd) => MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);

    private static MonitorInfo Describe(nint handle, Dictionary<nint, PixelRect> taskbars)
    {
        var info = new MONITORINFOEX { cbSize = sizeof(MONITORINFOEX) };
        GetMonitorInfo(handle, ref info);
        var device = new string(info.szDevice);
        var bounds = new PixelRect(info.rcMonitor.Left, info.rcMonitor.Top, info.rcMonitor.Right, info.rcMonitor.Bottom);
        var dpi = GetDpiForMonitor(handle, MDT_EFFECTIVE_DPI, out var dpiX, out _) == 0 ? dpiX : 96u;
        PixelRect? taskbar = taskbars.TryGetValue(handle, out var rect) ? rect : null;
        return new MonitorInfo(handle, device, (info.dwFlags & MONITORINFOF_PRIMARY) != 0, bounds, taskbar, dpi);
    }

    // Primary taskbar is Shell_TrayWnd; every other monitor's is a Shell_SecondaryTrayWnd.
    private static Dictionary<nint, PixelRect> TaskbarRects()
    {
        var result = new Dictionary<nint, PixelRect>();
        AddTaskbar(result, FindWindowEx(0, 0, "Shell_TrayWnd", null));
        for (var hwnd = FindWindowEx(0, 0, "Shell_SecondaryTrayWnd", null);
             hwnd != 0;
             hwnd = FindWindowEx(0, hwnd, "Shell_SecondaryTrayWnd", null))
        {
            AddTaskbar(result, hwnd);
        }

        return result;
    }

    private static void AddTaskbar(Dictionary<nint, PixelRect> result, nint hwnd)
    {
        if (hwnd != 0 && GetWindowRect(hwnd, out var rect))
        {
            result[MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST)] = new PixelRect(rect.Left, rect.Top, rect.Right, rect.Bottom);
        }
    }

    [UnmanagedCallersOnly]
    private static int Collect(nint monitor, nint hdc, RECT* rect, nint data)
    {
        ((List<nint>)GCHandle.FromIntPtr(data).Target!).Add(monitor);
        return 1;
    }
}
