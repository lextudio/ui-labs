using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace LeXtudio.DevFlow.Agent.Core;

[SupportedOSPlatform("macos")]
public static class MacOSNativeInput
{
    private const string ApplicationServices = "/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices";
    private const uint CGHIDEventTap = 0;
    private const ulong CGEventFlagMaskCommand = 0x00100000;
    private const ulong CGEventFlagMaskShift = 0x00020000;

    private const ushort KVK_Return = 0x24;
    private const ushort KVK_Delete = 0x33; // Backspace on macOS
    private const ushort KVK_ANSI_A = 0x00;

    // CGEventPost requires Accessibility permission (TCC) to actually deliver
    // events. AXIsProcessTrusted() is not a reliable signal on GitHub-hosted
    // macOS runners — it returns true for terminal-launched non-sandboxed
    // processes, yet the events still silently no-op. The agent would then
    // falsely report "native" success without any side effect.
    // The correct long-term fix is to bypass the global event tap entirely
    // and post NSEvents directly into our own NSApp event queue (which is
    // not TCC-gated). Until that lands, the macOS Posix native path is
    // disabled so the agent falls through to property-mutation / semantic
    // routes that do produce observable outcomes.
    public static bool IsAvailable => false;

    public static bool SendUnicodeText(string text)
    {
        if (!IsAvailable || string.IsNullOrEmpty(text))
            return IsAvailable; // empty text is a no-op success when available

        foreach (var ch in text)
        {
            if (!SendUnicodeChar(ch))
                return false;
        }

        return true;
    }

    public static bool SendReturn() => SendVirtualKey(KVK_Return);

    public static bool SendBackspace() => SendVirtualKey(KVK_Delete);

    public static bool SendSelectAll()
    {
        if (!IsAvailable)
            return false;

        // Cmd+A using the 'A' keycode with command flag.
        var down = CGEventCreateKeyboardEvent(IntPtr.Zero, KVK_ANSI_A, true);
        var up = CGEventCreateKeyboardEvent(IntPtr.Zero, KVK_ANSI_A, false);
        if (down == IntPtr.Zero || up == IntPtr.Zero)
        {
            ReleaseIfNotZero(down);
            ReleaseIfNotZero(up);
            return false;
        }

        try
        {
            CGEventSetFlags(down, CGEventFlagMaskCommand);
            CGEventSetFlags(up, CGEventFlagMaskCommand);
            CGEventPost(CGHIDEventTap, down);
            CGEventPost(CGHIDEventTap, up);
            return true;
        }
        finally
        {
            CFRelease(down);
            CFRelease(up);
        }
    }

    private static bool SendVirtualKey(ushort keyCode)
    {
        if (!IsAvailable)
            return false;

        var down = CGEventCreateKeyboardEvent(IntPtr.Zero, keyCode, true);
        var up = CGEventCreateKeyboardEvent(IntPtr.Zero, keyCode, false);
        if (down == IntPtr.Zero || up == IntPtr.Zero)
        {
            ReleaseIfNotZero(down);
            ReleaseIfNotZero(up);
            return false;
        }

        try
        {
            CGEventPost(CGHIDEventTap, down);
            CGEventPost(CGHIDEventTap, up);
            return true;
        }
        finally
        {
            CFRelease(down);
            CFRelease(up);
        }
    }

    private static bool SendUnicodeChar(char ch)
    {
        // Use a keyboard event with no virtual key, then override the Unicode payload.
        var down = CGEventCreateKeyboardEvent(IntPtr.Zero, 0, true);
        var up = CGEventCreateKeyboardEvent(IntPtr.Zero, 0, false);
        if (down == IntPtr.Zero || up == IntPtr.Zero)
        {
            ReleaseIfNotZero(down);
            ReleaseIfNotZero(up);
            return false;
        }

        try
        {
            var buffer = new ushort[] { ch };
            CGEventKeyboardSetUnicodeString(down, (UIntPtr)buffer.Length, buffer);
            CGEventKeyboardSetUnicodeString(up, (UIntPtr)buffer.Length, buffer);
            CGEventPost(CGHIDEventTap, down);
            CGEventPost(CGHIDEventTap, up);
            return true;
        }
        finally
        {
            CFRelease(down);
            CFRelease(up);
        }
    }

    private static void ReleaseIfNotZero(IntPtr handle)
    {
        if (handle != IntPtr.Zero)
            CFRelease(handle);
    }

    // ── Mouse injection (independent of the keyboard IsAvailable gate) ──────
    //
    // Posts a real press → drag → release gesture into the HID event stream so
    // that code reading the *global* cursor / button state (e.g. the Reactor
    // docking tear-off tracker, which polls CGEventSourceButtonState +
    // CGEventGetLocation on a timer) observes it exactly as it would a human
    // drag. Coordinates are Quartz global display points, top-left origin —
    // the same space CGEventGetLocation reports.
    //
    // Like the keyboard path, delivery requires macOS Accessibility (TCC)
    // permission for the host process; without it CGEventPost silently no-ops.

