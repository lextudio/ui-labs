using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace LeXtudio.DevFlow.Agent.Core;

[SupportedOSPlatform("linux")]
public static class LinuxNativeInput
{
    private const string LibX11 = "libX11.so.6";
    private const string LibXtst = "libXtst.so.6";

    private const uint XK_Return = 0xFF0D;
    private const uint XK_BackSpace = 0xFF08;
    private const uint XK_Shift_L = 0xFFE1;
    private const uint XK_a = 0x0061;

    public static bool IsAvailable => RuntimeInformation.IsOSPlatform(OSPlatform.Linux) && TryGetDisplay() != IntPtr.Zero;

    /// <summary>X11 button number for the left mouse button.</summary>
    private const uint Button1 = 1;

    /// <summary>
    /// Moves the pointer to an absolute screen position via XTest.
    /// </summary>
    public static bool TryMouseMove(double x, double y)
        => WithDisplay(display =>
        {
            XTestFakeMotionEvent(display, -1, (int)Math.Round(x), (int)Math.Round(y), 0);
            return true;
        });

    /// <summary>Presses the left button at an absolute screen position.</summary>
    public static bool TryMousePressDown(double x, double y)
        => WithDisplay(display =>
        {
            XTestFakeMotionEvent(display, -1, (int)Math.Round(x), (int)Math.Round(y), 0);
            XTestFakeButtonEvent(display, Button1, true, 0);
            return true;
        });

    /// <summary>Releases the left button at an absolute screen position.</summary>
    public static bool TryMouseRelease(double x, double y)
        => WithDisplay(display =>
        {
            XTestFakeMotionEvent(display, -1, (int)Math.Round(x), (int)Math.Round(y), 0);
            XTestFakeButtonEvent(display, Button1, false, 0);
            return true;
        });

    /// <summary>Clicks the left button at an absolute screen position.</summary>
    public static bool TryMouseClick(double x, double y, int clickCount)
        => WithDisplay(display =>
        {
            XTestFakeMotionEvent(display, -1, (int)Math.Round(x), (int)Math.Round(y), 0);
            for (var i = 0; i < Math.Max(1, clickCount); i++)
            {
                XTestFakeButtonEvent(display, Button1, true, 0);
                XTestFakeButtonEvent(display, Button1, false, 0);
            }

            return true;
        });

    /// <summary>
    /// Presses at the start point, moves to the end point in steps, then releases. The intermediate
    /// moves matter: applications that track a drag need to observe motion between press and
    /// release, not just the endpoints.
    /// </summary>
    public static bool TryMouseDrag(double fromX, double fromY, double toX, double toY, int steps)
        => WithDisplay(display =>
        {
            var count = Math.Max(1, steps);

            XTestFakeMotionEvent(display, -1, (int)Math.Round(fromX), (int)Math.Round(fromY), 0);
            XFlush(display);
            XTestFakeButtonEvent(display, Button1, true, 0);
            XFlush(display);

            for (var i = 1; i <= count; i++)
            {
                var t = (double)i / count;
                XTestFakeMotionEvent(
                    display,
                    -1,
                    (int)Math.Round(fromX + ((toX - fromX) * t)),
                    (int)Math.Round(fromY + ((toY - fromY) * t)),
                    0);
                XFlush(display);

                // Give the target a chance to process each move; a burst of motion delivered in one
                // go can be coalesced by the server and read as a single jump.
                Thread.Sleep(16);
            }

            XTestFakeButtonEvent(display, Button1, false, 0);
            return true;
        });

    /// <summary>
    /// Runs an XTest sequence against a short-lived display connection, flushing and closing it
    /// afterwards. Returns false when no X display is reachable (for example a pure Wayland session).
    /// </summary>
    private static bool WithDisplay(Func<IntPtr, bool> action)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return false;

        var display = TryGetDisplay();
        if (display == IntPtr.Zero)
            return false;

        try
        {
            var result = action(display);
            XFlush(display);
            return result;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
        finally
        {
            XCloseDisplay(display);
        }
    }

