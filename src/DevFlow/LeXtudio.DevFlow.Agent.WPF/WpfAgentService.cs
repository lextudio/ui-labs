using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.ComponentModel;
using System.Windows;
using Automation = System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Maui.DevFlow.Agent.Core;
using LeXtudio.DevFlow.Agent.Core;

namespace LeXtudio.DevFlow.Agent.WPF;

public sealed class WpfAgentService : DevFlowAgentServiceBase
{
    private readonly WpfVisualTreeWalker _treeWalker = new();
    private string _themeOverride = "system";

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
        appTheme = true,
        webview = true,
        webviewCdp = true,
        multiWindow = true
    };

    protected override Task<object?> GetThemeAsync()
        => Application.Current?.Dispatcher.InvokeAsync<object?>(BuildThemePayload).Task ?? Task.FromResult<object?>(null);

    protected override Task<object?> SetThemeAsync(string theme)
    {
        return Application.Current?.Dispatcher.InvokeAsync<object?>(() =>
        {
            var app = Application.Current;
            if (app == null)
                return null;

            var normalized = theme.Trim().ToLowerInvariant();
            if (normalized is not ("light" or "dark" or "system"))
                return null;

            TryWriteThemeProperty(app, "ThemeMode", normalized);
            _themeOverride = normalized;

            return BuildThemePayload();
        }).Task ?? Task.FromResult<object?>(null);
    }

    private object? BuildThemePayload()
    {
        var app = Application.Current;
        if (app == null)
            return null;

        var hasThemeMode = app.GetType().GetProperty("ThemeMode", BindingFlags.Public | BindingFlags.Instance) != null;
        var reportedTheme = hasThemeMode ? ReadThemeProperty(app, "ThemeMode") : "system";
        var requestedTheme = _themeOverride == "system" ? reportedTheme : _themeOverride;
        var userAppTheme = _themeOverride;
        var effectiveTheme = userAppTheme == "system" ? requestedTheme : userAppTheme;

        return new
        {
            theme = effectiveTheme,
            requestedTheme,
            userAppTheme,
            effectiveTheme,
            supportedThemes = new[] { "light", "dark", "system" }
        };
    }

    private static string ReadThemeProperty(Application app, string propertyName)
    {
        var prop = app.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        var value = prop?.GetValue(app)?.ToString();
        return value?.ToLowerInvariant() switch
        {
            "light" => "light",
            "dark" => "dark",
            "system" => "system",
            "none" => "system",
            _ => "system"
        };
    }

    private static bool TryWriteThemeProperty(Application app, string propertyName, string theme)
    {
        var prop = app.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        if (prop?.CanWrite != true)
            return false;

        var targetName = theme switch
        {
            "light" => "Light",
            "dark" => "Dark",
            _ => "System"
        };

        if (prop.PropertyType.IsEnum)
        {
            var resolved = Enum.GetNames(prop.PropertyType).FirstOrDefault(n => string.Equals(n, targetName, StringComparison.OrdinalIgnoreCase));
            if (resolved == null)
                return false;
            prop.SetValue(app, Enum.Parse(prop.PropertyType, resolved));
            return true;
        }

        var staticTheme = prop.PropertyType.GetProperty(targetName, BindingFlags.Public | BindingFlags.Static);
        if (staticTheme != null && staticTheme.PropertyType == prop.PropertyType)
        {
            prop.SetValue(app, staticTheme.GetValue(null));
            return true;
        }

        var converter = TypeDescriptor.GetConverter(prop.PropertyType);
        if (converter != null && converter.CanConvertFrom(typeof(string)))
        {
            var converted = converter.ConvertFromInvariantString(targetName);
            if (converted != null)
            {
                prop.SetValue(app, converted);
                return true;
            }
        }

        return false;
    }

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

    protected override Task<List<ElementInfo>> QueryElementsAsync(string? type = null, string? automationId = null, string? text = null)
    {
        return Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            var roots = _treeWalker.WalkTree();
            var all = new List<ElementInfo>();
            foreach (var root in roots)
                Flatten(root, all);

            return all.Where(e =>
                    (string.IsNullOrWhiteSpace(type) || string.Equals(e.Type, type, StringComparison.OrdinalIgnoreCase))
                    && (string.IsNullOrWhiteSpace(automationId) || string.Equals(e.AutomationId, automationId, StringComparison.OrdinalIgnoreCase))
                    && (string.IsNullOrWhiteSpace(text) || ((e.Text?.Contains(text, StringComparison.OrdinalIgnoreCase) == true))))
                .ToList();
        }).Task ?? Task.FromResult(new List<ElementInfo>());
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

    protected override Task<object?> TryTapResponseAsync(string elementId)
    {
        return Application.Current?.Dispatcher.InvokeAsync<object?>(() =>
        {
            var target = ResolveElementObject(elementId);
            if (target is null)
                return null;

            return ActionSimulationExecutor.Execute(
                () => target is FrameworkElement fe && WindowsNativeActions.TryTap(fe, TryGetScreenPoint) ? CreateSuccessResult(SimulationModes.Native, elementId) : null,
                () => TryInvokeOnElement(target) ? CreateSuccessResult(SimulationModes.Semantic, elementId) : null);
        }).Task ?? Task.FromResult<object?>(null);
    }

    protected override Task<bool> TryScrollAsync(string elementId, double deltaX, double deltaY)
    {
        return Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            var target = ResolveElementObject(elementId);
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

    protected override Task<bool> TryFillAsync(string elementId, string text)
    {
        return Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            var element = _treeWalker.FindElementById(elementId);
            if (element == null) return false;

            var target = _treeWalker.ResolveElementByStableId(element.Id);
            return target switch
            {
                TextBox textBox => SetText(textBox, text),
                PasswordBox passwordBox => SetPassword(passwordBox, text),
                _ => false
            };
        }).Task ?? Task.FromResult(false);
    }

    protected override Task<object?> TryFillResponseAsync(string elementId, string text)
    {
        return Application.Current?.Dispatcher.InvokeAsync<object?>(() =>
        {
            var target = ResolveElementObject(elementId);
            if (target is null)
                return null;

            return ActionSimulationExecutor.Execute(
                () => target is FrameworkElement fe && WindowsNativeActions.TryTextInput(fe, TryGetScreenPoint, text, replace: true) ? CreateSuccessResult(SimulationModes.Native, elementId, text: text) : null,
                () =>
                {
                    var success = target switch
                    {
                        TextBox textBox => SetText(textBox, text),
                        PasswordBox passwordBox => SetPassword(passwordBox, text),
                        _ => false
                    };

                    return success ? CreateSuccessResult(SimulationModes.PropertyMutation, elementId, text: text) : null;
                });
        }).Task ?? Task.FromResult<object?>(null);
    }

    protected override Task<bool> TryClearAsync(string elementId)
        => TryFillAsync(elementId, string.Empty);

    protected override Task<object?> TryClearResponseAsync(string elementId)
        => TryFillResponseAsync(elementId, string.Empty);

    protected override Task<bool> TryFocusAsync(string elementId)
    {
        return Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            var element = _treeWalker.FindElementById(elementId);
            if (element == null) return false;
            var target = _treeWalker.ResolveElementByStableId(element.Id);
            return target switch
            {
                UIElement ui => ui.Focus(),
                ContentElement ce => ce.Focus(),
                _ => false
            };
        }).Task ?? Task.FromResult(false);
    }

    protected override Task<object?> TryFocusResponseAsync(string elementId)
    {
        return Application.Current?.Dispatcher.InvokeAsync<object?>(() =>
        {
            var target = ResolveElementObject(elementId);
            if (target is null)
                return null;

            return ActionSimulationExecutor.Execute(
                () => target is FrameworkElement fe && WindowsNativeActions.TryTap(fe, TryGetScreenPoint) ? CreateSuccessResult(SimulationModes.Native, elementId) : null,
                () =>
                {
                    var success = target switch
                    {
                        UIElement ui => ui.Focus(),
                        ContentElement ce => ce.Focus(),
                        _ => false
                    };

                    return success ? CreateSuccessResult(SimulationModes.Semantic, elementId) : null;
                });
        }).Task ?? Task.FromResult<object?>(null);
    }

    protected override Task<object?> TryKeyAsync(string? elementId, string? key, string? text)
    {
        return Application.Current?.Dispatcher.InvokeAsync<object?>(() =>
        {
            var keyValue = key ?? text ?? string.Empty;
            var normalized = keyValue.Trim().ToLowerInvariant();
            var insertText = text ?? (keyValue.Length == 1 ? keyValue : null);

            if (string.IsNullOrWhiteSpace(elementId))
                return CreateSuccessResult(SimulationModes.Semantic, elementId, key: keyValue, text: text);

            var target = ResolveElementObject(elementId);
            if (target == null)
                return null;

            if (target is FrameworkElement fe && WindowsNativeActions.TryKeyInput(fe, TryGetScreenPoint, normalized, insertText))
                return CreateSuccessResult(SimulationModes.Native, elementId, key: keyValue, text: text);

            var ok = target switch
            {
                TextBox tb => ApplyTextBoxKey(tb, normalized, insertText),
                PasswordBox pb => ApplyPasswordBoxKey(pb, normalized, insertText),
                _ => false
            };

            return ok ? CreateSuccessResult(SimulationModes.PropertyMutation, elementId, key: keyValue, text: text) : null;
        }).Task ?? Task.FromResult<object?>(null);
    }

    protected override Task<bool> TryBackAsync()
    {
        return Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            var app = Application.Current;
            if (app == null)
                return false;

            var active = app.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive);
            if (active != null && active != app.MainWindow)
            {
                active.Close();
                return true;
            }

            return false;
        }).Task ?? Task.FromResult(false);
    }

    protected override async Task<object?> TryBackResponseAsync()
        => await TryBackAsync().ConfigureAwait(false) ? CreateSuccessResult(SimulationModes.Semantic) : null;

    private DependencyObject? ResolveElementObject(string elementId)
    {
        var element = _treeWalker.FindElementById(elementId);
        return element == null ? null : _treeWalker.ResolveElementByStableId(element.Id);
    }

    private static bool SetText(TextBox textBox, string text)
    {
        textBox.Text = text;
        return true;
    }

    private static void Flatten(ElementInfo element, List<ElementInfo> list)
    {
        list.Add(element);
        if (element.Children == null)
            return;
        foreach (var child in element.Children)
            Flatten(child, list);
    }

    private static bool SetPassword(PasswordBox passwordBox, string text)
    {
        passwordBox.Password = text;
        return true;
    }

    private static bool ApplyTextBoxKey(TextBox textBox, string normalizedKey, string? insertText)
    {
        if (normalizedKey is "enter" or "return")
        {
            textBox.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(textBox), 0, Key.Enter)
            { RoutedEvent = Keyboard.KeyDownEvent });
            return true;
        }

        if (normalizedKey is "backspace" or "delete")
        {
            if (!string.IsNullOrEmpty(textBox.Text))
                textBox.Text = textBox.Text[..^1];
            return true;
        }

        if (!string.IsNullOrEmpty(insertText))
        {
            textBox.Text += insertText;
            return true;
        }

        return false;
    }

    private static bool ApplyPasswordBoxKey(PasswordBox passwordBox, string normalizedKey, string? insertText)
    {
        if (normalizedKey is "backspace" or "delete")
        {
            if (!string.IsNullOrEmpty(passwordBox.Password))
                passwordBox.Password = passwordBox.Password[..^1];
            return true;
        }

        if (normalizedKey is "enter" or "return")
            return true;

        if (!string.IsNullOrEmpty(insertText))
        {
            passwordBox.Password += insertText;
            return true;
        }

        return false;
    }

    private static WindowsScreenPoint? TryGetScreenPoint(FrameworkElement element)
    {
        if (!element.IsVisible || element.ActualWidth <= 0 || element.ActualHeight <= 0)
            return null;

        try
        {
            var center = new Point(element.ActualWidth / 2d, element.ActualHeight / 2d);
            var screen = element.PointToScreen(center);
            return new WindowsScreenPoint((int)Math.Round(screen.X), (int)Math.Round(screen.Y));
        }
        catch
        {
            return null;
        }
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

        // Under ProGPU the WPF RenderTargetBitmap/PngBitmapEncoder path is not
        // available (it needs native milcore wpfgfx). Prefer ProGPU's own CPU
        // back-buffer capture when the host is present, discovered by reflection so
        // this agent keeps no hard dependency on ProGPU.Wpf. Tried before the size
        // guard because ProGPU sizes come from the back buffer, not ActualWidth.
        var progpuPng = TryCaptureViaProGpu(window);
        if (progpuPng != null)
            return progpuPng;

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

    private static byte[]? TryCaptureViaProGpu(Window window)
    {
        try
        {
            var type = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "ProGPU.Wpf")
                ?.GetType("System.Windows.Media.ProGPU.ProGpuWpfScreenshot");
            var method = type?.GetMethod("TryCapturePng", BindingFlags.Public | BindingFlags.Static);
            return method?.Invoke(null, new object?[] { window }) as byte[];
        }
        catch
        {
            return null;
        }
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

            var stream = new MemoryStream();
            if (capturePreviewAsync.Invoke(coreWebView2, [pngFormat, stream]) is not Task task)
                return null;

            if (!task.Wait(TimeSpan.FromSeconds(3)))
                return null;

            using (stream)
            {
                task.GetAwaiter().GetResult();
                return stream.Length > 0 ? stream.ToArray() : null;
            }
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
