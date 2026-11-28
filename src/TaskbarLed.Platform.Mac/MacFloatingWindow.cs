using System.Runtime.Versioning;
using TaskbarLed.Platform.Mac.Native;

namespace TaskbarLed.Platform.Mac;

/// <summary>Native tweaks that make the panel behave like a status overlay on macOS.</summary>
[SupportedOSPlatform("macos")]
public static class MacFloatingWindow
{
    // NSWindowCollectionBehavior flags.
    private const nuint CanJoinAllSpaces = 1 << 0;
    private const nuint Stationary = 1 << 4;
    private const nuint IgnoresCycle = 1 << 6;
    private const nuint FullScreenAuxiliary = 1 << 8;

    /// <summary>
    /// Shows <paramref name="nsWindow"/> on every Space (desktop), keeps it out of Cmd+` cycling and
    /// Mission Control, and lets it sit next to full-screen apps.
    /// </summary>
    /// <param name="nsWindow">The <c>NSWindow*</c> of the panel.</param>
    public static void JoinAllSpaces(nint nsWindow)
    {
        if (nsWindow == 0)
        {
            return;
        }

        MacNative.SendNUInt(nsWindow, MacNative.sel_registerName("setCollectionBehavior:"), CanJoinAllSpaces | Stationary | IgnoresCycle | FullScreenAuxiliary);
        MacNative.SendBool(nsWindow, MacNative.sel_registerName("setHidesOnDeactivate:"), false);
    }

    public static nint GetLevel(nint nsWindow) =>
        MacNative.SendPointer(nsWindow, MacNative.sel_registerName("level"));

    public static void SetLevel(nint nsWindow, nint level) =>
        MacNative.SendNUInt(nsWindow, MacNative.sel_registerName("setLevel:"), unchecked((nuint)level));

    // NSStatusWindowLevel is above the main menu bar.
    public const int StatusWindowLevel = 25;

    public static nint AddLiquidGlass(nint nsWindow, double width, double height, double opacity, bool crystal = false)
    {
        if (nsWindow == 0)
        {
            return 0;
        }

        var host = MacNative.SendPointer(nsWindow, MacNative.sel_registerName("contentView"));
        if (host == 0)
        {
            return 0;
        }

        if (crystal)
        {
            var clear = NewView("NSGlassEffectView");
            if (clear == 0)
            {
                return 0;
            }

            ConfigureClearGlass(clear, width, height);
            SetGlassOpacity(clear, opacity);
            AddSubview(host, clear, -1);
            return clear;
        }

        var backing = NewView("NSVisualEffectView");
        if (backing == 0)
        {
            return 0;
        }

        // Keep the behind-window material active even when another app owns focus.
        MacNative.SendNUInt(backing, MacNative.sel_registerName("setMaterial:"), 13); // HUD window
        MacNative.SendNUInt(backing, MacNative.sel_registerName("setBlendingMode:"), 0); // Behind window
        MacNative.SendNUInt(backing, MacNative.sel_registerName("setState:"), 1); // Active
        MacNative.SendBool(backing, MacNative.sel_registerName("setWantsLayer:"), true);
        var layer = MacNative.SendPointer(backing, MacNative.sel_registerName("layer"));
        if (layer != 0)
        {
            MacNative.SendDouble(layer, MacNative.sel_registerName("setCornerRadius:"), 16);
            MacNative.SendBool(layer, MacNative.sel_registerName("setMasksToBounds:"), true);
        }

        SizeToHost(backing, width, height);
        var glass = NewView("NSGlassEffectView");
        if (glass != 0)
        {
            ConfigureClearGlass(glass, width, height);
            // The clear highlight may change with focus. The active material underneath remains visible.
            SetGlassOpacity(glass, 0.32);
            AddSubview(backing, glass, 1);
        }

        SetGlassOpacity(backing, opacity);
        AddSubview(host, backing, -1);
        return backing;
    }

    private static nint NewView(string name)
    {
        var type = MacNative.objc_lookUpClass(name);
        return type == 0 ? 0 : MacNative.SendPointer(type, MacNative.sel_registerName("new"));
    }

    private static void ConfigureClearGlass(nint view, double width, double height)
    {
        MacNative.SendDouble(view, MacNative.sel_registerName("setCornerRadius:"), 16);
        MacNative.SendNUInt(view, MacNative.sel_registerName("setStyle:"), 1); // Clear
        SizeToHost(view, width, height);
    }

    private static void SizeToHost(nint view, double width, double height)
    {
        MacNative.SendNUInt(view, MacNative.sel_registerName("setAutoresizingMask:"), 18);
        MacNative.SendRect(view, MacNative.sel_registerName("setFrame:"),
            new MacNative.CGRect { Width = width, Height = height });
    }

    private static void AddSubview(nint host, nint view, nint order)
    {
        MacNative.AddSubview(host, MacNative.sel_registerName("addSubview:positioned:relativeTo:"), view, order, 0);
        MacNative.SendVoid(view, MacNative.sel_registerName("release"));
    }

    public static void SetGlassOpacity(nint glass, double opacity) =>
        MacNative.SendDouble(glass, MacNative.sel_registerName("setAlphaValue:"), opacity);

    public static void RemoveLiquidGlass(nint glass) =>
        MacNative.SendVoid(glass, MacNative.sel_registerName("removeFromSuperview"));
}
