#if LIBREWPF
using System.Runtime.InteropServices;

namespace LeXtudio.DevFlow.Agent.Wpf;

/// <summary>
/// Captures this process's own on-screen window as a PNG on macOS. A process may capture its own windows without the Screen Recording
/// permission. WPF's RenderTargetBitmap needs the Windows-only wpfgfx, so LibreWPF has no other path. Plain P/Invoke into system
/// frameworks, so it also works under NativeAOT. A locked session yields a blank image; callers treat that as "no screenshot".
/// </summary>
internal static class MacWindowCapture
{
    private const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const string ImageIO = "/System/Library/Frameworks/ImageIO.framework/ImageIO";
    private const uint Utf8 = 0x08000100;
    private const int CFNumberSInt64 = 4;

    [StructLayout(LayoutKind.Sequential)]
    private struct CGRect { public double X, Y, Width, Height; }

    [DllImport(CoreGraphics)] private static extern IntPtr CGWindowListCopyWindowInfo(uint option, uint relativeToWindow);
    [DllImport(CoreGraphics)] private static extern IntPtr CGWindowListCreateImage(CGRect bounds, uint listOption, uint windowId, uint imageOption);
    [DllImport(CoreGraphics)] private static extern void CGImageRelease(IntPtr image);
    [DllImport(CoreGraphics)] private static extern nuint CGImageGetWidth(IntPtr image);
    [DllImport(CoreGraphics)] private static extern nuint CGImageGetHeight(IntPtr image);
    [DllImport(CoreGraphics)] private static extern IntPtr CGImageCreateWithImageInRect(IntPtr image, CGRect rect);
    [DllImport(CoreFoundation)] private static extern nint CFArrayGetCount(IntPtr array);
    [DllImport(CoreFoundation)] private static extern IntPtr CFArrayGetValueAtIndex(IntPtr array, nint index);
    [DllImport(CoreFoundation)] private static extern IntPtr CFDictionaryGetValue(IntPtr dictionary, IntPtr key);
    [DllImport(CoreFoundation)] private static extern IntPtr CFStringCreateWithCString(IntPtr allocator, [MarshalAs(UnmanagedType.LPUTF8Str)] string text, uint encoding);
    [DllImport(CoreFoundation)] private static extern byte CFNumberGetValue(IntPtr number, int type, out long value);
    [DllImport(CoreFoundation)] private static extern byte CFStringGetCString(IntPtr text, byte[] buffer, nint size, uint encoding);
    [DllImport(CoreFoundation)] private static extern IntPtr CFDataCreateMutable(IntPtr allocator, nint capacity);
    [DllImport(CoreFoundation)] private static extern nint CFDataGetLength(IntPtr data);
    [DllImport(CoreFoundation)] private static extern IntPtr CFDataGetBytePtr(IntPtr data);
    [DllImport(CoreFoundation)] private static extern void CFRelease(IntPtr value);
    [DllImport(ImageIO)] private static extern IntPtr CGImageDestinationCreateWithData(IntPtr data, IntPtr type, nuint count, IntPtr options);
    [DllImport(ImageIO)] private static extern void CGImageDestinationAddImage(IntPtr destination, IntPtr image, IntPtr properties);
    [DllImport(ImageIO)] private static extern byte CGImageDestinationFinalize(IntPtr destination);

    private static long? Number(IntPtr dictionary, string key)
    {
        var cfKey = CFStringCreateWithCString(IntPtr.Zero, key, Utf8);
        try
        {
            var value = CFDictionaryGetValue(dictionary, cfKey);
            return value != IntPtr.Zero && CFNumberGetValue(value, CFNumberSInt64, out var number) != 0 ? number : null;
        }
        finally { CFRelease(cfKey); }
    }

    private static string? Text(IntPtr dictionary, string key)
    {
        var cfKey = CFStringCreateWithCString(IntPtr.Zero, key, Utf8);
        try
        {
            var value = CFDictionaryGetValue(dictionary, cfKey);
            var buffer = new byte[512];
            if (value == IntPtr.Zero || CFStringGetCString(value, buffer, buffer.Length, Utf8) == 0)
                return null;
            return System.Text.Encoding.UTF8.GetString(buffer, 0, Math.Max(0, Array.IndexOf(buffer, (byte)0)));
        }
        finally { CFRelease(cfKey); }
    }

