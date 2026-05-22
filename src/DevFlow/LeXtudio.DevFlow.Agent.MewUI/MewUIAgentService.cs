using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Microsoft.Maui.DevFlow.Agent.Core;
using LeXtudio.DevFlow.Agent.Core;

namespace LeXtudio.DevFlow.Agent.MewUI;

public sealed class MewUIAgentService : DevFlowAgentServiceBase
{
    private readonly MewUIVisualTreeWalker _treeWalker = new();

    public MewUIAgentService(AgentOptions? options = null)
        : base(options)
    {
    }

    protected override string AgentId => "LeXtudio.DevFlow.Agent";
    protected override string AgentName => "LeXtudio.DevFlow.Agent";
    protected override string FrameworkName => "mewui";

    protected override Task<string?> GetApplicationNameAsync()
    {
        if (!Application.IsRunning)
            return Task.FromResult<string?>(null);

        return Task.FromResult(Application.Current?.GetType().Name);
    }

    protected override Task<List<ElementInfo>> BuildTreeAsync()
    {
        if (!Application.IsRunning)
            return Task.FromResult(new List<ElementInfo>());

        var app = Application.Current;
        if (app == null)
            return Task.FromResult(new List<ElementInfo>());

        var result = default(List<ElementInfo>);
        app.Dispatcher.Invoke(() => result = _treeWalker.WalkTree());
        return Task.FromResult(result!);
    }

    protected override Task<ElementInfo?> FindElementAsync(string id)
    {
        if (!Application.IsRunning)
            return Task.FromResult<ElementInfo?>(null);

        var app = Application.Current;
        if (app == null)
            return Task.FromResult<ElementInfo?>(null);

        var result = default(ElementInfo?);
        app.Dispatcher.Invoke(() => result = _treeWalker.FindElementById(id));
        return Task.FromResult(result);
    }

    protected override Task<byte[]?> CaptureScreenshotAsync()
    {
        if (!Application.IsRunning)
            return Task.FromResult<byte[]?>(null);

        var app = Application.Current;
        if (app == null)
            return Task.FromResult<byte[]?>(null);

        var result = default(byte[]?);
        app.Dispatcher.Invoke(() => result = CapturePrimaryWindowScreenshot());
        return Task.FromResult(result);
    }

    protected override Task<bool> TryTapAsync(string elementId)
    {
        if (!Application.IsRunning)
            return Task.FromResult(false);

        var app = Application.Current;
        if (app == null)
            return Task.FromResult(false);

        var result = false;
        app.Dispatcher.Invoke(() => result = TryTap(elementId));
        return Task.FromResult(result);
    }

    protected override Task<bool> TryScrollAsync(string elementId, double deltaX, double deltaY)
    {
        if (!Application.IsRunning)
            return Task.FromResult(false);

        var app = Application.Current;
        if (app == null)
            return Task.FromResult(false);

        var result = false;
        app.Dispatcher.Invoke(() => result = TryScroll(elementId, deltaX, deltaY));
        return Task.FromResult(result);
    }

    private bool TryTap(string elementId)
    {
        var target = _treeWalker.FindElementObjectById(elementId);
        if (target == null)
            return false;

        return TryInvokeOnElement(target);
    }

    private bool TryScroll(string elementId, double deltaX, double deltaY)
    {
        var target = _treeWalker.FindElementObjectById(elementId);
        if (target == null)
            return false;

        var scrollViewer = FindScrollViewer(target);
        if (scrollViewer == null)
            return false;

        var offsetX = scrollViewer.HorizontalOffset;
        var offsetY = scrollViewer.VerticalOffset;
        scrollViewer.SetScrollOffsets(Math.Max(0, offsetX + deltaX), Math.Max(0, offsetY + deltaY));
        return true;
    }

