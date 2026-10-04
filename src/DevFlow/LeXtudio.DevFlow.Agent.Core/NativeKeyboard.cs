namespace LeXtudio.DevFlow.Agent.Core;

/// <summary>
/// Sends a key or key chord - "Escape", "Tab", "Up", "F4", "a", "Ctrl+Tab", "Ctrl+Shift+Tab" - to the
/// window that has the keyboard focus, with the platform's native input: SendInput on Windows, XTest on
/// Linux (X11/XWayland) and cliclick on macOS. This is what a key action without a target element does.
/// </summary>
public static class NativeKeyboard
{
    private sealed record KeyNames(ushort VirtualKey, string X11Keysym, string? Cliclick);

    private static readonly Dictionary<string, KeyNames> Modifiers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ctrl"] = new(0x11, "Control_L", "ctrl"),
        ["control"] = new(0x11, "Control_L", "ctrl"),
        ["shift"] = new(0x10, "Shift_L", "shift"),
        ["alt"] = new(0x12, "Alt_L", "alt"),
        ["win"] = new(0x5B, "Super_L", "cmd"),
        ["meta"] = new(0x5B, "Super_L", "cmd"),
        ["cmd"] = new(0x5B, "Super_L", "cmd"),
    };

    private static readonly Dictionary<string, KeyNames> Keys = CreateKeys();

    public static bool IsAvailable
        => OperatingSystem.IsWindows()
            || (OperatingSystem.IsLinux() && LinuxNativeInput.IsAvailable)
            || (OperatingSystem.IsMacOS() && CliclickInput.IsAvailable);

    /// <summary>Whether a key action's key names a key or chord this class can send.</summary>
    public static bool CanSend(string? chord) => TryParse(chord, out _, out _);

    public static bool TrySendChord(string chord)
    {
        if (!TryParse(chord, out var modifiers, out var key))
            return false;

        if (OperatingSystem.IsWindows())
            return WindowsNativeInput.TrySendChord(modifiers.Select(m => m.VirtualKey).Append(key.VirtualKey).ToArray());

        if (OperatingSystem.IsLinux())
            return LinuxNativeInput.TrySendKeysymChord(modifiers.Select(m => m.X11Keysym).Append(key.X11Keysym).ToArray());

        if (OperatingSystem.IsMacOS() && CliclickInput.IsAvailable && key.Cliclick != null)
        {
            foreach (var modifier in modifiers)
            {
                if (!CliclickInput.TryKeyDown(modifier.Cliclick!))
                    return false;
            }

            try
            {
                return CliclickInput.TryKeyPress(key.Cliclick);
            }
            finally
            {
                for (var i = modifiers.Count - 1; i >= 0; i--)
                    CliclickInput.TryKeyUp(modifiers[i].Cliclick!);
            }
        }

        return false;
    }

    private static bool TryParse(string? chord, out List<KeyNames> modifiers, out KeyNames key)
    {
        modifiers = new List<KeyNames>();
        key = null!;
        if (string.IsNullOrWhiteSpace(chord))
            return false;

        var parts = chord.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return false;

        for (var i = 0; i < parts.Length - 1; i++)
        {
            if (!Modifiers.TryGetValue(parts[i], out var modifier))
                return false;
            modifiers.Add(modifier);
        }

        return Keys.TryGetValue(parts[^1], out key!);
    }

    private static Dictionary<string, KeyNames> CreateKeys()
    {
        var keys = new Dictionary<string, KeyNames>(StringComparer.OrdinalIgnoreCase)
        {
            ["escape"] = new(0x1B, "Escape", "esc"),
            ["esc"] = new(0x1B, "Escape", "esc"),
            ["tab"] = new(0x09, "Tab", "tab"),
            ["enter"] = new(0x0D, "Return", "return"),
            ["return"] = new(0x0D, "Return", "return"),
            ["space"] = new(0x20, "space", "space"),
            ["backspace"] = new(0x08, "BackSpace", "delete"),
            ["delete"] = new(0x2E, "Delete", "fwd-delete"),
            ["del"] = new(0x2E, "Delete", "fwd-delete"),
            ["up"] = new(0x26, "Up", "arrow-up"),
            ["down"] = new(0x28, "Down", "arrow-down"),
            ["left"] = new(0x25, "Left", "arrow-left"),
            ["right"] = new(0x27, "Right", "arrow-right"),
            ["home"] = new(0x24, "Home", "home"),
            ["end"] = new(0x23, "End", "end"),
            ["pageup"] = new(0x21, "Prior", "page-up"),
            ["pagedown"] = new(0x22, "Next", "page-down"),
        };

        for (var i = 1; i <= 12; i++)
            keys["f" + i] = new((ushort)(0x70 + i - 1), "F" + i, "f" + i);

        // Letters and digits have no cliclick key name; on macOS they are only sent as text.
        for (var c = 'a'; c <= 'z'; c++)
            keys[c.ToString()] = new((ushort)char.ToUpperInvariant(c), c.ToString(), null);
        for (var c = '0'; c <= '9'; c++)
            keys[c.ToString()] = new(c, c.ToString(), null);

        return keys;
    }
}