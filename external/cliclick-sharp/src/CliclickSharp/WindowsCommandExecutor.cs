using System.Runtime.InteropServices;

namespace CliclickSharp;

/// <summary>Windows implementation of the mouse-oriented cliclick command subset.</summary>
/// <remarks>
/// SetCursorPos generates the actual pointer move while SendInput supplies the button transition.
/// Keeping press, move, and release as separate process invocations deliberately preserves the
/// system button state between calls, which is the contract DevFlow's decomposed drag API needs.
/// </remarks>
internal static class WindowsCommandExecutor
{
	static readonly string DebugLogPath = Path.Combine(Path.GetTempPath(), "cliclick-sharp-windows.log");
    const uint MouseMove = 0x0001;
    const uint LeftDown = 0x0002;
    const uint LeftUp = 0x0004;
    const uint RightDown = 0x0008;
    const uint RightUp = 0x0010;
    const uint MiddleDown = 0x0020;
    const uint MiddleUp = 0x0040;
    const uint Absolute = 0x8000;
    const uint VirtualDesk = 0x4000;
    const int SmXVirtualScreen = 76;
    const int SmYVirtualScreen = 77;
    const int SmCxVirtualScreen = 78;
    const int SmCyVirtualScreen = 79;

    public static int Execute(IEnumerable<string> commands)
    {
        foreach (var command in commands)
        {
            var result = ExecuteOne(command);
            Log($"command={command} ok={result}");
            if (!result)
                return 1;
        }

        return 0;
    }

    static bool ExecuteOne(string command)
    {
        var split = command.IndexOf(':');
        var op = (split < 0 ? command : command[..split]).Trim().ToLowerInvariant();
        var data = split < 0 ? string.Empty : command[(split + 1)..];
        if (op == "w" && int.TryParse(data, out var milliseconds))
        {
            Thread.Sleep(Math.Max(0, milliseconds));
            return true;
        }

        if (op == "p")
        {
            if (GetCursorPos(out var point))
                Console.WriteLine($"{point.X},{point.Y}");
            return true;
        }

        if (!TryGetPoint(data, out var x, out var y))
            return false;

        return op switch
        {
            "m" => Move(x, y),
            "dm" => MoveWhileHeld(x, y),
            "rdm" => MoveWhileHeld(x, y),
            "mdm" => MoveWhileHeld(x, y),
            "dd" => MoveAndSend(x, y, LeftDown),
            "du" => MoveAndSend(x, y, LeftUp),
            "rdd" => MoveAndSend(x, y, RightDown),
            "rdu" => MoveAndSend(x, y, RightUp),
            "mdd" => MoveAndSend(x, y, MiddleDown),
            "mdu" => MoveAndSend(x, y, MiddleUp),
            "c" => Click(x, y, LeftDown, LeftUp, 1),
            "dc" => Click(x, y, LeftDown, LeftUp, 2),
            "tc" => Click(x, y, LeftDown, LeftUp, 3),
            "rc" => Click(x, y, RightDown, RightUp, 1),
            "mc" => Click(x, y, MiddleDown, MiddleUp, 1),
            _ => Unknown(op),
        };
    }

    static bool Unknown(string op)
    {
        Console.Error.WriteLine($"Unsupported Windows cliclick command: {op}");
        return false;
    }

    static bool Click(int x, int y, uint down, uint up, int count)
    {
        if (!Move(x, y))
            return false;
        for (var i = 0; i < count; i++)
        {
            if (!Send(down) || !Send(up))
                return false;
            if (i + 1 < count)
                Thread.Sleep(50);
        }
        return true;
    }

