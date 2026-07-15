using System.Runtime.InteropServices;
using System.Text;

namespace LeXtudio.DevFlow.Agent.Core;

/// <summary>
/// Detects and dismisses the standard Win32 dialog box (class "#32770") — the window
/// class behind <c>MessageBox.Show</c> on both WPF and WinForms, and behind most
/// native "are you sure?"-style prompts. Works by walking top-level windows owned by
/// the current process rather than any UI-framework API, so it applies uniformly to
/// every Win32-hosted DevFlow agent (WPF, WinForms, LibreWPF) without per-framework code.
/// </summary>
public static class WindowsAlertDetector
{
    private const string DialogClassName = "#32770";
    private const int WM_GETTEXT = 0x000D;
    private const int WM_GETTEXTLENGTH = 0x000E;
    private const int BM_CLICK = 0x00F5;

    public sealed class AlertButtonInfo
    {
        public string Text { get; set; } = string.Empty;
        public nint Handle { get; set; }
    }

    public sealed class AlertInfo
    {
        public string? Message { get; set; }
        public List<AlertButtonInfo> Buttons { get; set; } = new();
    }

    public static AlertInfo? Detect()
    {
        if (!OperatingSystem.IsWindows())
            return null;

        var dialog = FindDialogWindow();
        if (dialog == nint.Zero)
            return null;

        return BuildAlertInfo(dialog);
    }

    public static bool Dismiss(string? buttonLabel = null)
    {
        if (!OperatingSystem.IsWindows())
            return false;

        var dialog = FindDialogWindow();
        if (dialog == nint.Zero)
            return false;

        var buttons = CollectButtons(dialog);
        if (buttons.Count == 0)
            return false;

        var target = buttonLabel != null
            ? buttons.FirstOrDefault(b => string.Equals(b.Text, buttonLabel, StringComparison.OrdinalIgnoreCase))
            : buttons.FirstOrDefault();

        if (target == null)
            return false;

        SendMessage(target.Handle, BM_CLICK, nint.Zero, nint.Zero);
        return true;
    }

    private static AlertInfo BuildAlertInfo(nint dialog)
    {
        return new AlertInfo
        {
            Message = CollectStaticText(dialog),
            Buttons = CollectButtons(dialog)
        };
    }

    private static nint FindDialogWindow()
    {
        var currentProcessId = Environment.ProcessId;
        nint found = nint.Zero;

        EnumWindows((hWnd, _) =>
        {
            GetWindowThreadProcessId(hWnd, out var pid);
            if (pid != currentProcessId)
                return true;

            var className = GetClassName(hWnd);
            if (className != DialogClassName)
                return true;

            found = hWnd;
            return false;
        }, nint.Zero);

        return found;
    }

    private static List<AlertButtonInfo> CollectButtons(nint dialog)
    {
        var buttons = new List<AlertButtonInfo>();
        EnumChildWindows(dialog, (hWnd, _) =>
        {
            if (GetClassName(hWnd) == "Button")
            {
                var text = GetWindowText(hWnd);
                if (!string.IsNullOrWhiteSpace(text))
                    buttons.Add(new AlertButtonInfo { Text = text, Handle = hWnd });
            }
            return true;
        }, nint.Zero);
        return buttons;
    }

    private static string? CollectStaticText(nint dialog)
    {
        string? message = null;
        EnumChildWindows(dialog, (hWnd, _) =>
        {
            if (message == null && GetClassName(hWnd) == "Static")
            {
                var text = GetWindowText(hWnd);
                if (!string.IsNullOrWhiteSpace(text))
                    message = text;
            }
            return true;
        }, nint.Zero);
        return message;
    }

    private static string GetClassName(nint hWnd)
    {
        var buffer = new StringBuilder(256);
        GetClassNameNative(hWnd, buffer, buffer.Capacity);
        return buffer.ToString();
    }

    private static string GetWindowText(nint hWnd)
    {
        var length = SendMessage(hWnd, WM_GETTEXTLENGTH, nint.Zero, nint.Zero).ToInt32();
        if (length <= 0)
            return string.Empty;

        var buffer = new StringBuilder(length + 1);
        SendMessage(hWnd, WM_GETTEXT, (nint)buffer.Capacity, buffer);
        return buffer.ToString();
    }

    private delegate bool EnumWindowsProc(nint hWnd, nint lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, nint lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(nint hWndParent, EnumWindowsProc lpEnumFunc, nint lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hWnd, out int lpdwProcessId);

    [DllImport("user32.dll", EntryPoint = "GetClassNameW", CharSet = CharSet.Unicode)]
    private static extern int GetClassNameNative(nint hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll", EntryPoint = "SendMessageW", CharSet = CharSet.Unicode)]
    private static extern nint SendMessage(nint hWnd, int msg, nint wParam, nint lParam);

    [DllImport("user32.dll", EntryPoint = "SendMessageW", CharSet = CharSet.Unicode)]
    private static extern nint SendMessage(nint hWnd, int msg, nint wParam, StringBuilder lParam);
}
