using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace LeXtudio.DevFlow.Agent.Core;

/// <summary>
/// Posts key down/up events straight to this process with <c>CGEventPostToPid</c>. The
/// session-tap path (<c>CGEventPost</c>) routes by the frontmost application, which is not
/// reliable when the target window was activated by automation: presses can be dropped, and a
/// dropped key-up leaves the key logically down so later presses are treated as repeats and never
/// reach the app. Posting to the process id delivers to this application's key window regardless
/// of which application is frontmost.
/// </summary>
internal static class MacOSKeyInput
{
    private const string ApplicationServices =
        "/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices";

    [DllImport(ApplicationServices)]
    private static extern IntPtr CGEventCreateKeyboardEvent(
        IntPtr source, ushort virtualKey, [MarshalAs(UnmanagedType.I1)] bool keyDown);

    [DllImport(ApplicationServices)]
    private static extern void CGEventPostToPid(int pid, IntPtr eventRef);

    [DllImport(ApplicationServices)]
    private static extern void CGEventPost(uint tap, IntPtr eventRef);

    [DllImport(ApplicationServices)]
    private static extern void CFRelease(IntPtr cf);

    internal static bool IsAvailable => OperatingSystem.IsMacOS();

    private static readonly HashSet<string> ModifierKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "ctrl", "control", "cmd", "command", "alt", "option", "shift", "fn",
    };

    /// <summary>macOS ANSI virtual keycodes (kVK_ANSI_*), including the modifier keycodes.</summary>
    private static readonly Dictionary<string, ushort> Keycodes = new(StringComparer.OrdinalIgnoreCase)
    {
        {"a", 0x00}, {"s", 0x01}, {"d", 0x02}, {"f", 0x03}, {"h", 0x04},
        {"g", 0x05}, {"z", 0x06}, {"x", 0x07}, {"c", 0x08}, {"v", 0x09},
        {"b", 0x0B}, {"q", 0x0C}, {"w", 0x0D}, {"e", 0x0E}, {"r", 0x0F},
        {"y", 0x10}, {"t", 0x11}, {"1", 0x12}, {"2", 0x13}, {"3", 0x14},
        {"4", 0x15}, {"6", 0x16}, {"5", 0x17}, {"9", 0x19}, {"7", 0x1A},
        {"8", 0x1C}, {"0", 0x1D}, {"o", 0x1F}, {"u", 0x20}, {"i", 0x22},
        {"p", 0x23}, {"l", 0x25}, {"j", 0x26}, {"k", 0x28}, {"n", 0x2D},
        {"m", 0x2E},
        {"ctrl", 59}, {"control", 59}, {"cmd", 55}, {"command", 55},
        {"alt", 58}, {"option", 58}, {"shift", 56}, {"fn", 63},
        {"return", 36}, {"enter", 76}, {"tab", 48}, {"space", 49},
        {"delete", 51}, {"backspace", 51}, {"fwd-delete", 117}, {"forward-delete", 117},
        {"esc", 53}, {"escape", 53},
        {"home", 115}, {"end", 119}, {"page-up", 116}, {"page-down", 121},
        {"arrow-left", 123}, {"left", 123}, {"arrow-right", 124}, {"right", 124},
        {"arrow-down", 125}, {"down", 125}, {"arrow-up", 126}, {"up", 126},
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

    /// <summary>
    /// Posts the key (or a comma-separated chord such as "shift,tab") down or up to this process.
    /// False when the platform or a key name is unsupported, so the caller can fall back.
    /// </summary>
    internal static bool TryPost(string key, bool keyDown)
    {
        if (!IsAvailable || string.IsNullOrWhiteSpace(key))
            return false;

        var parts = key.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
            return false;

        foreach (var part in parts)
        {
            if (!Keycodes.TryGetValue(part, out var keycode))
                return false;
        }

        foreach (var part in parts)
        {
            var keycode = Keycodes[part];
            var eventRef = CGEventCreateKeyboardEvent(IntPtr.Zero, keycode, keyDown);
            if (eventRef == IntPtr.Zero)
                return false;
            try
            {
                // Modifiers keep the session-tap route: it updates the session's global modifier
                // state, which native mouse events created elsewhere (CliclickSharp) read their
                // flags from, and the app is frontmost when the driver holds a modifier for a
                // pointer gesture. Posting the same modifier to the process as well makes the
                // session post suppress the process one, so the app never sees the modifier.
                // Ordinary keys post to the process only, because the session route is
                // frontmost-routed and can drop them (and the matching up).
                if (ModifierKeys.Contains(part))
                    CGEventPost(1 /* kCGSessionEventTap */, eventRef);
                else
                    CGEventPostToPid(Environment.ProcessId, eventRef);
            }
            finally
            {
                CFRelease(eventRef);
            }

            if (parts.Length > 1)
                Thread.Sleep(20);
        }

        return true;
    }
}
