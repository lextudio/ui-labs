using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace LeXtudio.DevFlow.Agent.Core;

/// <summary>
/// Measures the app's main window CONTENT-area origin in Quartz global points (top-left origin,
/// Y-down) via the native NSWindow content view — the coordinate space cliclick / CGEvent drag
/// injection require.
/// </summary>
/// <remarks>
/// This exists because <c>AppWindow.Position</c> on the Uno-Skia-macOS target is the *outer window
/// frame* origin (and drifts from the content area by roughly a title-bar height), so a drag
/// computed from it silently lands on the title bar instead of the intended element — the exact
/// class of "the drag path goes outside the content" bug the DataGrid reorder test hit. UnoDock's
/// design.md prescribes measuring the real content-origin instead; this ports the proven native
/// lookup (formerly DataGrid.IntegrationTestHost.MacOSWindowHelper) into the shared agent so every
/// drag benefits.
/// </remarks>
[SupportedOSPlatform("macos")]
public static class MacOSWindowOrigin
{
    private const string ObjC = "/System/Library/Frameworks/Foundation.framework/Foundation";
    private const string CG = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";

    [DllImport(ObjC)] private static extern IntPtr objc_getClass(string name);
    [DllImport(ObjC)] private static extern IntPtr sel_registerName(string name);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr msgSend(IntPtr self, IntPtr op);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern ulong msgSend_retNUInt(IntPtr self, IntPtr op);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern bool msgSend_retBool(IntPtr self, IntPtr op);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern long msgSend_nuint_retLong(IntPtr self, IntPtr op, nuint arg);
    // NOTE: on arm64 (Apple Silicon) there is NO objc_msgSend_stret — struct returns go through
    // plain objc_msgSend (the P/Invoke marshaller handles the x8 indirect-return register and the
    // by-value CGRect argument in v0-v3). Using objc_msgSend_stret here on arm64 silently returned
    // zeroed structs. This environment targets arm64 macOS; x86_64 would need _stret for a 32-byte CGRect.
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern CGRect msgSend_retCGRect(IntPtr self, IntPtr op);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern CGRect msgSend_CGRect_retCGRect(IntPtr self, IntPtr op, CGRect arg);

    [DllImport(CG)] private static extern uint CGMainDisplayID();
    [DllImport(CG)] private static extern nuint CGDisplayPixelsHigh(uint display);

    private static readonly IntPtr _clsNSScreen = objc_getClass("NSScreen");
    private static readonly IntPtr _selScreens = sel_registerName("screens");
    private static readonly IntPtr _selFrame = sel_registerName("frame");

    private struct CGPoint { public double X, Y; }
    private struct CGSize { public double Width, Height; }
    private struct CGRect { public CGPoint Origin; public CGSize Size; }

    private static readonly IntPtr _clsNSApp = objc_getClass("NSApplication");
    private static readonly IntPtr _selSharedApp = sel_registerName("sharedApplication");
    private static readonly IntPtr _selWindows = sel_registerName("windows");
    private static readonly IntPtr _selCount = sel_registerName("count");
    private static readonly IntPtr _selObjectAtIndex = sel_registerName("objectAtIndex:");
    private static readonly IntPtr _selStyleMask = sel_registerName("styleMask");
    private static readonly IntPtr _selIsVisible = sel_registerName("isVisible");
    private static readonly IntPtr _selContentView = sel_registerName("contentView");
    private static readonly IntPtr _selBounds = sel_registerName("bounds");
    private static readonly IntPtr _selConvertRectToScreen = sel_registerName("convertRectToScreen:");

    private const ulong NSWindowStyleMaskTitled = 1;

    /// <summary>Finds the app's main titled NSWindow handle, or IntPtr.Zero.</summary>
    public static IntPtr GetMainNSWindow()
    {
        try
        {
            var app = msgSend(_clsNSApp, _selSharedApp);
            if (app == IntPtr.Zero) return IntPtr.Zero;
            var wins = msgSend(app, _selWindows);
            if (wins == IntPtr.Zero) return IntPtr.Zero;
            var count = (long)msgSend_retNUInt(wins, _selCount);
            for (long i = 0; i < count; i++)
            {
                var w = (IntPtr)msgSend_nuint_retLong(wins, _selObjectAtIndex, (nuint)i);
                if (w == IntPtr.Zero) continue;
                var style = msgSend_retNUInt(w, _selStyleMask);
                if ((style & NSWindowStyleMaskTitled) == 0) continue;
                if (!msgSend_retBool(w, _selIsVisible)) continue;
                return w;
            }
        }
        catch { }
        return IntPtr.Zero;
    }

    /// <summary>
    /// Height (points) of the PRIMARY display — NSScreen.screens[0], the menu-bar screen whose
    /// Cocoa frame origin is (0,0). This is the flip reference for Cocoa↔Quartz global coordinates.
    /// </summary>
    private static double GetPrimaryScreenHeight()
    {
        try
        {
            var screens = msgSend(_clsNSScreen, _selScreens);
            if (screens == IntPtr.Zero) return 0;
            var count = (long)msgSend_retNUInt(screens, _selCount);
            if (count <= 0) return 0;
            var primary = (IntPtr)msgSend_nuint_retLong(screens, _selObjectAtIndex, 0);
            if (primary == IntPtr.Zero) return 0;
            return msgSend_retCGRect(primary, _selFrame).Size.Height;
        }
        catch { return 0; }
    }

    /// <summary>
    /// Returns the main window CONTENT origin in Quartz global points (top-left origin, Y-down),
    /// or null if it cannot be measured. A (0,0) native reading is treated as a failure so callers
    /// fall back to another source rather than dragging to the screen corner.
    /// </summary>
    public static (double X, double Y)? TryGetContentOrigin()
    {
        var nsWindow = GetMainNSWindow();
        if (nsWindow == IntPtr.Zero) return null;
        try
        {
            var contentView = msgSend(nsWindow, _selContentView);
            if (contentView == IntPtr.Zero) return null;

            var bounds = msgSend_retCGRect(contentView, _selBounds);
            var inScreen = msgSend_CGRect_retCGRect(nsWindow, _selConvertRectToScreen, bounds);

            // Y-flip Cocoa (bottom-left origin) → Quartz (top-left origin, the space CGEvent /
            // cliclick use) must use the PRIMARY display height in POINTS — the display whose
            // Cocoa frame origin is (0,0) (the menu-bar screen), which is NSScreen.screens[0].
            // NOT NSScreen.mainScreen: that is the *focus* screen, which in a multi-monitor setup
            // is often a different display (e.g. an external 2560x1440), and using its height
            // shifts the computed origin by the height difference — the bug that put the window's
            // content origin at Y=800 instead of ~477. Quartz global is anchored to the primary
            // display's top-left regardless of which screen the window is on.
            double primaryH = GetPrimaryScreenHeight();
            if (primaryH <= 0) primaryH = CGDisplayPixelsHigh(CGMainDisplayID());

            var quartzY = primaryH - (inScreen.Origin.Y + inScreen.Size.Height);
            if (inScreen.Origin.X == 0 && quartzY == 0)
                return null;
            return (inScreen.Origin.X, quartzY);
        }
        catch
        {
            return null;
        }
    }
}
