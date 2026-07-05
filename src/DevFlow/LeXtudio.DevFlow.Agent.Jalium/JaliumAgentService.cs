using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Jalium.UI;
using Jalium.UI.Controls;
using Microsoft.Maui.DevFlow.Agent.Core;
using LeXtudio.DevFlow.Agent.Core;

namespace LeXtudio.DevFlow.Agent.Jalium;

public sealed class JaliumAgentService : DevFlowAgentServiceBase
{
    private readonly JaliumVisualTreeWalker _treeWalker = new();

    public JaliumAgentService(AgentOptions? options = null)
        : base(options)
    {
    }

    protected override string AgentId => "LeXtudio.DevFlow.Agent";
    protected override string AgentName => "LeXtudio.DevFlow.Agent";
    protected override string FrameworkName => "jalium";
    protected override object GetCapabilities() => new
    {
        screenshots = true,
        elementScreenshots = true,
        selectorScreenshots = false,
        tap = true,
        scroll = true,
        structuredErrors = true,
        appTheme = false,
        webview = false,
        webviewCdp = false,
        multiWindow = false
    };

    protected override Task<object?> GetThemeAsync() => Task.FromResult<object?>(null);
    protected override Task<object?> SetThemeAsync(string theme) => Task.FromResult<object?>(null);

    protected override Task<string?> GetApplicationNameAsync()
    {
        if (Application.Current == null)
            return Task.FromResult<string?>(null);

        return Task.FromResult(Application.Current.GetType().Name);
    }

    protected override Task<List<ElementInfo>> BuildTreeAsync()
    {
        if (Application.Current == null)
            return Task.FromResult(new List<ElementInfo>());

        List<ElementInfo>? result = null;
        Dispatcher.MainDispatcher?.Invoke(() => result = _treeWalker.WalkTree());
        return Task.FromResult(result ?? new List<ElementInfo>());
    }

    protected override Task<ElementInfo?> FindElementAsync(string id)
    {
        if (Application.Current == null)
            return Task.FromResult<ElementInfo?>(null);

        ElementInfo? result = null;
        Dispatcher.MainDispatcher?.Invoke(() => result = _treeWalker.FindElementById(id));
        return Task.FromResult(result);
    }

    protected override Task<List<ElementInfo>> QueryElementsAsync(string? type = null, string? automationId = null, string? text = null, int maxResults = 50, int maxDepth = 24)
    {
        if (Application.Current == null)
            return Task.FromResult(new List<ElementInfo>());

        List<ElementInfo> result = [];
        Dispatcher.MainDispatcher?.Invoke(() =>
        {
            var roots = _treeWalker.WalkTree();
            var all = new List<ElementInfo>();
            foreach (var root in roots)
                Flatten(root, all);

            result = all.Where(e =>
                    (string.IsNullOrWhiteSpace(type) || string.Equals(e.Type, type, StringComparison.OrdinalIgnoreCase))
                    && (string.IsNullOrWhiteSpace(automationId) || string.Equals(e.AutomationId, automationId, StringComparison.OrdinalIgnoreCase))
                    && (string.IsNullOrWhiteSpace(text) || (e.Text?.Contains(text, StringComparison.OrdinalIgnoreCase) == true)))
                .ToList();
        });

        return Task.FromResult(result);
    }

    protected override Task<byte[]?> CaptureScreenshotAsync(string? elementId = null, string? selector = null)
    {
        if (Application.Current == null)
            return Task.FromResult<byte[]?>(null);

        byte[]? result = null;
        Dispatcher.MainDispatcher?.Invoke(() => result = CaptureScreenshotOnUiThread(elementId));
        return Task.FromResult(result);
    }

    protected override Task<bool> TryTapAsync(string elementId)
    {
        if (Application.Current == null)
            return Task.FromResult(false);

        var result = false;
        Dispatcher.MainDispatcher?.Invoke(() => result = TryTap(elementId));
        return Task.FromResult(result);
    }

