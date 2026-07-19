using CliclickSharp.Native;

namespace CliclickSharp.Actions;

public class TypeAction : IAction
{
    public static string CommandShortcut => "t";
    public static string CommandDescription => "Type text";

    public bool PerformAction(string data, ExecutionOptions options)
    {
        if (string.IsNullOrEmpty(data))
            return true;

        if (options.IsFirstAction)
            Thread.Sleep(65);

        foreach (char ch in data)
        {
            if (!TypeCharacter(ch))
                return false;
            Thread.Sleep(10);
        }

        return true;
    }

    private static bool TypeCharacter(char ch)
    {
        var entry = KeycodeInformer.Instance.GetKeycodeForCharacter(ch);
        if (entry == null)
        {
            Console.Error.WriteLine($"Character '{ch}' is not supported by the current keyboard layout");
            return false;
        }

        var (keycode, modifier) = entry.Value;

        // Press modifier if needed (Shift or Alt)
        if ((modifier & 0x20) != 0) // Shift
        {
            IntPtr shiftDown = CoreGraphics.CGEventCreateKeyboardEvent(IntPtr.Zero, 56, true);
            CoreGraphics.CGEventPost(CGEventTapLocation.kCGSessionEventTap, shiftDown);
            CoreGraphics.CFRelease(shiftDown);
        }
        if ((modifier & 0x40) != 0) // Alt
        {
            IntPtr altDown = CoreGraphics.CGEventCreateKeyboardEvent(IntPtr.Zero, 58, true);
            CoreGraphics.CGEventPost(CGEventTapLocation.kCGSessionEventTap, altDown);
            CoreGraphics.CFRelease(altDown);
        }

        // Key down
        IntPtr downEvent = CoreGraphics.CGEventCreateKeyboardEvent(IntPtr.Zero, keycode, true);
        CoreGraphics.CGEventPost(CGEventTapLocation.kCGSessionEventTap, downEvent);
        CoreGraphics.CFRelease(downEvent);

        // Key up
        IntPtr upEvent = CoreGraphics.CGEventCreateKeyboardEvent(IntPtr.Zero, keycode, false);
        CoreGraphics.CGEventPost(CGEventTapLocation.kCGSessionEventTap, upEvent);
        CoreGraphics.CFRelease(upEvent);

        // Release modifier
        if ((modifier & 0x40) != 0)
        {
            IntPtr altUp = CoreGraphics.CGEventCreateKeyboardEvent(IntPtr.Zero, 58, false);
            CoreGraphics.CGEventPost(CGEventTapLocation.kCGSessionEventTap, altUp);
            CoreGraphics.CFRelease(altUp);
        }
        if ((modifier & 0x20) != 0)
        {
            IntPtr shiftUp = CoreGraphics.CGEventCreateKeyboardEvent(IntPtr.Zero, 56, false);
            CoreGraphics.CGEventPost(CGEventTapLocation.kCGSessionEventTap, shiftUp);
            CoreGraphics.CFRelease(shiftUp);
        }

        return true;
    }
}
