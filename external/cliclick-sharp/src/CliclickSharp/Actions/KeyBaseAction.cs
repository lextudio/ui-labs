using CliclickSharp.Native;

namespace CliclickSharp.Actions;

public abstract class KeyBaseAction
{
    internal static readonly Dictionary<string, ushort> SupportedKeycodes = new()
    {
        // macOS ANSI virtual keycodes (kVK_ANSI_*), the same identifiers
        // CGEventCreateKeyboardEvent takes. cliclick v5.1 (and this port, originally) never
        // covered plain letters/digits - "kp"/"kd"/"ku" only ever had named special keys. Added
        // so a game/editor WASD-style camera control (or any letter/digit shortcut) can be
        // driven: a held key needs kd/ku on a non-modifier key, which is the gap this closes
        // (see KeyDownAction/KeyUpAction, which look up a key here when it is not a modifier).
        {"a", 0x00}, {"s", 0x01}, {"d", 0x02}, {"f", 0x03}, {"h", 0x04},
        {"g", 0x05}, {"z", 0x06}, {"x", 0x07}, {"c", 0x08}, {"v", 0x09},
        {"b", 0x0B}, {"q", 0x0C}, {"w", 0x0D}, {"e", 0x0E}, {"r", 0x0F},
        {"y", 0x10}, {"t", 0x11}, {"1", 0x12}, {"2", 0x13}, {"3", 0x14},
        {"4", 0x15}, {"6", 0x16}, {"5", 0x17}, {"9", 0x19}, {"7", 0x1A},
        {"8", 0x1C}, {"0", 0x1D}, {"o", 0x1F}, {"u", 0x20}, {"i", 0x22},
        {"p", 0x23}, {"l", 0x25}, {"j", 0x26}, {"k", 0x28}, {"n", 0x2D},
        {"m", 0x2E},
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