    protected override Task<object?> TryTapResponseAsync(string elementId)
    {
        if (Application.Current == null)
            return Task.FromResult<object?>(null);

        object? result = null;
        Dispatcher.MainDispatcher?.Invoke(() =>
        {
            var target = _treeWalker.FindElementObjectById(elementId);
            if (target == null)
                return;

            result = ActionSimulationExecutor.Execute(
                () => WindowsNativeActions.TryTap(target, TryGetWindowsScreenPoint) ? CreateSuccessResult(SimulationModes.Native, elementId) : null,
                () => TryInvokeOnElement(target) ? CreateSuccessResult(SimulationModes.Reflection, elementId) : null);
        });
        return Task.FromResult(result);
    }

    protected override Task<bool> TryScrollAsync(string elementId, double deltaX, double deltaY)
    {
        if (Application.Current == null)
            return Task.FromResult(false);

        var result = false;
        Dispatcher.MainDispatcher?.Invoke(() => result = TryScroll(elementId, deltaX, deltaY));
        return Task.FromResult(result);
    }

    protected override Task<object?> TryScrollResponseAsync(string elementId, double deltaX, double deltaY)
    {
        if (Application.Current == null)
            return Task.FromResult<object?>(null);

        object? result = null;
        Dispatcher.MainDispatcher?.Invoke(() =>
        {
            if (TryScroll(elementId, deltaX, deltaY))
                result = CreateSuccessResult(SimulationModes.Semantic, elementId, deltaX: deltaX, deltaY: deltaY);
        });
        return Task.FromResult(result);
    }

    protected override Task<bool> TryFillAsync(string elementId, string text)
    {
        if (Application.Current == null)
            return Task.FromResult(false);

        var result = false;
        Dispatcher.MainDispatcher?.Invoke(() =>
        {
            var target = _treeWalker.FindElementObjectById(elementId);
            result = target != null && TrySetTextValue(target, text);
        });
        return Task.FromResult(result);
    }

    protected override Task<object?> TryFillResponseAsync(string elementId, string text)
    {
        if (Application.Current == null)
            return Task.FromResult<object?>(null);

        object? result = null;
        Dispatcher.MainDispatcher?.Invoke(() =>
        {
            var target = _treeWalker.FindElementObjectById(elementId);
            if (target == null)
                return;

            result = ActionSimulationExecutor.Execute(
                () => WindowsNativeActions.TryTextInput(target, TryGetWindowsScreenPoint, text, replace: true) ? CreateSuccessResult(SimulationModes.Native, elementId, text: text) : null,
                () => TrySetTextValue(target, text) ? CreateSuccessResult(SimulationModes.PropertyMutation, elementId, text: text) : null);
        });
        return Task.FromResult(result);
    }

    protected override Task<bool> TryClearAsync(string elementId) => TryFillAsync(elementId, string.Empty);
    protected override Task<object?> TryClearResponseAsync(string elementId) => TryFillResponseAsync(elementId, string.Empty);

    protected override Task<bool> TryFocusAsync(string elementId)
    {
        if (Application.Current == null)
            return Task.FromResult(false);

        var result = false;
        Dispatcher.MainDispatcher?.Invoke(() =>
        {
            var target = _treeWalker.FindElementObjectById(elementId);
            if (target is UIElement element)
            {
                result = element.Focus();
            }
        });
        return Task.FromResult(result);
    }

    protected override Task<object?> TryFocusResponseAsync(string elementId)
    {
        if (Application.Current == null)
            return Task.FromResult<object?>(null);

        object? result = null;
        Dispatcher.MainDispatcher?.Invoke(() =>
        {
            var target = _treeWalker.FindElementObjectById(elementId);
            if (target == null)
                return;

            if (WindowsNativeActions.TryTap(target, TryGetWindowsScreenPoint))
            {
                result = CreateSuccessResult(SimulationModes.Native, elementId);
                return;
            }

            if (target is UIElement element && element.Focus())
                result = CreateSuccessResult(SimulationModes.Semantic, elementId);
        });
        return Task.FromResult(result);
    }