    static bool MoveAndSend(int x, int y, uint flags) => Move(x, y) && Send(flags);
    static bool Move(int x, int y)
    {
        // SetCursorPos changes the cursor but does not reliably carry the synthetic button state
        // into WPF/WinForms drag routing. An absolute SendInput move does, so DM_* reaches the
        // target as a real mouse-move with MK_LBUTTON (or the corresponding button) still held.
        var left = GetSystemMetrics(SmXVirtualScreen);
        var top = GetSystemMetrics(SmYVirtualScreen);
        var width = Math.Max(1, GetSystemMetrics(SmCxVirtualScreen) - 1);
        var height = Math.Max(1, GetSystemMetrics(SmCyVirtualScreen) - 1);
        var input = new INPUT
        {
            type = 0,
            mi = new MOUSEINPUT
            {
                dx = (int)Math.Round((x - left) * 65535.0 / width),
                dy = (int)Math.Round((y - top) * 65535.0 / height),
                dwFlags = MouseMove | Absolute | VirtualDesk,
            },
        };
        var sent = SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
        GetCursorPos(out var actual);
        Log($"move requested=({x},{y}) normalized=({input.mi.dx},{input.mi.dy}) sent={sent} error={Marshal.GetLastWin32Error()} actual=({actual.X},{actual.Y})");
        return sent == 1;
    }

    static bool MoveWhileHeld(int x, int y)
    {
        var left = GetSystemMetrics(SmXVirtualScreen);
        var top = GetSystemMetrics(SmYVirtualScreen);
        var width = Math.Max(1, GetSystemMetrics(SmCxVirtualScreen) - 1);
        var height = Math.Max(1, GetSystemMetrics(SmCyVirtualScreen) - 1);
        var input = new INPUT
        {
            type = 0,
            mi = new MOUSEINPUT
            {
                dx = (int)Math.Round((x - left) * 65535.0 / width),
                dy = (int)Math.Round((y - top) * 65535.0 / height),
                // The down transition was sent by the preceding dd/rdd/mdd command and remains
                // in the system input state until its matching *du. Re-sending it here creates a
                // new MouseDown on every move, which resets WPF resize gestures instead of
                // extending them.
                dwFlags = MouseMove | Absolute | VirtualDesk,
            },
        };
        var sent = SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
        GetCursorPos(out var actual);
        Log($"held-move requested=({x},{y}) flags=0x{input.mi.dwFlags:X} sent={sent} error={Marshal.GetLastWin32Error()} actual=({actual.X},{actual.Y})");
        return sent == 1;
    }

    static bool Send(uint flags)
    {
        var input = new INPUT { type = 0, mi = new MOUSEINPUT { dwFlags = flags } };
        var sent = SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
        GetCursorPos(out var actual);
        Log($"button flags=0x{flags:X} sent={sent} error={Marshal.GetLastWin32Error()} actual=({actual.X},{actual.Y})");
        return sent == 1;
    }

    static void Log(string message)
    {
        try { File.AppendAllText(DebugLogPath, $"[{DateTime.Now:HH:mm:ss.fff}] pid={Environment.ProcessId} {message}{Environment.NewLine}"); }
        catch { }
    }

    static bool TryGetPoint(string value, out int x, out int y)
    {
        x = y = 0;
        if (!GetCursorPos(out var current))
            return false;
        if (value == ".")
        {
            x = current.X;
            y = current.Y;
            return true;
        }
        var parts = value.Split(',');
        return parts.Length == 2 && TryAxis(parts[0], current.X, out x) && TryAxis(parts[1], current.Y, out y);
    }

    static bool TryAxis(string text, int current, out int value)
    {
        value = 0;
        text = text.Trim();
        var absolute = text.StartsWith('=');
        if (absolute)
            text = text[1..];
        if (!int.TryParse(text, out var parsed))
            return false;
        value = !absolute && (text.StartsWith('+') || text.StartsWith('-')) ? current + parsed : parsed;
        return true;
    }

    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X; public int Y; }
    [StructLayout(LayoutKind.Sequential)] struct INPUT { public uint type; public MOUSEINPUT mi; }
    [StructLayout(LayoutKind.Sequential)] struct MOUSEINPUT { public int dx; public int dy; public uint mouseData; public uint dwFlags; public uint time; public nint dwExtraInfo; }
    [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT point);
    [DllImport("user32.dll")] static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll", SetLastError = true)] static extern uint SendInput(uint count, INPUT[] inputs, int size);
}