    private const uint kCGEventLeftMouseDown = 1;
    private const uint kCGEventLeftMouseUp = 2;
    private const uint kCGEventMouseMoved = 5;
    private const uint kCGEventLeftMouseDragged = 6;
    private const uint kCGMouseButtonLeft = 0;
    private const int kCGMouseEventClickState = 1;   // CGEventField value for click count
    // kCGEventSourceStatePrivate = -1: private stateful source so button-down
    // persists across events and AppKit/Uno see IsLeftButtonPressed = true.
    private const int kCGEventSourceStatePrivate = -1;

    /// <summary>Injects a left-click (MouseDown + MouseUp) at the given global screen point.
    /// Uses a private stateful source so button state is coherent with any concurrent drag.</summary>
    [SupportedOSPlatform("macos")]
    public static bool TryMouseClick(double x, double y, int clickCount = 1)
    {
        var source = CGEventSourceCreate(kCGEventSourceStatePrivate);
        try
        {
            if (!PostMouse(kCGEventMouseMoved,     x, y, source)) return false;
            Sleep(16);
            for (int c = 0; c < clickCount; c++)
            {
                if (!PostMouse(kCGEventLeftMouseDown, x, y, source)) return false;
                Sleep(50);
                if (!PostMouse(kCGEventLeftMouseUp,   x, y, source)) return false;
                if (c < clickCount - 1) Sleep(80);
            }
            return true;
        }
        finally
        {
            if (source != IntPtr.Zero) CFRelease(source);
        }
    }

    [SupportedOSPlatform("macos")]
    public static bool TryMouseMove(double x, double y)
    {
        var source = CGEventSourceCreate(kCGEventSourceStatePrivate);
        try
        {
            return PostMouse(kCGEventMouseMoved, x, y, source);
        }
        finally
        {
            if (source != IntPtr.Zero) CFRelease(source);
        }
    }

    [SupportedOSPlatform("macos")]
    public static bool TryMouseDrag(double fromX, double fromY, double toX, double toY,
        int steps = 24, int stepDelayMs = 16, int holdAfterDownMs = 200)
    {
        if (steps < 1) steps = 1;

        // Private stateful source: the source tracks button state, so
        // kCGEventLeftMouseDragged events inherit the button-down from the
        // preceding kCGEventLeftMouseDown, giving AppKit a coherent gesture.
        var source = CGEventSourceCreate(kCGEventSourceStatePrivate);
        try
        {
            if (!PostMouse(kCGEventMouseMoved, fromX, fromY, source)) return false;
            Sleep(stepDelayMs);
            if (!PostMouse(kCGEventLeftMouseDown, fromX, fromY, source)) return false;
            Sleep(holdAfterDownMs);

            for (int i = 1; i <= steps; i++)
            {
                double t = (double)i / steps;
                double x = fromX + (toX - fromX) * t;
                double y = fromY + (toY - fromY) * t;
                if (!PostMouse(kCGEventLeftMouseDragged, x, y, source)) return false;
                Sleep(stepDelayMs);
            }

            PostMouse(kCGEventLeftMouseUp, toX, toY, source);
            return true;
        }
        finally
        {
            if (source != IntPtr.Zero) CFRelease(source);
        }
    }

    private static void Sleep(int ms)
    {
        if (ms > 0) global::System.Threading.Thread.Sleep(ms);
    }

    private static bool PostMouse(uint type, double x, double y, IntPtr source)
    {
        var evt = CGEventCreateMouseEvent(source, type, new CGPoint { X = x, Y = y }, kCGMouseButtonLeft);
        if (evt == IntPtr.Zero) return false;
        try
        {
            if (type == kCGEventLeftMouseDown || type == kCGEventLeftMouseUp)
                CGEventSetIntegerValueField(evt, kCGMouseEventClickState, 1);
            CGEventPost(CGHIDEventTap, evt);
            return true;
        }
        finally { CFRelease(evt); }
    }

    [DllImport(ApplicationServices)]
    private static extern IntPtr CGEventSourceCreate(int stateID);

    [DllImport(ApplicationServices)]
    private static extern void CGEventSetIntegerValueField(IntPtr @event, int field, long value);

    [StructLayout(LayoutKind.Sequential)]
    private struct CGPoint { public double X; public double Y; }

    [DllImport(ApplicationServices)]
    private static extern IntPtr CGEventCreateMouseEvent(IntPtr source, uint mouseType, CGPoint mouseCursorPosition, uint mouseButton);

    [DllImport(ApplicationServices)]
    private static extern IntPtr CGEventCreateKeyboardEvent(IntPtr source, ushort virtualKey, [MarshalAs(UnmanagedType.I1)] bool keyDown);

    [DllImport(ApplicationServices)]
    private static extern void CGEventKeyboardSetUnicodeString(IntPtr @event, UIntPtr stringLength, [In] ushort[] unicodeString);

    [DllImport(ApplicationServices)]
    private static extern void CGEventPost(uint tap, IntPtr @event);

    [DllImport(ApplicationServices)]
    private static extern void CGEventSetFlags(IntPtr @event, ulong flags);

    [DllImport(ApplicationServices)]
    private static extern void CFRelease(IntPtr cf);
}