    public static bool SendUnicodeText(string text)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || string.IsNullOrEmpty(text))
            return RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

        var display = TryGetDisplay();
        if (display == IntPtr.Zero)
            return false;

        try
        {
            foreach (var ch in text)
            {
                if (!SendChar(display, ch))
                    return false;
            }

            XFlush(display);
            return true;
        }
        finally
        {
            XCloseDisplay(display);
        }
    }

    public static bool SendReturn() => SendKeysym(XK_Return);

    public static bool SendBackspace() => SendKeysym(XK_BackSpace);

    public static bool SendSelectAll()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return false;

        var display = TryGetDisplay();
        if (display == IntPtr.Zero)
            return false;

        try
        {
            var ctrl = XStringToKeysym("Control_L");
            var a = XStringToKeysym("a");
            var ctrlCode = XKeysymToKeycode(display, ctrl);
            var aCode = XKeysymToKeycode(display, a);
            if (ctrlCode == 0 || aCode == 0)
                return false;

            XTestFakeKeyEvent(display, ctrlCode, true, 0);
            XTestFakeKeyEvent(display, aCode, true, 0);
            XTestFakeKeyEvent(display, aCode, false, 0);
            XTestFakeKeyEvent(display, ctrlCode, false, 0);
            XFlush(display);
            return true;
        }
        finally
        {
            XCloseDisplay(display);
        }
    }

    private static bool SendKeysym(uint keysym)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return false;

        var display = TryGetDisplay();
        if (display == IntPtr.Zero)
            return false;

        try
        {
            var code = XKeysymToKeycode(display, (UIntPtr)keysym);
            if (code == 0)
                return false;

            XTestFakeKeyEvent(display, code, true, 0);
            XTestFakeKeyEvent(display, code, false, 0);
            XFlush(display);
            return true;
        }
        finally
        {
            XCloseDisplay(display);
        }
    }

    private static bool SendChar(IntPtr display, char ch)
    {
        // ASCII / Latin-1 only for V1: keysym == codepoint for these ranges.
        // Arbitrary Unicode would require remapping a spare keycode via
        // XChangeKeyboardMapping (xdotool's approach) — TODO.
        if (ch > 0xFF)
            return false;

        UIntPtr keysym;
        bool needsShift = false;

        if (ch == ' ')
        {
            keysym = (UIntPtr)0x0020;
        }
        else if (ch is >= 'A' and <= 'Z')
        {
            keysym = (UIntPtr)(ch - 'A' + 'a');
            needsShift = true;
        }
        else if (ch is >= 'a' and <= 'z' or >= '0' and <= '9')
        {
            keysym = (UIntPtr)ch;
        }
        else
        {
            // Latin-1 fallback: keysym equals the codepoint for 0x20-0xFF.
            keysym = (UIntPtr)ch;
        }

        var code = XKeysymToKeycode(display, keysym);
        if (code == 0)
            return false;

        byte shiftCode = 0;
        if (needsShift)
        {
            shiftCode = XKeysymToKeycode(display, (UIntPtr)XK_Shift_L);
            if (shiftCode == 0)
                return false;
            XTestFakeKeyEvent(display, shiftCode, true, 0);
        }

        XTestFakeKeyEvent(display, code, true, 0);
        XTestFakeKeyEvent(display, code, false, 0);

        if (needsShift)
            XTestFakeKeyEvent(display, shiftCode, false, 0);

        return true;
    }

    private static IntPtr TryGetDisplay()
    {
        try
        {
            return XOpenDisplay(IntPtr.Zero);
        }
        catch (DllNotFoundException)
        {
            return IntPtr.Zero;
        }
        catch (EntryPointNotFoundException)
        {
            return IntPtr.Zero;
        }
    }

    [DllImport(LibX11)]
    private static extern IntPtr XOpenDisplay(IntPtr display);

    [DllImport(LibX11)]
    private static extern int XCloseDisplay(IntPtr display);

    [DllImport(LibX11)]
    private static extern int XFlush(IntPtr display);

    [DllImport(LibX11)]
    private static extern UIntPtr XStringToKeysym([MarshalAs(UnmanagedType.LPStr)] string str);

    [DllImport(LibX11)]
    private static extern byte XKeysymToKeycode(IntPtr display, UIntPtr keysym);

    [DllImport(LibXtst)]
    private static extern int XTestFakeKeyEvent(IntPtr display, byte keycode, [MarshalAs(UnmanagedType.I1)] bool isPress, ulong delay);

    [DllImport(LibXtst)]
    private static extern int XTestFakeMotionEvent(IntPtr display, int screen, int x, int y, ulong delay);

    [DllImport(LibXtst)]
    private static extern int XTestFakeButtonEvent(IntPtr display, uint button, [MarshalAs(UnmanagedType.I1)] bool isPress, ulong delay);
}
