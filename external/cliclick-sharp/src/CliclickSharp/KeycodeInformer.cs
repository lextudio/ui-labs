using System.Runtime.InteropServices;
using CliclickSharp.Native;

namespace CliclickSharp;

public class KeycodeInformer
{
    public static readonly KeycodeInformer Instance = new();

    private readonly Dictionary<char, (ushort keycode, ushort modifier)> _charToKeycode = new();

    private KeycodeInformer()
    {
        BuildCharacterMap();
    }

    private void BuildCharacterMap()
    {
        IntPtr inputSource = Carbon.TISCopyCurrentKeyboardInputSource();
        if (inputSource == IntPtr.Zero)
            return;

        IntPtr layoutData = Carbon.TISGetInputSourceProperty(inputSource, Carbon.kTISPropertyUnicodeKeyLayoutData);

        if (layoutData == IntPtr.Zero)
        {
            Carbon.CFRelease(inputSource);
            return;
        }

        IntPtr keyboardLayout = Carbon.CFDataGetBytePtr(layoutData);
        if (keyboardLayout == IntPtr.Zero)
        {
            Carbon.CFRelease(inputSource);
            return;
        }

        uint keyboardType = Carbon.LMGetKbdType();

        for (ushort keycode = 0; keycode <= 50; keycode++)
        {
            // Try each modifier combination: none, shift, alt, shift+alt
            ushort[] modifiers = [0, 0x20, 0x40, 0x60]; // 0, shift, alt, shift+alt

            foreach (ushort modifier in modifiers)
            {
                ushort[] chars = new ushort[4];
                uint deadKeyState = 0;
                uint actualLength = 0;

                uint result = Carbon.UCKeyTranslate(
                    keyboardLayout,
                    keycode,
                    modifier,
                    keyboardType,
                    0,
                    ref deadKeyState,
                    4,
                    out actualLength,
                    chars);

                if (result == 0 && actualLength > 0)
                {
                    char ch = (char)chars[0];
                    if (ch != 0 && !char.IsControl(ch) && !_charToKeycode.ContainsKey(ch))
                    {
                        _charToKeycode[ch] = (keycode, modifier);
                    }
                }
            }
        }

        Carbon.CFRelease(inputSource);
    }

    public (ushort keycode, ushort modifier)? GetKeycodeForCharacter(char ch)
    {
        if (_charToKeycode.TryGetValue(ch, out var entry))
            return entry;

        return null;
    }
}
