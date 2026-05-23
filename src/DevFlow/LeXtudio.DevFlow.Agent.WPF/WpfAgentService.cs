using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using Automation = System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Maui.DevFlow.Agent.Core;
using LeXtudio.DevFlow.Agent.Core;

namespace LeXtudio.DevFlow.Agent.WPF;

public sealed class WpfAgentService : DevFlowAgentServiceBase
{
    private readonly WpfVisualTreeWalker _treeWalker = new();

    public WpfAgentService(AgentOptions? options = null)
        : base(options)
    {
    }

    protected override string AgentId => "LeXtudio.DevFlow.Agent";
    protected override string AgentName => "LeXtudio.DevFlow.Agent";
    protected override string FrameworkName => "wpf";
    protected override object GetCapabilities() => new
    {
        screenshots = true,
        elementScreenshots = true,
        selectorScreenshots = true,
        tap = true,
        scroll = true,
        structuredErrors = true,
        webview = true,
        webviewCdp = true,
        multiWindow = true
    };

    protected override Task<string?> GetApplicationNameAsync()
    {
        return Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            var app = Application.Current;
            return app?.GetType().Name;
        }).Task ?? Task.FromResult<string?>(null);
    }

    protected override Task<List<Microsoft.Maui.DevFlow.Agent.Core.ElementInfo>> BuildTreeAsync()
    {
        return Application.Current?.Dispatcher.InvokeAsync(() => _treeWalker.WalkTree()).Task
               ?? Task.FromResult(new List<Microsoft.Maui.DevFlow.Agent.Core.ElementInfo>());
    }

    protected override Task<Microsoft.Maui.DevFlow.Agent.Core.ElementInfo?> FindElementAsync(string id)
    {
        return Application.Current?.Dispatcher.InvokeAsync(() => _treeWalker.FindElementById(id)).Task
               ?? Task.FromResult<Microsoft.Maui.DevFlow.Agent.Core.ElementInfo?>(null);
    }

    protected override Task<byte[]?> CaptureScreenshotAsync(string? elementId = null, string? selector = null)
    {
        return Application.Current?.Dispatcher.InvokeAsync(() => CaptureScreenshotOnUiThread(elementId, selector)).Task
               ?? Task.FromResult<byte[]?>(null);
    }

    protected override Task<object?> GetWebViewContextsAsync()
    {
        return Application.Current?.Dispatcher.InvokeAsync<object?>(GetWebViewContextsOnUiThread).Task
               ?? Task.FromResult<object?>(new { contexts = Array.Empty<object>() });
    }

    protected override Task<byte[]?> CaptureWebViewScreenshotAsync(string? contextId = null)
    {
        return Application.Current?.Dispatcher.InvokeAsync(() => CaptureWebViewScreenshotOnUiThread(contextId)).Task
               ?? Task.FromResult<byte[]?>(null);
    }

    protected override Task<object?> SendWebViewCdpCommandAsync(string? contextId, string method, JsonElement? @params)
    {
        return Application.Current?.Dispatcher.InvokeAsync<object?>(() => SendWebViewCdpCommandOnUiThread(contextId, method, @params)).Task
               ?? Task.FromResult<object?>(null);
    }

    protected override Task<bool> TryTapAsync(string elementId)
    {
        return Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            var element = _treeWalker.FindElementById(elementId);
            if (element == null) return false;

            var target = _treeWalker.ResolveElementByStableId(element.Id);
            if (target is null) return false;

            return TryInvokeOnElement(target);
        }).Task ?? Task.FromResult(false);
    }

    protected override Task<bool> TryScrollAsync(string elementId, double deltaX, double deltaY)
    {
        return Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            var element = _treeWalker.FindElementById(elementId);
            if (element == null) return false;

            var target = _treeWalker.ResolveElementByStableId(element.Id);
            if (target is null) return false;

            var scrollViewer = FindScrollViewer(target);
            if (scrollViewer == null)
                return false;

            if (deltaX != 0)
                scrollViewer.ScrollToHorizontalOffset(Math.Max(0, scrollViewer.HorizontalOffset + deltaX));

            if (deltaY != 0)
                scrollViewer.ScrollToVerticalOffset(Math.Max(0, scrollViewer.VerticalOffset + deltaY));

            return true;
        }).Task ?? Task.FromResult(false);
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject element)
    {
        if (element is ScrollViewer sv)
            return sv;

        var current = element;
        while (current != null)
        {
            if (current is ScrollViewer found)
                return found;

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private bool TryInvokeOnElement(DependencyObject target)
    {
        try
        {
            if (target is ButtonBase buttonBase)
            {
                buttonBase.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, buttonBase));
                return true;
            }

            if (target is UIElement ui)
            {
                ui.Focus();
                return true;
            }
        }
        catch
        {
        }

        return false;
    }

    private static byte[]? CapturePrimaryWindowScreenshot()
    {
        var app = Application.Current;
        var window = app?.MainWindow ?? app?.Windows.OfType<Window>().FirstOrDefault();
        if (window == null)
            return null;

        var width = (int)Math.Ceiling(window.ActualWidth);
        var height = (int)Math.Ceiling(window.ActualHeight);
        if (width <= 0 || height <= 0)
            return null;

        var source = PresentationSource.FromVisual(window);
        var dpi = 96.0;
        if (source?.CompositionTarget != null)
            dpi = 96.0 * source.CompositionTarget.TransformToDevice.M11;

        var rtb = new RenderTargetBitmap(width, height, dpi, dpi, PixelFormats.Pbgra32);
        rtb.Render(window);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }

    private byte[]? CaptureScreenshotOnUiThread(string? elementId, string? selector)
    {
        if (string.IsNullOrWhiteSpace(elementId) && !string.IsNullOrWhiteSpace(selector))
        {
            elementId = ResolveElementIdBySelector(selector);
        }

        if (!string.IsNullOrWhiteSpace(elementId))
        {
            var element = _treeWalker.FindElementById(elementId);
            var target = element == null ? null : _treeWalker.ResolveElementByStableId(element.Id);
            var webViewBytes = TryCaptureWebView2Screenshot(target);
            if (webViewBytes != null)
                return webViewBytes;

            if (target is FrameworkElement fe)
            {
                var bytes = CaptureElementScreenshot(fe);
                if (bytes != null)
                    return bytes;
            }
        }

        return CapturePrimaryWindowScreenshot();
    }

    private string? ResolveElementIdBySelector(string selector)
    {
        var normalized = selector.Trim();
        if (normalized.StartsWith("#", StringComparison.Ordinal))
            return normalized[1..];

        var roots = _treeWalker.WalkTree();
        foreach (var root in roots)
        {
            var match = FindBySelector(root, normalized);
            if (!string.IsNullOrWhiteSpace(match))
                return match;
        }

        return null;
    }

    private static string? FindBySelector(ElementInfo element, string selector)
    {
        if (MatchesSelector(element, selector))
            return element.Id;

        if (element.Children == null)
            return null;

        foreach (var child in element.Children)
        {
            var found = FindBySelector(child, selector);
            if (!string.IsNullOrWhiteSpace(found))
                return found;
        }

        return null;
    }

    private static bool MatchesSelector(ElementInfo element, string selector)
    {
        if (string.IsNullOrWhiteSpace(selector))
            return false;

        if (selector.StartsWith("#", StringComparison.Ordinal))
            return string.Equals(element.Id, selector[1..], StringComparison.OrdinalIgnoreCase);

        if (selector.StartsWith("[name='", StringComparison.OrdinalIgnoreCase) && selector.EndsWith("']", StringComparison.Ordinal))
        {
            var value = selector[7..^2];
            if (element.NativeProperties != null
                && element.NativeProperties.TryGetValue("name", out var name)
                && !string.IsNullOrWhiteSpace(name))
                return string.Equals(name, value, StringComparison.OrdinalIgnoreCase);
        }

        if ((selector.StartsWith("[automationid='", StringComparison.OrdinalIgnoreCase)
             || selector.StartsWith("[automationId='", StringComparison.OrdinalIgnoreCase))
            && selector.EndsWith("']", StringComparison.Ordinal))
        {
            var value = selector[15..^2];
            return string.Equals(element.AutomationId, value, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private static byte[]? TryCaptureWebView2Screenshot(DependencyObject? target)
    {
        try
        {
            if (target == null)
                return null;

            var webView2Type = FindType("Microsoft.Web.WebView2.Wpf.WebView2");
            if (webView2Type == null || !webView2Type.IsInstanceOfType(target))
                return null;

            var coreWebView2 = webView2Type.GetProperty("CoreWebView2", BindingFlags.Public | BindingFlags.Instance)?.GetValue(target);
            if (coreWebView2 == null)
                return null;

            var imageFormatType = FindType("Microsoft.Web.WebView2.Core.CoreWebView2CapturePreviewImageFormat");
            if (imageFormatType == null)
                return null;

            var pngFormat = Enum.Parse(imageFormatType, "Png");
            var capturePreviewAsync = coreWebView2.GetType().GetMethod("CapturePreviewAsync", [imageFormatType, typeof(Stream)]);
            if (capturePreviewAsync == null)
                return null;

            using var stream = new MemoryStream();
            if (capturePreviewAsync.Invoke(coreWebView2, [pngFormat, stream]) is not Task task)
                return null;

            task.GetAwaiter().GetResult();
            return stream.Length > 0 ? stream.ToArray() : null;
        }
        catch
        {
            return null;
        }
    }

    private static Type? FindType(string typeName)
    {
        var type = Type.GetType(typeName, false, true);
        if (type != null)
            return type;

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            type = assembly.GetType(typeName, false, true);
            if (type != null)
                return type;
        }

        return null;
    }

    private static object GetWebViewContextsOnUiThread()
    {
        var webViewType = FindType("Microsoft.Web.WebView2.Wpf.WebView2");
        if (webViewType == null)
            return new { contexts = Array.Empty<object>() };

        var contexts = new List<object>();
        foreach (var window in Application.Current?.Windows.OfType<Window>() ?? Enumerable.Empty<Window>())
        {
            foreach (var webView in EnumerateDescendants(window).Where(d => webViewType.IsInstanceOfType(d)))
            {
                var name = (webView as FrameworkElement)?.Name;
                var automationId = (webView as FrameworkElement) != null
                    ? Automation.AutomationProperties.GetAutomationId((FrameworkElement)webView)
                    : null;
                var id = !string.IsNullOrWhiteSpace(automationId) ? automationId : name ?? $"webview-{contexts.Count + 1}";
                contexts.Add(new { id, type = "webview2", title = name ?? id });
            }
        }

        return new { contexts };
    }

    private static byte[]? CaptureWebViewScreenshotOnUiThread(string? contextId)
    {
        var webViewType = FindType("Microsoft.Web.WebView2.Wpf.WebView2");
        if (webViewType == null)
            return null;

        var webViews = Application.Current?.Windows
            .OfType<Window>()
            .SelectMany(EnumerateDescendants)
            .Where(d => webViewType.IsInstanceOfType(d))
            .ToList() ?? new List<DependencyObject>();

        if (webViews.Count == 0)
            return null;

        var target = webViews.FirstOrDefault(w =>
        {
            if (string.IsNullOrWhiteSpace(contextId))
                return true;
            var fe = w as FrameworkElement;
            var automationId = fe != null ? Automation.AutomationProperties.GetAutomationId(fe) : null;
            return string.Equals(automationId, contextId, StringComparison.OrdinalIgnoreCase)
                   || string.Equals(fe?.Name, contextId, StringComparison.OrdinalIgnoreCase);
        });

        return TryCaptureWebView2Screenshot(target);
    }

    private static IEnumerable<DependencyObject> EnumerateDescendants(DependencyObject root)
    {
        var queue = new Queue<DependencyObject>();
        queue.Enqueue(root);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            yield return current;
            var count = VisualTreeHelper.GetChildrenCount(current);
            for (var i = 0; i < count; i++)
            {
                queue.Enqueue(VisualTreeHelper.GetChild(current, i));
            }
        }
    }

    private static object? SendWebViewCdpCommandOnUiThread(string? contextId, string method, JsonElement? @params)
    {
        var webViewType = FindType("Microsoft.Web.WebView2.Wpf.WebView2");
        if (webViewType == null)
            return null;

        var target = Application.Current?.Windows
            .OfType<Window>()
            .SelectMany(EnumerateDescendants)
            .FirstOrDefault(d =>
            {
                if (!webViewType.IsInstanceOfType(d))
                    return false;
                if (string.IsNullOrWhiteSpace(contextId))
                    return true;
                var fe = d as FrameworkElement;
                var automationId = fe != null ? Automation.AutomationProperties.GetAutomationId(fe) : null;
                return string.Equals(automationId, contextId, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(fe?.Name, contextId, StringComparison.OrdinalIgnoreCase);
            });
        if (target == null)
            return null;

        var core = webViewType.GetProperty("CoreWebView2", BindingFlags.Public | BindingFlags.Instance)?.GetValue(target);
        if (core == null)
            return null;

        if (string.Equals(method, "Runtime.evaluate", StringComparison.OrdinalIgnoreCase))
        {
            if (!@params.HasValue || !@params.Value.TryGetProperty("expression", out var exprProp))
                return new { error = "Missing params.expression for Runtime.evaluate" };

            var expression = exprProp.GetString() ?? string.Empty;
            var execute = core.GetType().GetMethod("ExecuteScriptAsync", [typeof(string)]);
            if (execute == null)
                return null;

            var task = execute.Invoke(core, [expression]) as Task<string>;
            var result = task?.GetAwaiter().GetResult();
            return new { result = new { value = result } };
        }

        return new { error = $"Unsupported CDP method: {method}" };
    }

    private static byte[]? CaptureElementScreenshot(FrameworkElement element)
    {
        var width = (int)Math.Ceiling(element.ActualWidth);
        var height = (int)Math.Ceiling(element.ActualHeight);
        if (width <= 0 || height <= 0)
            return null;

        var source = PresentationSource.FromVisual(element);
        var dpiX = 96.0;
        var dpiY = 96.0;
        if (source?.CompositionTarget != null)
        {
            dpiX = 96.0 * source.CompositionTarget.TransformToDevice.M11;
            dpiY = 96.0 * source.CompositionTarget.TransformToDevice.M22;
        }

        var rtb = new RenderTargetBitmap(width, height, dpiX, dpiY, PixelFormats.Pbgra32);
        rtb.Render(element);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }
}