    protected override Task<object?> TryKeyAsync(string? elementId, string? key, string? text)
    {
        if (Application.Current == null)
            return Task.FromResult<object?>(null);

        object? result = null;
        Dispatcher.MainDispatcher?.Invoke(() =>
        {
            var keyValue = key ?? text ?? string.Empty;
            var normalized = keyValue.Trim().ToLowerInvariant();
            var insertText = text ?? (keyValue.Length == 1 ? keyValue : null);

            if (string.IsNullOrWhiteSpace(elementId))
            {
                result = CreateSuccessResult(SimulationModes.Semantic, elementId, key: keyValue, text: text);
                return;
            }

            var target = _treeWalker.FindElementObjectById(elementId);
            if (target == null)
                return;

            var current = ReadStringProperty(target, "Text") ?? ReadStringProperty(target, "Value") ?? string.Empty;

            if (WindowsNativeActions.TryKeyInput(target, TryGetWindowsScreenPoint, normalized, insertText))
            {
                result = CreateSuccessResult(SimulationModes.Native, elementId, key: keyValue, text: text);
                return;
            }

            if (normalized is "backspace" or "delete")
            {
                var next = current.Length > 0 ? current[..^1] : string.Empty;
                if (TrySetTextValue(target, next))
                    result = CreateSuccessResult(SimulationModes.PropertyMutation, elementId, key: keyValue, text: text);
                return;
            }

            if (normalized is "enter" or "return")
            {
                result = CreateSuccessResult(SimulationModes.Semantic, elementId, key: keyValue, text: text);
                return;
            }

            if (!string.IsNullOrEmpty(insertText) && TrySetTextValue(target, current + insertText))
                result = CreateSuccessResult(SimulationModes.PropertyMutation, elementId, key: keyValue, text: text);
        });

        return Task.FromResult(result);
    }

    protected override Task<bool> TryBackAsync()
    {
        // Jalium tracks only MainWindow; back navigation is a no-op when there is no secondary window.
        return Task.FromResult(false);
    }

    protected override Task<object?> TryBackResponseAsync() => Task.FromResult<object?>(null);

