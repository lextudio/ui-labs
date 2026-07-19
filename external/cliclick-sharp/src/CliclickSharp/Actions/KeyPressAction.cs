using CliclickSharp.Native;

namespace CliclickSharp.Actions;

public class KeyPressAction : KeyBaseAction, IAction
{
    public static string CommandShortcut => "kp";
    public static string CommandDescription => "Press a keyboard key";

    private static readonly Dictionary<string, (int NXKeyType, bool PostCGEvent)> SystemKeys = new()
    {
        {"mute", (7, false)},
        {"volume-up", (0, false)},
        {"volume-down", (1, false)},
        {"play-pause", (16, false)},
        {"play-next", (17, false)},
        {"play-previous", (18, false)},
        {"brightness-up", (2, false)},
        {"brightness-down", (3, false)},
        {"keys-light-toggle", (21, false)},
        {"keys-light-up", (20, false)},
        {"keys-light-down", (19, false)},
    };

    public override bool PerformAction(string data, ExecutionOptions options)
    {
        if (options.IsFirstAction)
            Thread.Sleep(65);

        string key = data.Trim().ToLowerInvariant();

        if (SystemKeys.TryGetValue(key, out var systemKey))
        {
            PostSystemDefinedEvent((uint)systemKey.NXKeyType);
            return true;
        }

        if (!SupportedKeycodes.TryGetValue(key, out ushort keycode))
        {
            Console.Error.WriteLine($"Unsupported key: {key}");
            return false;
        }

        PostKeyboardEvent(keycode, true);
        PostKeyboardEvent(keycode, false);

        return true;
    }

    private static void PostSystemDefinedEvent(uint nxKeyType)
    {
        IntPtr pool = ObjCRuntime.objc_autoreleasePoolPush();
        try
        {
            IntPtr eventClass = ObjCRuntime.objc_getClass("NSEvent");
            IntPtr selector = ObjCRuntime.sel_registerName("otherEventWithType:location:modifierFlags:timestamp:windowNumber:context:subtype:data1:data2:");

            // key down
            nint data1 = (int)((nxKeyType << 16) | (0xa << 8));
            IntPtr nsEvent = ObjCRuntime.objc_msgSend(
                eventClass, selector,
                (uint)14, // NSSystemDefined
                new CGPoint(0, 0),
                (ulong)0,
                0.0,
                (nint)(-1),
                IntPtr.Zero,
                (short)8,
                data1,
                (nint)0);

            IntPtr cgEvent = ObjCRuntime.objc_msgSend(nsEvent, ObjCRuntime.sel_registerName("CGEvent"));
            if (cgEvent != IntPtr.Zero)
            {
                CoreGraphics.CGEventPost(CGEventTapLocation.kCGSessionEventTap, cgEvent);
                CoreGraphics.CFRelease(cgEvent);
            }

            // key up
            data1 = (int)((nxKeyType << 16) | (0xb << 8));
            nsEvent = ObjCRuntime.objc_msgSend(
                eventClass, selector,
                (uint)14,
                new CGPoint(0, 0),
                (ulong)0,
                0.0,
                (nint)(-1),
                IntPtr.Zero,
                (short)8,
                data1,
                (nint)0);

            cgEvent = ObjCRuntime.objc_msgSend(nsEvent, ObjCRuntime.sel_registerName("CGEvent"));
            if (cgEvent != IntPtr.Zero)
            {
                CoreGraphics.CGEventPost(CGEventTapLocation.kCGSessionEventTap, cgEvent);
                CoreGraphics.CFRelease(cgEvent);
            }
        }
        finally
        {
            ObjCRuntime.objc_autoreleasePoolPop(pool);
        }
    }
}
