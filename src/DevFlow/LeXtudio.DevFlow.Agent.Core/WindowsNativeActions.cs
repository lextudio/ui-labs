namespace LeXtudio.DevFlow.Agent.Core;

public static class WindowsNativeActions
{
    public static bool TryTap<TTarget>(TTarget target, Func<TTarget, WindowsScreenPoint?> pointResolver)
        => TryTap(() => pointResolver(target));

    public static bool TryTap(Func<WindowsScreenPoint?> pointResolver)
    {
        if (!OperatingSystem.IsWindows())
            return false;

        var point = pointResolver();
        return point is WindowsScreenPoint screenPoint
            && WindowsNativeInput.TrySendClick(screenPoint.X, screenPoint.Y);
    }

    public static bool TryRightTap<TTarget>(TTarget target, Func<TTarget, WindowsScreenPoint?> pointResolver)
        => TryRightTap(() => pointResolver(target));

    public static bool TryRightTap(Func<WindowsScreenPoint?> pointResolver)
    {
        if (!OperatingSystem.IsWindows())
            return false;

        var point = pointResolver();
        return point is WindowsScreenPoint screenPoint
            && WindowsNativeInput.TrySendRightClick(screenPoint.X, screenPoint.Y);
    }

    public static bool TryTextInput<TTarget>(TTarget target, Func<TTarget, WindowsScreenPoint?> pointResolver, string text, bool replace)
        => TryTextInput(() => pointResolver(target), text, replace);

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

    public static bool TrySpecialKey<TTarget>(TTarget target, Func<TTarget, WindowsScreenPoint?> pointResolver, ushort virtualKey)
        => TrySpecialKey(() => pointResolver(target), virtualKey);

    public static bool TrySpecialKey(Func<WindowsScreenPoint?> pointResolver, ushort virtualKey)
    {
        if (!TryTap(pointResolver))
            return false;

        return WindowsNativeInput.TrySendVirtualKey(virtualKey);
    }

    public static bool TryKeyInput<TTarget>(TTarget target, Func<TTarget, WindowsScreenPoint?> pointResolver, string normalizedKey, string? insertText)
        => TryKeyInput(() => pointResolver(target), normalizedKey, insertText);

    public static bool TryKeyInput(Func<WindowsScreenPoint?> pointResolver, string normalizedKey, string? insertText)
    {
        if (normalizedKey is "enter" or "return")
            return TrySpecialKey(pointResolver, WindowsNativeInput.VirtualKeyReturn);

        if (normalizedKey is "backspace" or "delete")
            return TrySpecialKey(pointResolver, WindowsNativeInput.VirtualKeyBackspace);

        return !string.IsNullOrEmpty(insertText) && TryTextInput(pointResolver, insertText, replace: false);
    }
}
