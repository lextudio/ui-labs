using CliclickSharp.Native;

namespace CliclickSharp.Actions;

public class KeyDownAction : KeyDownUpBaseAction, IAction
{
    public static string CommandShortcut => "kd";
    // Originally "modifier key(s)" only; now also accepts any key from KeyBaseAction's
    // SupportedKeycodes (letters, digits, arrows, ...), so a non-modifier key can be held down
    // across several separate cliclick-sharp invocations - e.g. a game's WASD movement, which
    // needs the key down for multiple frames, not a single kp: press-and-release.
    public static string CommandDescription => "Press and hold a modifier or ordinary key";

    public bool PerformAction(string data, ExecutionOptions options)
    {
        if (options.IsFirstAction)
            Thread.Sleep(65);

        string[] keys = data.Split(',');
        for (int i = 0; i < keys.Length; i++)
        {
            string key = keys[i].Trim().ToLowerInvariant();

            if (!ModifierKeycodes.TryGetValue(key, out ushort keycode)
                && !KeyBaseAction.SupportedKeycodes.TryGetValue(key, out keycode))
            {
                Console.Error.WriteLine($"Unsupported key: {key}");
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
