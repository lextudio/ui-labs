using CliclickSharp.Native;

namespace CliclickSharp.Actions;

public abstract class KeyBaseAction
{
    protected static readonly Dictionary<string, ushort> SupportedKeycodes = new()
    {
        {"return", 36},
        {"enter", 76},
        {"tab", 48},
        {"space", 49},
        {"delete", 51},
        {"fwd-delete", 117},
        {"esc", 53},
        {"escape", 53},
        {"home", 115},
        {"end", 119},
        {"page-up", 116},
        {"page-down", 121},
        {"arrow-left", 123},
        {"arrow-right", 124},
        {"arrow-down", 125},
        {"arrow-up", 126},
        {"f1", 122}, {"f2", 120}, {"f3", 99}, {"f4", 118},
        {"f5", 96}, {"f6", 97}, {"f7", 98}, {"f8", 100},
        {"f9", 101}, {"f10", 109}, {"f11", 103}, {"f12", 111},
        {"f13", 105}, {"f14", 107}, {"f15", 113}, {"f16", 106},
        {"num-0", 82}, {"num-1", 83}, {"num-2", 84}, {"num-3", 85},
        {"num-4", 86}, {"num-5", 87}, {"num-6", 88}, {"num-7", 89},
        {"num-8", 91}, {"num-9", 92},
        {"num-clear", 71}, {"num-enter", 76}, {"num-divide", 75},
        {"num-multiply", 67}, {"num-minus", 78}, {"num-plus", 69},
        {"num-equals", 81},
    };

    protected static bool PostKeyboardEvent(ushort keycode, bool keyDown)
    {
        IntPtr eventRef = CoreGraphics.CGEventCreateKeyboardEvent(IntPtr.Zero, keycode, keyDown);
        if (eventRef == IntPtr.Zero)
            return false;
        CoreGraphics.CGEventPost(CGEventTapLocation.kCGSessionEventTap, eventRef);
        CoreGraphics.CFRelease(eventRef);
        return true;
    }

    public abstract bool PerformAction(string data, ExecutionOptions options);
}
