namespace LeXtudio.DevFlow.Agent.Core;

public static class WindowsNativeActions
{
    public static bool TryTap(Func<WindowsScreenPoint?> pointResolver)
    {
        if (!OperatingSystem.IsWindows())
            return false;

        var point = pointResolver();
        return point is WindowsScreenPoint screenPoint
            && WindowsNativeInput.TrySendClick(screenPoint.X, screenPoint.Y);
    }

    public static bool TryTextInput(Func<WindowsScreenPoint?> pointResolver, string text, bool replace)
    {
        if (!TryTap(pointResolver))
            return false;

        if (replace && !WindowsNativeInput.TrySendChord(WindowsNativeInput.VirtualKeyControl, WindowsNativeInput.VirtualKeyA))
            return false;

        if (replace && !WindowsNativeInput.TrySendVirtualKey(WindowsNativeInput.VirtualKeyBackspace))
            return false;

        return string.IsNullOrEmpty(text) || WindowsNativeInput.TrySendUnicodeText(text);
    }

    public static bool TrySpecialKey(Func<WindowsScreenPoint?> pointResolver, ushort virtualKey)
    {
        if (!TryTap(pointResolver))
            return false;

        return WindowsNativeInput.TrySendVirtualKey(virtualKey);
    }
}
