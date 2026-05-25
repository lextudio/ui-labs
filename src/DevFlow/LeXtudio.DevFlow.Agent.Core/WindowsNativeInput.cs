using System.Runtime.InteropServices;

namespace LeXtudio.DevFlow.Agent.Core;

public static class WindowsNativeInput
{
    public const ushort VirtualKeyA = 0x41;
    public const ushort VirtualKeyBackspace = 0x08;
    public const ushort VirtualKeyControl = 0x11;
    public const ushort VirtualKeyReturn = 0x0D;

    public static bool TrySendClick(int x, int y)
    {
        if (!OperatingSystem.IsWindows())
            return false;

        if (!SetCursorPos(x, y))
            return false;

        mouse_event(MouseEventLeftDown, 0, 0, 0, UIntPtr.Zero);
        mouse_event(MouseEventLeftUp, 0, 0, 0, UIntPtr.Zero);
        return true;
    }

    public static bool TrySendChord(params ushort[] keys)
    {
        if (!OperatingSystem.IsWindows())
            return false;

        foreach (var key in keys)
        {
            if (!TrySendSingleInput(CreateVirtualKeyInput(key, keyUp: false)))
                return false;
        }

        for (var i = keys.Length - 1; i >= 0; i--)
        {
            if (!TrySendSingleInput(CreateVirtualKeyInput(keys[i], keyUp: true)))
                return false;
        }

        return true;
    }

    public static bool TrySendVirtualKey(ushort key)
    {
        if (!OperatingSystem.IsWindows())
            return false;

        return TrySendSingleInput(CreateVirtualKeyInput(key, keyUp: false))
            && TrySendSingleInput(CreateVirtualKeyInput(key, keyUp: true));
    }

    public static bool TrySendUnicodeText(string text)
    {
        if (!OperatingSystem.IsWindows())
            return false;

        foreach (var ch in text)
        {
            if (!TrySendSingleInput(CreateUnicodeInput(ch, keyUp: false))
                || !TrySendSingleInput(CreateUnicodeInput(ch, keyUp: true)))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TrySendSingleInput(INPUT input)
    {
        var inputs = new[] { input };
        return SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>()) == inputs.Length;
    }

    private static INPUT CreateUnicodeInput(char ch, bool keyUp)
    {
        return new INPUT
        {
            type = InputKeyboard,
            U = new INPUTUNION
            {
                ki = new KEYBDINPUT
                {
                    wScan = ch,
                    dwFlags = KeyEventUnicode | (keyUp ? KeyEventKeyUp : 0),
                }
            }
        };
    }

    private static INPUT CreateVirtualKeyInput(ushort key, bool keyUp)
    {
        return new INPUT
        {
            type = InputKeyboard,
            U = new INPUTUNION
            {
                ki = new KEYBDINPUT
                {
                    wVk = key,
                    dwFlags = keyUp ? KeyEventKeyUp : 0,
                }
            }
        };
    }

    private const uint MouseEventLeftDown = 0x0002;
    private const uint MouseEventLeftUp = 0x0004;
    private const uint InputKeyboard = 1;
    private const uint KeyEventKeyUp = 0x0002;
    private const uint KeyEventUnicode = 0x0004;

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public INPUTUNION U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct INPUTUNION
    {
        [FieldOffset(0)]
        public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll", SetLastError = false)]
    private static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);
}
