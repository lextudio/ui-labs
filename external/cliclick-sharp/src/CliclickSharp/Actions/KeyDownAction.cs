using CliclickSharp.Native;

namespace CliclickSharp.Actions;

public class KeyDownAction : KeyDownUpBaseAction, IAction
{
    public static string CommandShortcut => "kd";
    public static string CommandDescription => "Press modifier key(s)";

    public bool PerformAction(string data, ExecutionOptions options)
    {
        if (options.IsFirstAction)
            Thread.Sleep(65);

        string[] keys = data.Split(',');
        for (int i = 0; i < keys.Length; i++)
        {
            string key = keys[i].Trim().ToLowerInvariant();

            if (!ModifierKeycodes.TryGetValue(key, out ushort keycode))
            {
                Console.Error.WriteLine($"Unsupported modifier key: {key}");
                return false;
            }

            IntPtr eventRef = CoreGraphics.CGEventCreateKeyboardEvent(IntPtr.Zero, keycode, true);
            CoreGraphics.CGEventPost(CGEventTapLocation.kCGSessionEventTap, eventRef);
            CoreGraphics.CFRelease(eventRef);

            if (i < keys.Length - 1)
                Thread.Sleep(20);
        }

        return true;
    }
}