    /// <summary>
    /// The window number of this process's on-screen window. The process also owns helper windows (renderer surfaces), so the window
    /// titled <paramref name="title"/> wins; otherwise the largest normal-layer window is used.
    /// </summary>
    private static uint? OwnWindow(string? title)
    {
        var windows = CGWindowListCopyWindowInfo(1 /* on screen only */, 0);
        if (windows == IntPtr.Zero)
            return null;

        try
        {
            uint? best = null;
            var bestArea = 0.0;
            for (nint i = 0; i < CFArrayGetCount(windows); i++)
            {
                var info = CFArrayGetValueAtIndex(windows, i);
                if (Number(info, "kCGWindowOwnerPID") != Environment.ProcessId || Number(info, "kCGWindowLayer") != 0)
                    continue;

                var number = (uint?)Number(info, "kCGWindowNumber");
                if (number == null)
                    continue;

                if (!string.IsNullOrEmpty(title) && Text(info, "kCGWindowName") == title)
                    return number;

                var area = BoundsArea(info);
                if (area > bestArea)
                {
                    bestArea = area;
                    best = number;
                }
            }

            return best;
        }
        finally { CFRelease(windows); }
    }

    private static double BoundsArea(IntPtr info)
    {
        var cfKey = CFStringCreateWithCString(IntPtr.Zero, "kCGWindowBounds", Utf8);
        try
        {
            var bounds = CFDictionaryGetValue(info, cfKey);
            if (bounds == IntPtr.Zero)
                return 0;
            return (Number(bounds, "Width") ?? 0) * (Number(bounds, "Height") ?? 0);
        }
        finally { CFRelease(cfKey); }
    }

    /// <summary>PNG bytes of this process's window, or null when not on macOS, no window is on screen, or capture is refused.</summary>
    /// <param name="title">Title of the window to capture.</param>
    /// <param name="crop">Optional region in window points (x, y, width, height, origin top-left); scaled to the image's pixels.</param>
    /// <param name="windowSize">Window size in points; required to scale <paramref name="crop"/>.</param>
    public static byte[]? CapturePng(string? title, (double X, double Y, double Width, double Height)? crop = null,
        (double Width, double Height)? windowSize = null)
    {
        if (!OperatingSystem.IsMacOS() || OwnWindow(title) is not { } window)
            return null;

        // An infinite rect means "the window's own bounds"; BestResolution gives Retina pixels.
        var image = CGWindowListCreateImage(new CGRect { X = double.PositiveInfinity, Y = double.PositiveInfinity },
            1 << 3 /* IncludingWindow */, window, 1 << 0 | 1 << 3 /* BoundsIgnoreFraming | BestResolution */);
        if (image == IntPtr.Zero)
            return null;

        if (crop is { } c && windowSize is { Width: > 0, Height: > 0 } w)
        {
            var scaleX = CGImageGetWidth(image) / w.Width;
            var scaleY = CGImageGetHeight(image) / w.Height;
            var x = Math.Max(0, c.X * scaleX);
            var y = Math.Max(0, c.Y * scaleY);
            var cropped = CGImageCreateWithImageInRect(image, new CGRect
            {
                X = x,
                Y = y,
                Width = Math.Min(c.Width * scaleX, CGImageGetWidth(image) - x),
                Height = Math.Min(c.Height * scaleY, CGImageGetHeight(image) - y)
            });
            if (cropped != IntPtr.Zero)
            {
                CGImageRelease(image);
                image = cropped;
            }
        }

        var png = CFStringCreateWithCString(IntPtr.Zero, "public.png", Utf8);
        var data = CFDataCreateMutable(IntPtr.Zero, 0);
        var destination = IntPtr.Zero;
        try
        {
            destination = CGImageDestinationCreateWithData(data, png, 1, IntPtr.Zero);
            if (destination == IntPtr.Zero)
                return null;

            CGImageDestinationAddImage(destination, image, IntPtr.Zero);
            if (CGImageDestinationFinalize(destination) == 0)
                return null;

            var length = (int)CFDataGetLength(data);
            var bytes = new byte[length];
            Marshal.Copy(CFDataGetBytePtr(data), bytes, 0, length);
            return bytes;
        }
        finally
        {
            if (destination != IntPtr.Zero) CFRelease(destination);
            CFRelease(data);
            CFRelease(png);
            CGImageRelease(image);
        }
    }
}
#endif