    private static bool TrySetTextValue(object target, string text)
    {
        var type = target.GetType();
        var textProp = type.GetProperty("Text", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        if (textProp?.CanWrite == true && textProp.PropertyType == typeof(string))
        {
            textProp.SetValue(target, text);
            return true;
        }

        var valueProp = type.GetProperty("Value", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        if (valueProp?.CanWrite == true && valueProp.PropertyType == typeof(string))
        {
            valueProp.SetValue(target, text);
            return true;
        }

        return false;
    }

    private static WindowsScreenPoint? TryGetWindowsScreenPoint(object target)
    {
        if (!OperatingSystem.IsWindows() || target is not FrameworkElement fe)
            return null;

        var window = Application.Current?.MainWindow;
        if (window == null || window.Handle == 0)
            return null;

        var transform = fe.TransformToVisual(window);
        if (transform == null)
            return null;

        var topLeft = transform.Transform(new Point(0, 0));
        var pt = new POINT
        {
            X = (int)Math.Round(topLeft.X + fe.ActualWidth / 2),
            Y = (int)Math.Round(topLeft.Y + fe.ActualHeight / 2)
        };
        ClientToScreen(window.Handle, ref pt);
        return new WindowsScreenPoint(pt.X, pt.Y);
    }

    private static string? ReadStringProperty(object target, string name)
    {
        var prop = target.GetType().GetProperty(name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        return prop?.PropertyType == typeof(string) ? prop.GetValue(target) as string : null;
    }

    private bool TryTap(string elementId)
    {
        var target = _treeWalker.FindElementObjectById(elementId);
        return target != null && TryInvokeOnElement(target);
    }

    private bool TryScroll(string elementId, double deltaX, double deltaY)
    {
        var target = _treeWalker.FindElementObjectById(elementId);
        if (target == null)
            return false;

        var sv = FindScrollViewer(target);
        if (sv == null)
            return false;

        sv.ScrollToHorizontalOffset(Math.Max(0, sv.HorizontalOffset + deltaX));
        sv.ScrollToVerticalOffset(Math.Max(0, sv.VerticalOffset + deltaY));
        return true;
    }

    private static bool TryInvokeOnElement(object target)
    {
        if (target is UIElement element)
            element.Focus();

        var type = target.GetType();

        var raiseClick = type.GetMethod("RaiseClick", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        if (raiseClick != null) { try { raiseClick.Invoke(target, null); return true; } catch { } }

        var onClick = type.GetMethod("OnClick", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        if (onClick != null) { try { onClick.Invoke(target, null); return true; } catch { } }

        var clickEvent = type.GetEvent("Click", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        if (clickEvent != null)
        {
            var field = type.GetField("Click", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.FlattenHierarchy);
            var handler = field?.GetValue(target) as Delegate;
            if (handler != null) { handler.DynamicInvoke(); return true; }
        }

        return true;
    }

    private static void Flatten(ElementInfo element, List<ElementInfo> list)
    {
        list.Add(element);
        if (element.Children == null) return;
        foreach (var child in element.Children)
            Flatten(child, list);
    }

    private static ScrollViewer? FindScrollViewer(object element)
    {
        var current = element as DependencyObject;
        while (current != null)
        {
            if (current is ScrollViewer sv)
                return sv;
            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private byte[]? CaptureScreenshotOnUiThread(string? elementId)
    {
        var window = Application.Current?.MainWindow;
        if (window == null || window.Handle == 0)
            return null;

        if (!string.IsNullOrWhiteSpace(elementId))
        {
            var target = _treeWalker.FindElementObjectById(elementId);
            if (target is FrameworkElement fe)
            {
                var bytes = CaptureElementScreenshot(fe, window);
                if (bytes != null)
                    return bytes;
            }
        }

        return CaptureWindowByHandle(window.Handle);
    }

    private static byte[]? CaptureElementScreenshot(FrameworkElement element, Window window)
    {
        var windowPng = CaptureWindowByHandle(window.Handle);
        if (windowPng == null)
            return null;

        var transform = element.TransformToVisual(window);
        if (transform == null)
            return null;

        var topLeft = transform.Transform(new Point(0, 0));
        var x = (int)Math.Floor(topLeft.X);
        var y = (int)Math.Floor(topLeft.Y);
        var w = (int)Math.Ceiling(element.ActualWidth);
        var h = (int)Math.Ceiling(element.ActualHeight);
        if (w <= 0 || h <= 0)
            return null;

        return CropPng(windowPng, x, y, w, h);
    }

    private static byte[]? CaptureWindowByHandle(nint hwnd)
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
                if (windowDc == IntPtr.Zero) return null;
                try { BitBlt(hdc, 0, 0, width, height, windowDc, 0, 0, TernaryRasterOperations.SRCCOPY); }
                finally { ReleaseDC(hwnd, windowDc); }
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

    private static byte[]? CropPng(byte[] windowPng, int x, int y, int width, int height)
    {
        try
        {
            using var source = new System.Drawing.Bitmap(new MemoryStream(windowPng));
            if (x < 0 || y < 0 || x >= source.Width || y >= source.Height)
                return null;

            var cw = Math.Min(width, source.Width - x);
            var ch = Math.Min(height, source.Height - y);
            if (cw <= 0 || ch <= 0)
                return null;

            using var cropped = source.Clone(new System.Drawing.Rectangle(x, y, cw, ch), source.PixelFormat);
            using var ms = new MemoryStream();
            cropped.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            return ms.ToArray();
        }
        catch { return null; }
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
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ClientToScreen(nint hWnd, ref POINT lpPoint);

    private enum TernaryRasterOperations : uint { SRCCOPY = 0x00CC0020u }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left; public int Top; public int Right; public int Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }
}