    private static bool TryInvokeOnElement(object target)
    {
        if (target is not Control control)
            return false;

        control.Focus();
        var type = target.GetType();

        var raiseClick = type.GetMethod("RaiseClick", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        if (raiseClick != null)
        {
            try
            {
                raiseClick.Invoke(target, null);
                return true;
            }
            catch
            {
            }
        }

        var onClick = type.GetMethod("OnClick", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        if (onClick != null)
        {
            try
            {
                onClick.Invoke(target, null);
                return true;
            }
            catch
            {
            }
        }

        var clickEvent = type.GetEvent("Click", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        if (clickEvent != null)
        {
            var field = type.GetField("Click", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.FlattenHierarchy);
            if (field != null)
            {
                var handler = field.GetValue(target) as Delegate;
                if (handler != null)
                {
                    handler.DynamicInvoke();
                    return true;
                }
            }
        }

        return true;
    }

    private static ScrollViewer? FindScrollViewer(object element)
    {
        var current = element;
        while (current != null)
        {
            if (current is ScrollViewer viewer)
                return viewer;

            current = GetPropertyValue(current, "Parent");
        }

        return null;
    }

    private static object? GetPropertyValue(object target, string propertyName)
    {
        if (target == null)
            return null;

        var property = target.GetType().GetProperty(propertyName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        return property?.GetValue(target);
    }

    private static byte[]? CapturePrimaryWindowScreenshot()
    {
        var window = Application.Current.AllWindows.FirstOrDefault(w => w.Handle != 0);
        if (window == null)
            return null;

        if (OperatingSystem.IsWindows())
            return CaptureWindow(window.Handle);

        if (OperatingSystem.IsLinux())
            return CaptureX11Window(window);

        if (OperatingSystem.IsMacOS())
            return CaptureMacOSWindow(window);

        return null;
    }

    private static byte[]? CaptureX11Window(Window window)
    {
        var clientSize = window.ClientSize;
        if (clientSize.Width <= 0 || clientSize.Height <= 0)
            return null;

        var screenPosition = window.ClientToScreen(new Point(0, 0));
        var width = (int)Math.Max(1, Math.Round(clientSize.Width));
        var height = (int)Math.Max(1, Math.Round(clientSize.Height));
        return CaptureX11Region((int)Math.Round(screenPosition.X), (int)Math.Round(screenPosition.Y), width, height);
    }

    private static byte[]? CaptureMacOSWindow(Window window)
    {
        var clientSize = window.ClientSize;
        if (clientSize.Width <= 0 || clientSize.Height <= 0)
            return null;

        var screenPosition = window.ClientToScreen(new Point(0, 0));
        var bounds = new CGRect(screenPosition.X, screenPosition.Y, clientSize.Width, clientSize.Height);
        var image = CGWindowListCreateImage(bounds, CGWindowListOption.OnScreenOnly, 0, CGWindowImageOption.BestResolution);
        if (image == IntPtr.Zero)
            return null;

        try
        {
            return EncodePngFromCGImage(image);
        }
        finally
        {
            CGImageRelease(image);
        }
    }

    private static byte[]? EncodePngFromCGImage(IntPtr cgImage)
    {
        var pngType = CFStringCreateWithCString(IntPtr.Zero, "public.png", CFStringEncoding.Utf8);
        if (pngType == IntPtr.Zero)
            return null;

        var data = CFDataCreateMutable(IntPtr.Zero, 0);
        if (data == IntPtr.Zero)
        {
            CFRelease(pngType);
            return null;
        }

        var destination = CGImageDestinationCreateWithData(data, pngType, 1, IntPtr.Zero);
        if (destination == IntPtr.Zero)
        {
            CFRelease(data);
            CFRelease(pngType);
            return null;
        }

        CGImageDestinationAddImage(destination, cgImage, IntPtr.Zero);
        if (!CGImageDestinationFinalize(destination))
        {
            CFRelease(destination);
            CFRelease(data);
            CFRelease(pngType);
            return null;
        }

        var length = CFDataGetLength(data);
        var bytes = CFDataGetBytePtr(data);
        if (bytes == IntPtr.Zero || length <= 0)
        {
            CFRelease(destination);
            CFRelease(data);
            CFRelease(pngType);
            return null;
        }

        var result = new byte[(int)length];
        Marshal.Copy(bytes, result, 0, result.Length);

        CFRelease(destination);
        CFRelease(data);
        CFRelease(pngType);
        return result;
    }

    private static byte[]? CaptureX11Region(int x, int y, int width, int height)
    {
        var display = XOpenDisplay(nint.Zero);
        if (display == nint.Zero)
            return null;

        try
        {
            var screen = XDefaultScreen(display);
            var root = XRootWindow(display, screen);
            var image = XGetImage(display, root, x, y, (uint)width, (uint)height, AllPlanes, ZPixmap);
            if (image == nint.Zero)
                return null;

            try
            {
                using var bitmap = new System.Drawing.Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                var rect = new System.Drawing.Rectangle(0, 0, width, height);
                var bitmapData = bitmap.LockBits(rect, System.Drawing.Imaging.ImageLockMode.WriteOnly, bitmap.PixelFormat);
                try
                {
                    var pixelsBase = bitmapData.Scan0;
                    for (var row = 0; row < height; row++)
                    {
                        for (var col = 0; col < width; col++)
                        {
                            var pixel = XGetPixel(image, col, row);
                            var argb = unchecked((int)(0xFF000000u | pixel));
                            Marshal.WriteInt32(pixelsBase + row * bitmapData.Stride + col * 4, argb);
                        }
                    }
                }
                finally
                {
                    bitmap.UnlockBits(bitmapData);
                }

                using var ms = new MemoryStream();
                bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                return ms.ToArray();
            }
            finally
            {
                XDestroyImage(image);
            }
        }
        finally
        {
            XCloseDisplay(display);
        }
    }

    private static byte[]? CaptureWindow(nint hwnd)
    {
        if (!GetWindowRect(hwnd, out var rect))
            return null;

        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        if (width <= 0 || height <= 0)
            return null;

        using var bitmap = new System.Drawing.Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var graphics = System.Drawing.Graphics.FromImage(bitmap);
        var hdc = graphics.GetHdc();

        try
        {
            if (!PrintWindow(hwnd, hdc, 0))
            {
                var windowDc = GetWindowDC(hwnd);
                if (windowDc == IntPtr.Zero)
                    return null;

                try
                {
                    BitBlt(hdc, 0, 0, width, height, windowDc, 0, 0, TernaryRasterOperations.SRCCOPY);
                }
                finally
                {
                    ReleaseDC(hwnd, windowDc);
                }
            }
        }
        finally
        {
            graphics.ReleaseHdc(hdc);
        }

        using var ms = new MemoryStream();
        bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
        return ms.ToArray();
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(nint hWnd, out RECT lpRect);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetWindowDC(nint hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int ReleaseDC(nint hWnd, IntPtr hDC);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PrintWindow(nint hwnd, IntPtr hdcBlt, uint nFlags);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool BitBlt(IntPtr hdcDest, int nXDest, int nYDest, int nWidth, int nHeight, IntPtr hdcSrc, int nXSrc, int nYSrc, TernaryRasterOperations dwRop);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation", CharSet = CharSet.Ansi)]
    private static extern IntPtr CFStringCreateWithCString(IntPtr alloc, string cStr, CFStringEncoding encoding);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern IntPtr CFDataCreateMutable(IntPtr allocator, nint capacity);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern void CFRelease(IntPtr cf);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern IntPtr CFDataGetBytePtr(IntPtr theData);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern nint CFDataGetLength(IntPtr theData);

    [DllImport("/System/Library/Frameworks/ImageIO.framework/ImageIO")]
    private static extern IntPtr CGImageDestinationCreateWithData(IntPtr data, IntPtr type, nint count, IntPtr options);

    [DllImport("/System/Library/Frameworks/ImageIO.framework/ImageIO")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CGImageDestinationAddImage(IntPtr destination, IntPtr image, IntPtr properties);

    [DllImport("/System/Library/Frameworks/ImageIO.framework/ImageIO")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CGImageDestinationFinalize(IntPtr destination);

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern IntPtr CGWindowListCreateImage(CGRect screenBounds, CGWindowListOption listOption, uint windowID, CGWindowImageOption imageOption);

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern void CGImageRelease(IntPtr image);

    private enum CGWindowListOption : uint
    {
        OnScreenOnly = 1,
    }

    [Flags]
    private enum CGWindowImageOption : uint
    {
        BestResolution = 1 << 2,
    }

    private enum CFStringEncoding : uint
    {
        Utf8 = 0x08000100,
    }

    [DllImport("libX11.so.6")]
    private static extern nint XOpenDisplay(nint displayName);

    [DllImport("libX11.so.6")]
    private static extern int XCloseDisplay(nint display);

    [DllImport("libX11.so.6")]
    private static extern int XDefaultScreen(nint display);

    [DllImport("libX11.so.6")]
    private static extern nint XRootWindow(nint display, int screenNumber);

    [DllImport("libX11.so.6")]
    private static extern nint XGetImage(nint display, nint drawable, int x, int y, uint width, uint height, ulong planeMask, int format);

    [DllImport("libX11.so.6")]
    private static extern ulong XGetPixel(nint ximage, int x, int y);

    [DllImport("libX11.so.6")]
    private static extern int XDestroyImage(nint ximage);

    private const ulong AllPlanes = 0xFFFFFFFFFFFFFFFFul;
    private const int ZPixmap = 2;

    private enum TernaryRasterOperations : uint
    {
        SRCCOPY = 0x00CC0020u,
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct CGRect
    {
        public readonly double X;
        public readonly double Y;
        public readonly double Width;
        public readonly double Height;

        public CGRect(double x, double y, double width, double height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }
    }
}
