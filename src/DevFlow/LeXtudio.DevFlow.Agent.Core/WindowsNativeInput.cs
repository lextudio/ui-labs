using System.Runtime.InteropServices;
using System.Threading;

namespace LeXtudio.DevFlow.Agent.Core;

public static class WindowsNativeInput
{
    public const ushort VirtualKeyA = 0x41;
    public const ushort VirtualKeyBackspace = 0x08;
    public const ushort VirtualKeyControl = 0x11;
    public const ushort VirtualKeyReturn = 0x0D;

    public static bool TryBringToForeground(IntPtr hwnd)
    {
        if (!OperatingSystem.IsWindows() || hwnd == IntPtr.Zero)
            return false;

        // SendInput / mouse_event / SetCursorPos all deliver to the foreground
        // window's input queue. Even when the agent and the target window are
        // in the same process, Windows refuses naive SetForegroundWindow calls
        // unless we own the foreground or do the AttachThreadInput dance.
        ShowWindow(hwnd, SwShow);

        var currentThread = GetCurrentThreadId();
        var foreground = GetForegroundWindow();
        var foregroundThread = foreground == IntPtr.Zero
            ? 0u
            : GetWindowThreadProcessId(foreground, out _);

        var attached = false;
        if (foregroundThread != 0 && foregroundThread != currentThread)
            attached = AttachThreadInput(currentThread, foregroundThread, true);

        try
        {
            BringWindowToTop(hwnd);
            SetForegroundWindow(hwnd);
            SetActiveWindow(hwnd);
            SetFocus(hwnd);
        }
        finally
        {
            if (attached)
                AttachThreadInput(currentThread, foregroundThread, false);
        }

        return GetForegroundWindow() == hwnd;
    }

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

    public static bool TryMouseClick(int x, int y, int clickCount = 1)
    {
        if (!OperatingSystem.IsWindows() || clickCount < 1)
            return false;

        if (!SetCursorPos(x, y))
            return false;

        for (int c = 0; c < clickCount; c++)
        {
            mouse_event(MouseEventLeftDown, 0, 0, 0, UIntPtr.Zero);
            Thread.Sleep(50);
            mouse_event(MouseEventLeftUp, 0, 0, 0, UIntPtr.Zero);
            if (c < clickCount - 1)
                Thread.Sleep(80);
        }
        return true;
    }

    /// <summary>
    /// Injects a left-click press → drag → release gesture via the OS input
    /// queue.  Coordinates are absolute screen pixels (top-left origin).
    /// Mirrors the macOS <c>MacOSNativeInput.TryMouseDrag</c> implementation.
    /// </summary>
    public static bool TryMouseDrag(double fromX, double fromY, double toX, double toY,
        int steps = 24, int stepDelayMs = 16, int holdAfterDownMs = 200)
    {
        if (!OperatingSystem.IsWindows())
            return false;

        if (steps < 1) steps = 1;

        // Move cursor to start position
        if (!SetCursorPos((int)Math.Round(fromX), (int)Math.Round(fromY)))
            return false;
        Thread.Sleep(stepDelayMs);

        // Press left button
        mouse_event(MouseEventLeftDown, 0, 0, 0, UIntPtr.Zero);
        Thread.Sleep(holdAfterDownMs);

        // Interpolate intermediate drag points
        for (int i = 1; i <= steps; i++)
        {
            double t = (double)i / steps;
            int x = (int)Math.Round(fromX + (toX - fromX) * t);
            int y = (int)Math.Round(fromY + (toY - fromY) * t);
            SetCursorPos(x, y);
            // mouse_event with MOUSEEVENTF_MOVE to generate WM_MOUSEMOVE / WM_MOUSEDRAG
            // while the button is held.  absolute coordinates (0,0) + MOVE flag
            // moves relative to last position, so we use SetCursorPos above and
            // just post the move event so WPF's internal hit-testing picks it up.
            mouse_event(MouseEventMove, 0, 0, 0, UIntPtr.Zero);
            Thread.Sleep(stepDelayMs);
        }

        // Release
        mouse_event(MouseEventLeftUp, 0, 0, 0, UIntPtr.Zero);
        return true;
    }

    public static bool TrySendRightClick(int x, int y)
    {
        if (!OperatingSystem.IsWindows())
            return false;

        if (!SetCursorPos(x, y))
            return false;

        mouse_event(MouseEventRightDown, 0, 0, 0, UIntPtr.Zero);
        mouse_event(MouseEventRightUp, 0, 0, 0, UIntPtr.Zero);
        return true;
    }

    public static bool TryPostMouseClick(IntPtr hwnd, int clientX, int clientY)
    {
        if (!OperatingSystem.IsWindows() || hwnd == IntPtr.Zero)
            return false;

        var lParam = MakeLParam(clientX, clientY);
        // PostMessage targets the HWND's queue directly, so a hidden / non-
        // foreground window still gets the click. Move + button down + up
        // mirrors what SendInput would produce.
        return PostMessage(hwnd, WmMouseMove, IntPtr.Zero, lParam)
            && PostMessage(hwnd, WmLButtonDown, new IntPtr(MkLButton), lParam)
            && PostMessage(hwnd, WmLButtonUp, IntPtr.Zero, lParam);
    }

    public static bool TryPostVirtualKey(IntPtr hwnd, ushort virtualKey)
    {
        if (!OperatingSystem.IsWindows() || hwnd == IntPtr.Zero)
            return false;

        // lParam layout for WM_KEYDOWN/UP: repeat count (16) | scan code (8) |
        // extended (1) | … | context (29) | previous state (30) | transition (31).
        var down = new IntPtr(1);
        var up = new IntPtr(unchecked((int)0xC0000001));
        if (!PostMessage(hwnd, WmKeyDown, new IntPtr(virtualKey), down))
            return false;

        // Uno's Skia-Win32 keyboard handler peeks for the WM_CHAR that normally
        // follows WM_KEYDOWN under TranslateMessage. Posting it ourselves lets
        // controls that key off the character (TextBox Enter handling, etc.)
        // see a complete event sequence.
        var character = MapVirtualKeyToChar(virtualKey);
        if (character.HasValue)
            PostMessage(hwnd, WmChar, new IntPtr(character.Value), down);

        return PostMessage(hwnd, WmKeyUp, new IntPtr(virtualKey), up);
    }

    private static char? MapVirtualKeyToChar(ushort virtualKey) => virtualKey switch
    {
        VirtualKeyReturn => '\r',
        VirtualKeyBackspace => '\b',
        _ => null,
    };

    private static IntPtr MakeLParam(int low, int high) =>
        new((high << 16) | (low & 0xFFFF));

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

    private const uint MouseEventMove = 0x0001;
    private const uint MouseEventLeftDown = 0x0002;
    private const uint MouseEventLeftUp = 0x0004;
    private const uint MouseEventRightDown = 0x0008;
    private const uint MouseEventRightUp = 0x0010;
    private const uint InputKeyboard = 1;
    private const uint KeyEventKeyUp = 0x0002;
    private const uint KeyEventUnicode = 0x0004;

    private const uint WmMouseMove = 0x0200;
    private const uint WmLButtonDown = 0x0201;
    private const uint WmLButtonUp = 0x0202;
    private const uint WmKeyDown = 0x0100;
    private const uint WmKeyUp = 0x0101;
    private const uint WmChar = 0x0102;
    private const int MkLButton = 0x0001;
    private const int SwShow = 5;
    private const int SwShowNoActivate = 4;

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

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetActiveWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetFocus(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, [MarshalAs(UnmanagedType.Bool)] bool fAttach);
}
