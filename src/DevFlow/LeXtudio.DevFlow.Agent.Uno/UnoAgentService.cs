using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Versioning;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using LeXtudio.DevFlow.Agent.Core;
using Microsoft.Maui.DevFlow.Agent.Core;

namespace LeXtudio.DevFlow.Agent.Uno;

public sealed class UnoAgentService : DevFlowAgentServiceBase
{
    private readonly UnoVisualTreeWalker _treeWalker = new();
    private readonly object? _dispatcherQueue;
    private string _themeOverride = "system";

    public UnoAgentService(AgentOptions? options = null)
        : base(options)
    {
        _dispatcherQueue = GetDispatcherQueue();
    }

    protected override string AgentId => "LeXtudio.DevFlow.Agent";
    protected override string AgentName => "LeXtudio.DevFlow.Agent";
    protected override string FrameworkName => "uno";
    protected override object GetCapabilities() => new
    {
        screenshots = true,
        elementScreenshots = true,
        selectorScreenshots = false,
        tap = true,
        scroll = true,
        structuredErrors = true,
        appTheme = true,
        webview = true,
        webviewCdp = true,
        multiWindow = true
    };

    protected override Task<string?> GetApplicationNameAsync()
    {
        var appType = FindType(
            "Microsoft.UI.Xaml.Application",
            "Windows.UI.Xaml.Application");

        if (appType == null)
            return Task.FromResult<string?>("UnoApplication");

        var current = appType.GetProperty("Current", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
        return Task.FromResult<string?>(current?.GetType().FullName ?? "UnoApplication");
    }

    private static Type? FindType(params string[] typeNames)
    {
        foreach (var typeName in typeNames)
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
        }

        return null;
    }

    protected override Task<List<ElementInfo>> BuildTreeAsync()
    {
        return InvokeOnUiThreadAsync(() => _treeWalker.WalkTree());
    }

    protected override Task<ElementInfo?> FindElementAsync(string id)
    {
        return InvokeOnUiThreadAsync(() => _treeWalker.FindElementById(id));
    }

    protected override Task<List<ElementInfo>> QueryElementsAsync(string? type = null, string? automationId = null, string? text = null)
    {
        return InvokeOnUiThreadAsync(() =>
        {
            var roots = _treeWalker.WalkTree();
            var all = new List<ElementInfo>();
            foreach (var root in roots)
                Flatten(root, all);

            return all.Where(e =>
                    (string.IsNullOrWhiteSpace(type) || string.Equals(e.Type, type, StringComparison.OrdinalIgnoreCase))
                    && (string.IsNullOrWhiteSpace(automationId) || string.Equals(e.AutomationId, automationId, StringComparison.OrdinalIgnoreCase))
                    && (string.IsNullOrWhiteSpace(text) || (e.Text?.Contains(text, StringComparison.OrdinalIgnoreCase) == true)))
                .ToList();
        });
    }

    protected override Task<byte[]?> CaptureScreenshotAsync(string? elementId = null, string? selector = null)
    {
        return InvokeOnUiThreadAsync(() => CaptureScreenshotOnUiThreadAsync(elementId));
    }

    protected override Task<bool> TryTapAsync(string elementId)
    {
        return InvokeOnUiThreadAsync(() =>
        {
            var target = _treeWalker.FindElementObjectById(elementId);
            if (target == null)
                return false;

            if (TryExecuteCommand(target))
                return true;

            if (TryInvokeAutomationPattern(target))
                return true;

            if (TryInvokeOnClick(target))
                return true;

            return false;
        });
    }

    protected override Task<object?> TryTapResponseAsync(string elementId)
    {
        return InvokeOnUiThreadAsync<object?>(() =>
        {
            var target = _treeWalker.FindElementObjectById(elementId);
            if (target == null)
                return null;

            if (!IsElementEnabled(target))
                return null;

            return ActionSimulationExecutor.Execute(
                () => TryNativeTap(target) ? CreateSuccessResult(SimulationModes.Native, elementId) : null,
                () => TryExecuteCommand(target) ? CreateSuccessResult(SimulationModes.Semantic, elementId) : null,
                () => TryInvokeAutomationPattern(target) ? CreateSuccessResult(SimulationModes.Reflection, elementId) : null,
                () => TryInvokeOnClick(target) ? CreateSuccessResult(SimulationModes.Reflection, elementId) : null);
        });
    }

    protected override Task<bool> TryScrollAsync(string elementId, double deltaX, double deltaY)
    {
        return InvokeOnUiThreadAsync(() =>
        {
            var target = _treeWalker.FindElementObjectById(elementId);
            if (target == null)
                return false;

            var scrollViewer = FindScrollViewer(target);
            if (scrollViewer == null)
                return false;

            if (TryScroll(scrollViewer, deltaX, deltaY))
                return true;

            return false;
        });
    }

    protected override Task<object?> TryScrollResponseAsync(string elementId, double deltaX, double deltaY)
    {
        return InvokeOnUiThreadAsync<object?>(() =>
        {
            var target = _treeWalker.FindElementObjectById(elementId);
            if (target == null)
                return null;

            if (!IsElementEnabled(target))
                return null;

            var scrollViewer = FindScrollViewer(target);
            if (scrollViewer == null)
                return null;

            return TryScroll(scrollViewer, deltaX, deltaY)
                ? CreateSuccessResult(SimulationModes.Semantic, elementId, deltaX: deltaX, deltaY: deltaY)
                : null;
        });
    }

    protected override Task<bool> TryFillAsync(string elementId, string text)
    {
        return InvokeOnUiThreadAsync(() =>
        {
            var target = _treeWalker.FindElementObjectById(elementId);
            if (target == null)
                return false;

            return TrySetTextValue(target, text);
        });
    }

    protected override Task<object?> TryFillResponseAsync(string elementId, string text)
    {
        return InvokeOnUiThreadAsync<object?>(() =>
        {
            var target = _treeWalker.FindElementObjectById(elementId);
            if (target == null)
                return null;

            if (!IsElementEnabled(target))
                return null;

            return ActionSimulationExecutor.Execute(
                () => TryNativeTextInput(target, text, replace: true) ? CreateSuccessResult(SimulationModes.Native, elementId, text: text) : null,
                () =>
                {
                    TryFocusElement(target);
                    return TrySetTextValue(target, text)
                        ? CreateSuccessResult(SimulationModes.PropertyMutation, elementId, text: text)
                        : null;
                });
        });
    }

    protected override Task<bool> TryClearAsync(string elementId)
    {
        return TryFillAsync(elementId, string.Empty);
    }

    protected override Task<object?> TryClearResponseAsync(string elementId)
    {
        return TryFillResponseAsync(elementId, string.Empty);
    }

    protected override Task<bool> TryFocusAsync(string elementId)
    {
        return InvokeOnUiThreadAsync(() =>
        {
            var target = _treeWalker.FindElementObjectById(elementId);
            if (target == null)
                return false;

            var targetType = target.GetType();

            // First try parameterless Focus(), available on some XAML targets.
            var focusNoArgs = targetType.GetMethod("Focus", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
            if (focusNoArgs != null)
            {
                var result = focusNoArgs.Invoke(target, null);
                if (result is bool focused)
                    return focused;

                return true;
            }

            // Fall back to Focus(FocusState) for platforms where only that overload exists.
            var focusWithState = targetType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(m =>
                {
                    if (!string.Equals(m.Name, "Focus", StringComparison.Ordinal))
                        return false;

                    var parameters = m.GetParameters();
                    return parameters.Length == 1 && parameters[0].ParameterType.IsEnum;
                });

            if (focusWithState == null)
                return false;

            var enumType = focusWithState.GetParameters()[0].ParameterType;
            var programmatic = Enum.GetNames(enumType).FirstOrDefault(n => string.Equals(n, "Programmatic", StringComparison.OrdinalIgnoreCase));
            var state = programmatic != null ? Enum.Parse(enumType, programmatic) : Enum.ToObject(enumType, 0);

            var focusedResult = focusWithState.Invoke(target, new[] { state });
            return focusedResult is bool focusedBool ? focusedBool : true;
        });
    }

    protected override Task<object?> TryFocusResponseAsync(string elementId)
    {
        return InvokeOnUiThreadAsync<object?>(() =>
        {
            var target = _treeWalker.FindElementObjectById(elementId);
            if (target == null)
                return null;

            if (!IsElementEnabled(target))
                return null;

            var targetType = target.GetType();
            var focusNoArgs = targetType.GetMethod("Focus", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
            if (focusNoArgs != null)
            {
                var result = focusNoArgs.Invoke(target, null);
                var hasFocusFromNoArgCall = result is bool noArgFocusResult ? noArgFocusResult : true;
                return hasFocusFromNoArgCall ? CreateSuccessResult(SimulationModes.Semantic, elementId) : null;
            }

            var focusWithState = targetType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(m =>
                {
                    if (!string.Equals(m.Name, "Focus", StringComparison.Ordinal))
                        return false;

                    var parameters = m.GetParameters();
                    return parameters.Length == 1 && parameters[0].ParameterType.IsEnum;
                });

            if (focusWithState == null)
                return null;

            var enumType = focusWithState.GetParameters()[0].ParameterType;
            var programmatic = Enum.GetNames(enumType).FirstOrDefault(n => string.Equals(n, "Programmatic", StringComparison.OrdinalIgnoreCase));
            var state = programmatic != null ? Enum.Parse(enumType, programmatic) : Enum.ToObject(enumType, 0);
            var focusedResult = focusWithState.Invoke(target, new[] { state });
            var hasFocusFromStateCall = focusedResult is bool stateFocusResult ? stateFocusResult : true;
            return hasFocusFromStateCall ? CreateSuccessResult(SimulationModes.Semantic, elementId) : null;
        });
    }

    protected override Task<object?> TryKeyAsync(string? elementId, string? key, string? text)
    {
        return InvokeOnUiThreadAsync<object?>(() =>
        {
            var keyValue = key ?? text ?? string.Empty;
            var normalized = keyValue.Trim().ToLowerInvariant();
            var insertText = text ?? (keyValue.Length == 1 ? keyValue : null);

            if (string.IsNullOrWhiteSpace(elementId))
                return CreateSuccessResult(SimulationModes.Semantic, elementId, key: keyValue, text: text);

            var target = _treeWalker.FindElementObjectById(elementId);
            if (target == null)
                return null;

            if (!IsElementEnabled(target))
                return null;

            if (TryNativeKeyInput(target, normalized, insertText))
                return CreateSuccessResult(SimulationModes.Native, elementId, key: keyValue, text: text);

            TryFocusElement(target);

            var current = ReadStringProperty(target, "Text") ?? ReadStringProperty(target, "Value") ?? string.Empty;

            if (normalized is "backspace" or "delete")
            {
                var next = current.Length > 0 ? current[..^1] : string.Empty;
                return TrySetTextValue(target, next) ? CreateSuccessResult(SimulationModes.PropertyMutation, elementId, key: keyValue, text: text) : null;
            }

            if (normalized is "enter" or "return")
                return CreateSuccessResult(SimulationModes.Semantic, elementId, key: keyValue, text: text);

            if (!string.IsNullOrEmpty(insertText))
            {
                var next = current + insertText;
                return TrySetTextValue(target, next) ? CreateSuccessResult(SimulationModes.PropertyMutation, elementId, key: keyValue, text: text) : null;
            }

            return null;
        });
    }

    protected override Task<bool> TryBackAsync()
    {
        return InvokeOnUiThreadAsync(() =>
        {
            var root = GetRootVisual();
            if (root == null)
                return false;

            var frame = FindAncestorOrSelfByTypeName(root, "Frame");
            if (frame == null)
                return false;

            var canGoBack = frame.GetType().GetProperty("CanGoBack", BindingFlags.Public | BindingFlags.Instance)?.GetValue(frame) as bool?;
            if (canGoBack != true)
                return false;

            var goBack = frame.GetType().GetMethod("GoBack", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
            goBack?.Invoke(frame, null);
            return true;
        });
    }

    protected override Task<object?> TryBackResponseAsync()
    {
        return InvokeOnUiThreadAsync<object?>(() =>
        {
            var root = GetRootVisual();
            if (root == null)
                return null;

            var frame = FindAncestorOrSelfByTypeName(root, "Frame");
            if (frame == null)
                return null;

            var canGoBack = frame.GetType().GetProperty("CanGoBack", BindingFlags.Public | BindingFlags.Instance)?.GetValue(frame) as bool?;
            if (canGoBack != true)
                return null;

            var goBack = frame.GetType().GetMethod("GoBack", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
            goBack?.Invoke(frame, null);
            return CreateSuccessResult(SimulationModes.Semantic);
        });
    }

    protected override Task<object?> GetThemeAsync()
    {
        return InvokeOnUiThreadAsync(() =>
        {
            var app = GetCurrentApplicationObject();
            if (app == null)
                return (object?)null;

            var requestedTheme = ReadThemeProperty(app, "RequestedTheme");
            var userAppTheme = ReadThemeProperty(app, "UserAppTheme");
            if (_themeOverride != "system")
                userAppTheme = _themeOverride;
            var effectiveTheme = string.Equals(userAppTheme, "system", StringComparison.OrdinalIgnoreCase)
                ? requestedTheme
                : userAppTheme;

            return (object?)new
            {
                theme = effectiveTheme,
                requestedTheme,
                userAppTheme,
                effectiveTheme,
                supportedThemes = new[] { "light", "dark", "system" }
            };
        });
    }

    protected override Task<object?> SetThemeAsync(string theme)
    {
        return InvokeOnUiThreadAsync(() =>
        {
            if (!TryParseTheme(theme, out var normalized))
                return (object?)null;

            var app = GetCurrentApplicationObject();
            if (app == null)
                return (object?)null;

            TryWriteThemeProperty(app, "UserAppTheme", normalized);
            _themeOverride = normalized;

            var requestedTheme = ReadThemeProperty(app, "RequestedTheme");
            var userAppTheme = ReadThemeProperty(app, "UserAppTheme");
            if (_themeOverride != "system")
                userAppTheme = _themeOverride;
            var effectiveTheme = string.Equals(userAppTheme, "system", StringComparison.OrdinalIgnoreCase)
                ? requestedTheme
                : userAppTheme;

            return (object?)new
            {
                theme = effectiveTheme,
                requestedTheme,
                userAppTheme,
                effectiveTheme,
                supportedThemes = new[] { "light", "dark", "system" }
            };
        });
    }

    private static bool TrySetTextValue(object target, string text)
    {
        var type = target.GetType();
        var textProperty = type.GetProperty("Text", BindingFlags.Public | BindingFlags.Instance);
        if (textProperty?.CanWrite == true && textProperty.PropertyType == typeof(string))
        {
            textProperty.SetValue(target, text);
            return true;
        }

        var valueProperty = type.GetProperty("Value", BindingFlags.Public | BindingFlags.Instance);
        if (valueProperty?.CanWrite == true && valueProperty.PropertyType == typeof(string))
        {
            valueProperty.SetValue(target, text);
            return true;
        }

        return false;
    }

    private static string? ReadStringProperty(object target, string propertyName)
    {
        var property = target.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        return property?.PropertyType == typeof(string) ? property.GetValue(target) as string : null;
    }

    private static object? FindAncestorOrSelfByTypeName(object start, string typeName)
    {
        var current = start;
        while (current != null)
        {
            if (string.Equals(current.GetType().Name, typeName, StringComparison.Ordinal))
                return current;

            current = GetPropertyValue(current, "Parent");
        }

        return null;
    }

    private static object? GetCurrentApplicationObject()
    {
        var appType = FindType("Microsoft.UI.Xaml.Application", "Windows.UI.Xaml.Application");
        return appType?.GetProperty("Current", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
    }

    private static bool TryParseTheme(string input, out string normalized)
    {
        normalized = input.Trim().ToLowerInvariant();
        return normalized is "light" or "dark" or "system";
    }

    private static string ReadThemeProperty(object app, string propertyName)
    {
        var prop = app.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        var value = prop?.GetValue(app);
        var token = value?.ToString();
        if (string.IsNullOrWhiteSpace(token))
            return "system";

        return token.ToLowerInvariant() switch
        {
            "light" => "light",
            "dark" => "dark",
            _ => "system"
        };
    }

    private static bool TryWriteThemeProperty(object app, string propertyName, string theme)
    {
        var prop = app.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        if (prop?.CanWrite != true)
            return false;

        var enumType = prop.PropertyType;
        if (!enumType.IsEnum)
            return false;

        string enumName = theme switch
        {
            "light" => "Light",
            "dark" => "Dark",
            _ => "Default"
        };

        var resolved = Enum.GetNames(enumType).FirstOrDefault(n => string.Equals(n, enumName, StringComparison.OrdinalIgnoreCase));
        if (resolved == null)
            return false;

        prop.SetValue(app, Enum.Parse(enumType, resolved));
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

    protected override Task<object?> SendWebViewCdpCommandAsync(string? contextId, string method, JsonElement? @params)
    {
        return InvokeOnUiThreadAsync(() => SendWebViewCdpCommandOnUiThread(contextId, method, @params));
    }

    private Task<T> InvokeOnUiThreadAsync<T>(Func<T> callback)
    {
        var dispatcherQueue = _dispatcherQueue;
        if (dispatcherQueue == null)
            return Task.FromResult(callback());

        var hasThreadAccess = GetPropertyValue(dispatcherQueue, "HasThreadAccess");
        if (hasThreadAccess is bool hasAccess && hasAccess)
            return Task.FromResult(callback());

        var tryEnqueue = dispatcherQueue.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(method => method.Name == "TryEnqueue" && method.GetParameters().Length == 1);
        if (tryEnqueue == null)
            return Task.FromResult(callback());

        var handlerType = tryEnqueue.GetParameters()[0].ParameterType;
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        void InvokeCallback()
        {
            try
            {
                completion.SetResult(callback());
            }
            catch (Exception ex)
            {
                completion.SetException(ex);
            }
        }

        var handler = Delegate.CreateDelegate(handlerType, (Action)InvokeCallback, nameof(Action.Invoke));
        var queued = tryEnqueue.Invoke(dispatcherQueue, new object[] { handler });
        if (queued is bool wasQueued && !wasQueued)
            completion.SetException(new InvalidOperationException("Unable to enqueue work on the Uno UI dispatcher."));

        return completion.Task;
    }

    private Task<T> InvokeOnUiThreadAsync<T>(Func<Task<T>> callback)
    {
        var dispatcherQueue = _dispatcherQueue;
        if (dispatcherQueue == null)
            return callback();

        var hasThreadAccess = GetPropertyValue(dispatcherQueue, "HasThreadAccess");
        if (hasThreadAccess is bool hasAccess && hasAccess)
            return callback();

        var tryEnqueue = dispatcherQueue.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(method => method.Name == "TryEnqueue" && method.GetParameters().Length == 1);
        if (tryEnqueue == null)
            return callback();

        var handlerType = tryEnqueue.GetParameters()[0].ParameterType;
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

        async void InvokeCallback()
        {
            try
            {
                completion.SetResult(await callback().ConfigureAwait(false));
            }
            catch (Exception ex)
            {
                completion.SetException(ex);
            }
        }

        var handler = Delegate.CreateDelegate(handlerType, (Action)InvokeCallback, nameof(Action.Invoke));
        var queued = tryEnqueue.Invoke(dispatcherQueue, new object[] { handler });
        if (queued is bool wasQueued && !wasQueued)
            completion.SetException(new InvalidOperationException("Unable to enqueue work on the Uno UI dispatcher."));

        return completion.Task;
    }

    private static object? GetDispatcherQueue()
    {
        var dispatcherQueueType = FindType(
            "Microsoft.UI.Dispatching.DispatcherQueue",
            "Windows.System.DispatcherQueue");

        var currentDispatcherQueue = dispatcherQueueType?
            .GetMethod("GetForCurrentThread", BindingFlags.Public | BindingFlags.Static)?
            .Invoke(null, null);
        if (currentDispatcherQueue != null)
            return currentDispatcherQueue;

        var appType = FindType(
            "Microsoft.UI.Xaml.Application",
            "Windows.UI.Xaml.Application");

        var app = appType?.GetProperty("Current", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
        var appDispatcher = GetPropertyValue(app, "DispatcherQueue");
        if (appDispatcher != null)
            return appDispatcher;

        var mainWindow = GetPropertyValue(app, "MainWindow")
            ?? GetPropertyValue(app, "CurrentWindow");
        return GetPropertyValue(mainWindow, "DispatcherQueue");
    }

    private async Task<byte[]?> CaptureScreenshotOnUiThreadAsync(string? elementId = null)
    {
        try
        {
            var webViewCapture = await TryCaptureWebView2ScreenshotAsync().ConfigureAwait(false);
            if (webViewCapture != null)
                return webViewCapture;

            var root = !string.IsNullOrWhiteSpace(elementId)
                ? _treeWalker.FindElementObjectById(elementId)
                : GetRootVisual();
            if (root == null)
            {
                LogScreenshotFailure("root visual is null.");
                return null;
            }

            var actualWidth = GetDoubleProperty(root, "ActualWidth");
            var actualHeight = GetDoubleProperty(root, "ActualHeight");

            var renderTargetBitmapType = FindType(
                "Microsoft.UI.Xaml.Media.Imaging.RenderTargetBitmap",
                "Windows.UI.Xaml.Media.Imaging.RenderTargetBitmap");
            if (renderTargetBitmapType == null)
            {
                LogScreenshotFailure("RenderTargetBitmap type not found.");
                return null;
            }

            var renderTargetBitmap = Activator.CreateInstance(renderTargetBitmapType);
            if (renderTargetBitmap == null)
            {
                LogScreenshotFailure("could not create RenderTargetBitmap instance.");
                return null;
            }

            var renderAsync = FindRenderAsyncMethod(renderTargetBitmapType, root, actualWidth, actualHeight)
                ?? renderTargetBitmapType.GetMethod("RenderAsync", new[] { root.GetType() })
                ?? renderTargetBitmapType.GetMethod("RenderAsync", [typeof(object)])
                ?? renderTargetBitmapType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .FirstOrDefault(method => method.Name == "RenderAsync" && method.GetParameters().Length == 1);
            if (renderAsync == null)
            {
                LogScreenshotFailure("RenderAsync method not found.");
                return null;
            }

            var renderArgs = CreateRenderAsyncArguments(renderAsync, root, actualWidth, actualHeight);
            await AwaitAsync(renderAsync.Invoke(renderTargetBitmap, renderArgs)).ConfigureAwait(false);

            var pixelWidth = GetIntProperty(renderTargetBitmap, "PixelWidth");
            var pixelHeight = GetIntProperty(renderTargetBitmap, "PixelHeight");
            if (pixelWidth <= 0 || pixelHeight <= 0)
            {
                if (OperatingSystem.IsWindows())
                {
                    var windowCapture = CaptureWindowsWindowScreenshot();
                    if (windowCapture != null)
                        return windowCapture;
                }

                LogScreenshotFailure($"invalid size {pixelWidth}x{pixelHeight}.");
                return null;
            }

            var getPixelsAsync = renderTargetBitmapType.GetMethod("GetPixelsAsync", Type.EmptyTypes);
            if (getPixelsAsync == null)
            {
                LogScreenshotFailure("GetPixelsAsync method not found.");
                return null;
            }

            var buffer = await AwaitAsync(getPixelsAsync.Invoke(renderTargetBitmap, null)).ConfigureAwait(false);
            var pixels = BufferToByteArray(buffer);
            if (pixels == null || pixels.Length == 0)
            {
                LogScreenshotFailure("pixel buffer conversion returned no data.");
                return null;
            }

            var result = await EncodePngAsync(pixelWidth.GetValueOrDefault(), pixelHeight.GetValueOrDefault(), pixels).ConfigureAwait(false);
            if (result == null)
                LogScreenshotFailure("EncodePngAsync returned null.");

            return result;
        }
        catch (Exception ex)
        {
            LogScreenshotFailure(ex.ToString());
            return null;
        }
    }

    private async Task<byte[]?> TryCaptureWebView2ScreenshotAsync()
    {
        try
        {
            var webView2Type = FindType("Microsoft.UI.Xaml.Controls.WebView2");
            if (webView2Type == null)
                return null;

            var webView = FindFirstDescendantOfType(GetRootVisual(), webView2Type);
            if (webView == null)
                return null;

            var coreWebView2 = GetPropertyValue(webView, "CoreWebView2");
            if (coreWebView2 == null)
                return null;

            var streamType = FindType("Windows.Storage.Streams.InMemoryRandomAccessStream");
            if (streamType == null)
                return null;

            using var stream = Activator.CreateInstance(streamType) as IDisposable;
            if (stream == null)
                return null;

            var imageFormatType = FindType("Microsoft.Web.WebView2.Core.CoreWebView2CapturePreviewImageFormat");
            if (imageFormatType == null)
                return null;

            var pngFormat = Enum.Parse(imageFormatType, "Png");
            var capturePreviewAsync = coreWebView2.GetType().GetMethod("CapturePreviewAsync", [imageFormatType, streamType]);
            if (capturePreviewAsync == null)
                return null;

            await AwaitAsync(capturePreviewAsync.Invoke(coreWebView2, [pngFormat, stream])).ConfigureAwait(false);

            var sizeValue = GetPropertyValue(stream, "Size");
            var streamSize = sizeValue switch
            {
                ulong u => u,
                long l when l >= 0 => (ulong)l,
                uint ui => ui,
                int i when i >= 0 => (ulong)i,
                _ => 0UL
            };

            if (streamSize == 0)
                return null;

            var seek = streamType.GetMethod("Seek", BindingFlags.Public | BindingFlags.Instance);
            seek?.Invoke(stream, [0UL]);

            var inputStream = streamType.GetMethod("GetInputStreamAt", BindingFlags.Public | BindingFlags.Instance)?.Invoke(stream, [0UL]);
            if (inputStream == null)
                return null;

            var bufferType = FindType("Windows.Storage.Streams.Buffer");
            var inputStreamOptionsType = FindType("Windows.Storage.Streams.InputStreamOptions");
            if (bufferType == null || inputStreamOptionsType == null)
                return null;

            var buffer = Activator.CreateInstance(bufferType, (uint)streamSize);
            if (buffer == null)
                return null;

            var options = Enum.Parse(inputStreamOptionsType, "None");
            var readAsync = inputStream.GetType().GetMethod("ReadAsync", BindingFlags.Public | BindingFlags.Instance);
            if (readAsync == null)
                return null;

            var readBuffer = await AwaitAsync(readAsync.Invoke(inputStream, [buffer, (object)(uint)streamSize, options])).ConfigureAwait(false);
            return BufferToByteArray(readBuffer);
        }
        catch
        {
            return null;
        }
    }

    private static object? FindFirstDescendantOfType(object? root, Type targetType)
    {
        if (root == null)
            return null;

        if (targetType.IsInstanceOfType(root))
            return root;

        var queue = new Queue<object>();
        queue.Enqueue(root);

        var visualTreeHelperType = FindType(
            "Microsoft.UI.Xaml.Media.VisualTreeHelper",
            "Windows.UI.Xaml.Media.VisualTreeHelper");
        var getChildrenCount = visualTreeHelperType?.GetMethod("GetChildrenCount", BindingFlags.Public | BindingFlags.Static);
        var getChild = visualTreeHelperType?.GetMethod("GetChild", BindingFlags.Public | BindingFlags.Static);
        if (getChildrenCount == null || getChild == null)
            return null;

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (targetType.IsInstanceOfType(current))
                return current;

            var countValue = getChildrenCount.Invoke(null, [current]);
            if (countValue is not int count || count <= 0)
                continue;

            for (var i = 0; i < count; i++)
            {
                var child = getChild.Invoke(null, [current, i]);
                if (child != null)
                    queue.Enqueue(child);
            }
        }

        return null;
    }

    private object? SendWebViewCdpCommandOnUiThread(string? contextId, string method, JsonElement? @params)
    {
        var webView2Type = FindType("Microsoft.UI.Xaml.Controls.WebView2");
        if (webView2Type == null)
            return new { error = "WebView2 type not found on this Uno target." };

        var root = GetRootVisual();
        var webViews = new List<object>();
        var queue = new Queue<object>();
        if (root != null) queue.Enqueue(root);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (webView2Type.IsInstanceOfType(current))
                webViews.Add(current);

            var visualTreeHelperType = FindType("Microsoft.UI.Xaml.Media.VisualTreeHelper", "Windows.UI.Xaml.Media.VisualTreeHelper");
            var getChildrenCount = visualTreeHelperType?.GetMethod("GetChildrenCount", BindingFlags.Public | BindingFlags.Static);
            var getChild = visualTreeHelperType?.GetMethod("GetChild", BindingFlags.Public | BindingFlags.Static);
            if (getChildrenCount == null || getChild == null)
                continue;
            var count = (int?)getChildrenCount.Invoke(null, new[] { current }) ?? 0;
            for (var i = 0; i < count; i++)
            {
                var child = getChild.Invoke(null, new object[] { current, i });
                if (child != null) queue.Enqueue(child);
            }
        }

        object? target = webViews.FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(contextId))
        {
            target = webViews.FirstOrDefault(w =>
                string.Equals(GetPropertyValue(w, "Name")?.ToString(), contextId, StringComparison.OrdinalIgnoreCase)
                || string.Equals(GetPropertyValue(w, "AutomationId")?.ToString(), contextId, StringComparison.OrdinalIgnoreCase));
        }

        if (target == null)
            return new { error = "No matching WebView2 context found." };

        var core = GetPropertyValue(target, "CoreWebView2");
        if (core == null)
            return new { error = "CoreWebView2 is not initialized." };

        if (string.Equals(method, "Runtime.evaluate", StringComparison.OrdinalIgnoreCase))
        {
            if (!@params.HasValue || !@params.Value.TryGetProperty("expression", out var exprProp))
                return new { error = "Missing params.expression for Runtime.evaluate" };

            var expression = exprProp.GetString() ?? string.Empty;
            var executeScript = core.GetType().GetMethod("ExecuteScriptAsync", new[] { typeof(string) });
            if (executeScript == null)
                return new { error = "ExecuteScriptAsync not found on CoreWebView2." };

            var task = executeScript.Invoke(core, new object[] { expression }) as Task<string>;
            var scriptResult = task?.GetAwaiter().GetResult();
            return new { result = new { value = scriptResult } };
        }

        return new { error = $"Unsupported CDP method: {method}" };
    }

    private static void LogScreenshotFailure(string message)
    {
        Console.Error.WriteLine($"[UnoAgentService] Screenshot capture failed: {message}");
    }

    private object? GetRootVisual()
    {
        return _treeWalker.FindRootElementObject();
    }

    [SupportedOSPlatform("windows")]
    private static byte[]? CaptureWindowsWindowScreenshot()
    {
        var appType = FindType(
            "Microsoft.UI.Xaml.Application",
            "Windows.UI.Xaml.Application");
        var app = appType?.GetProperty("Current", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
        var window = GetPropertyValueAny(app, "MainWindow")
            ?? GetPropertyValueAny(app, "CurrentWindow");
        if (window == null)
            return null;

        var windowNativeType = FindType("WinRT.Interop.WindowNative");
        var getWindowHandle = windowNativeType?.GetMethod("GetWindowHandle", BindingFlags.Public | BindingFlags.Static);
        var handleValue = getWindowHandle?.Invoke(null, new[] { window });
        var hwnd = handleValue switch
        {
            IntPtr value => value,
            long value => new IntPtr(value),
            int value => new IntPtr(value),
            _ => IntPtr.Zero
        };

        return hwnd == IntPtr.Zero ? null : CaptureWindow(hwnd);
    }

    [SupportedOSPlatform("windows")]
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

    private static MethodInfo? FindRenderAsyncMethod(Type renderTargetBitmapType, object root, double? actualWidth, double? actualHeight)
    {
        if (actualWidth is null or <= 0 || actualHeight is null or <= 0)
            return null;

        return renderTargetBitmapType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(method => method.Name == "RenderAsync" && method.GetParameters().Length == 3);
    }

    private static object?[] CreateRenderAsyncArguments(MethodInfo renderAsync, object root, double? actualWidth, double? actualHeight)
    {
        var parameters = renderAsync.GetParameters();
        if (parameters.Length == 3 && actualWidth is > 0 && actualHeight is > 0)
        {
            return
            [
                root,
                ConvertToParameterType((int)Math.Ceiling(actualWidth.Value), parameters[1].ParameterType),
                ConvertToParameterType((int)Math.Ceiling(actualHeight.Value), parameters[2].ParameterType)
            ];
        }

        return [root];
    }

    private static async Task<object?> AwaitAsync(object? operation)
    {
        if (operation is not Task task)
        {
            var statusProperty = operation?.GetType().GetProperty("Status", BindingFlags.Public | BindingFlags.Instance);
            if (statusProperty != null)
            {
                while (true)
                {
                    var status = statusProperty.GetValue(operation)?.ToString();
                    if (string.Equals(status, "Completed", StringComparison.OrdinalIgnoreCase))
                        break;

                    if (string.Equals(status, "Error", StringComparison.OrdinalIgnoreCase))
                    {
                        var errorCode = operation?.GetType().GetProperty("ErrorCode", BindingFlags.Public | BindingFlags.Instance)?.GetValue(operation);
                        throw new InvalidOperationException($"WinRT async operation failed with status Error. ErrorCode={errorCode}");
                    }

                    if (string.Equals(status, "Canceled", StringComparison.OrdinalIgnoreCase))
                        throw new TaskCanceledException("WinRT async operation was canceled.");

                    await Task.Delay(10).ConfigureAwait(false);
                }

                var getResults = operation?.GetType().GetMethod("GetResults", BindingFlags.Public | BindingFlags.Instance);
                return getResults?.Invoke(operation, null);
            }

            return operation;
        }

        await task.ConfigureAwait(false);
        var resultProperty = task.GetType().GetProperty("Result", BindingFlags.Public | BindingFlags.Instance);
        return resultProperty?.GetValue(task);
    }

    private static byte[]? BufferToByteArray(object? buffer)
    {
        if (buffer == null)
            return null;

        var extensionsType = Type.GetType("System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions, System.Runtime.WindowsRuntime");
        var toArray = extensionsType?.GetMethod("ToArray", BindingFlags.Public | BindingFlags.Static, null, [buffer.GetType()], null);
        if (toArray != null)
            return (byte[]?)toArray.Invoke(null, new[] { buffer });

        var dataReaderType = FindType("Windows.Storage.Streams.DataReader");
        if (dataReaderType == null)
            return null;

        var fromBuffer = dataReaderType.GetMethod("FromBuffer", BindingFlags.Public | BindingFlags.Static);
        var reader = fromBuffer?.Invoke(null, new[] { buffer });
        if (reader == null)
            return null;

        try
        {
            var unconsumedLength = (uint?)GetPropertyValue(reader, "UnconsumedBufferLength");
            if (unconsumedLength is null or 0)
                return null;

            var bytes = new byte[unconsumedLength.Value];
            dataReaderType.GetMethod("ReadBytes", BindingFlags.Public | BindingFlags.Instance)?.Invoke(reader, new object[] { bytes });
            return bytes;
        }
        finally
        {
            if (reader is IDisposable disposable)
                disposable.Dispose();
        }
    }

    private static async Task<byte[]?> EncodePngAsync(int width, int height, byte[] pixels)
    {
        var streamType = FindType("Windows.Storage.Streams.InMemoryRandomAccessStream");
        var encoderType = FindType("Windows.Graphics.Imaging.BitmapEncoder");
        var pixelFormatType = FindType("Windows.Graphics.Imaging.BitmapPixelFormat");
        var alphaModeType = FindType("Windows.Graphics.Imaging.BitmapAlphaMode");
        if (streamType == null || encoderType == null || pixelFormatType == null || alphaModeType == null)
        {
            Console.Error.WriteLine("[UnoAgentService] EncodePngAsync failed: required WinRT encoder types not found.");
            return null;
        }

        var stream = Activator.CreateInstance(streamType);
        if (stream == null)
        {
            Console.Error.WriteLine("[UnoAgentService] EncodePngAsync failed: could not create InMemoryRandomAccessStream.");
            return null;
        }

        var pngEncoderId = encoderType.GetProperty("PngEncoderId", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
        if (pngEncoderId == null)
        {
            Console.Error.WriteLine("[UnoAgentService] EncodePngAsync failed: PngEncoderId property not found.");
            return null;
        }

        var createAsync = encoderType.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .FirstOrDefault(method => method.Name == "CreateAsync" && method.GetParameters().Length == 2);
        if (createAsync == null)
            return null;

        var encoder = await AwaitAsync(createAsync.Invoke(null, new[] { pngEncoderId, stream })).ConfigureAwait(false);
        if (encoder == null)
            return null;

        var pixelFormat = Enum.Parse(pixelFormatType, "Bgra8");
        var alphaMode = Enum.Parse(alphaModeType, "Premultiplied");

        encoderType.GetMethod("SetPixelData", BindingFlags.Public | BindingFlags.Instance)?.Invoke(
            encoder,
            new object[] { pixelFormat, alphaMode, (uint)width, (uint)height, 96d, 96d, pixels });

        var flushAsync = encoderType.GetMethod("FlushAsync", BindingFlags.Public | BindingFlags.Instance);
        if (flushAsync == null)
            return null;

        await AwaitAsync(flushAsync.Invoke(encoder, null)).ConfigureAwait(false);

        var seekMethod = streamType.GetMethod("Seek", BindingFlags.Public | BindingFlags.Instance);
        seekMethod?.Invoke(stream, new object[] { 0UL });

        var sizeValue = GetPropertyValue(stream, "Size");
        var streamSize = sizeValue switch
        {
            ulong u => u,
            long l when l >= 0 => (ulong)l,
            uint ui => ui,
            int i when i >= 0 => (ulong)i,
            _ => 0UL
        };
        if (streamSize == 0)
            return null;

        var getInputStreamAt = streamType.GetMethod("GetInputStreamAt", BindingFlags.Public | BindingFlags.Instance);
        var inputStream = getInputStreamAt?.Invoke(stream, new object[] { 0UL });
        if (inputStream == null)
            return null;

        var bufferType = FindType("Windows.Storage.Streams.Buffer");
        var inputStreamOptionsType = FindType("Windows.Storage.Streams.InputStreamOptions");
        if (bufferType == null || inputStreamOptionsType == null)
            return null;

        var winRtBuffer = Activator.CreateInstance(bufferType, (uint)streamSize);
        if (winRtBuffer == null)
            return null;

        var inputStreamOptionsNone = Enum.Parse(inputStreamOptionsType, "None");
        var readAsync = inputStream.GetType().GetMethod("ReadAsync", BindingFlags.Public | BindingFlags.Instance);
        if (readAsync == null)
            return null;

        var readBuffer = await AwaitAsync(readAsync.Invoke(inputStream, new[] { winRtBuffer, (object)(uint)streamSize, inputStreamOptionsNone })).ConfigureAwait(false);
        return BufferToByteArray(readBuffer);
    }

    private static int? GetIntProperty(object instance, string propertyName)
    {
        var value = GetPropertyValue(instance, propertyName);
        return value switch
        {
            int intValue => intValue,
            uint uintValue when uintValue <= int.MaxValue => (int)uintValue,
            long longValue when longValue is >= int.MinValue and <= int.MaxValue => (int)longValue,
            _ => null
        };
    }

    private static object? FindScrollViewer(object element)
    {
        var current = element;
        while (current != null)
        {
            if (IsScrollViewer(current))
                return current;

            current = GetParent(current);
        }

        return null;
    }

    private static bool IsScrollViewer(object element)
    {
        var type = element.GetType();
        return string.Equals(type.Name, "ScrollViewer", StringComparison.OrdinalIgnoreCase)
            || (type.FullName?.EndsWith("ScrollViewer", StringComparison.OrdinalIgnoreCase) ?? false);
    }

    private static object? GetParent(object element)
    {
        var parentProp = element.GetType().GetProperty("Parent", BindingFlags.Public | BindingFlags.Instance);
        if (parentProp != null)
            return parentProp.GetValue(element);

        var visualTreeHelperType = FindType(
            "Microsoft.UI.Xaml.Media.VisualTreeHelper",
            "Windows.UI.Xaml.Media.VisualTreeHelper");

        if (visualTreeHelperType != null)
        {
            var getParent = visualTreeHelperType.GetMethod("GetParent", BindingFlags.Public | BindingFlags.Static);
            if (getParent != null)
                return getParent.Invoke(null, new[] { element });
        }

        return null;
    }

    private static bool TryScroll(object scrollViewer, double deltaX, double deltaY)
    {
        var currentHorizontal = GetDoubleProperty(scrollViewer, "HorizontalOffset");
        var currentVertical = GetDoubleProperty(scrollViewer, "VerticalOffset");
        var horizontal = currentHorizontal ?? 0.0;
        var vertical = currentVertical ?? 0.0;
        var targetHorizontal = Math.Max(0, horizontal + deltaX);
        var targetVertical = Math.Max(0, vertical + deltaY);

        if (TryInvokeChangeView(scrollViewer, targetHorizontal, targetVertical))
            return true;

        if (deltaX != 0 && TryInvokeMethod(scrollViewer, "ScrollToHorizontalOffset", targetHorizontal))
            return true;

        if (deltaY != 0 && TryInvokeMethod(scrollViewer, "ScrollToVerticalOffset", targetVertical))
            return true;

        return false;
    }

    private static bool TryInvokeChangeView(object target, double horizontalOffset, double verticalOffset)
    {
        foreach (var method in target.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance).Where(m => m.Name == "ChangeView"))
        {
            var parameters = method.GetParameters();
            if (parameters.Length == 3)
            {
                try
                {
                    var args = new object?[3];
                    args[0] = ConvertToParameterType(horizontalOffset, parameters[0].ParameterType);
                    args[1] = ConvertToParameterType(verticalOffset, parameters[1].ParameterType);
                    args[2] = null;
                    method.Invoke(target, args);
                    return true;
                }
                catch
                {
                }
            }
            else if (parameters.Length == 4)
            {
                try
                {
                    var args = new object?[4];
                    args[0] = ConvertToParameterType(horizontalOffset, parameters[0].ParameterType);
                    args[1] = ConvertToParameterType(verticalOffset, parameters[1].ParameterType);
                    args[2] = null;
                    args[3] = ConvertToParameterType(true, parameters[3].ParameterType);
                    method.Invoke(target, args);
                    return true;
                }
                catch
                {
                }
            }
        }

        return false;
    }

    private static bool TryInvokeMethod(object target, string methodName, double value)
    {
        foreach (var method in target.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance).Where(m => m.Name == methodName && m.GetParameters().Length == 1))
        {
            var parameterType = method.GetParameters()[0].ParameterType;
            try
            {
                var convertedValue = ConvertToParameterType(value, parameterType);
                method.Invoke(target, new[] { convertedValue! });
                return true;
            }
            catch
            {
            }
        }

        return false;
    }

    private static object? ConvertToParameterType(object? value, Type targetType)
    {
        if (value == null)
        {
            if (!targetType.IsValueType || Nullable.GetUnderlyingType(targetType) != null)
            {
                return null;
            }

            throw new InvalidOperationException($"Cannot convert null to non-nullable type {targetType.FullName}.");
        }

        var effectiveType = Nullable.GetUnderlyingType(targetType) ?? targetType;
        return Convert.ChangeType(value, effectiveType);
    }

    private static bool TryExecuteCommand(object element)
    {
        var command = GetPropertyValue(element, "Command");
        if (command == null)
            return false;

        var canExecuteMethod = command.GetType().GetMethod("CanExecute", new[] { typeof(object) });
        var executeMethod = command.GetType().GetMethod("Execute", new[] { typeof(object) });
        if (canExecuteMethod == null || executeMethod == null)
            return false;

        var canExecute = canExecuteMethod.Invoke(command, new object?[] { null });
        if (canExecute is bool can && can)
        {
            executeMethod.Invoke(command, new object?[] { null });
            return true;
        }

        return false;
    }

    private static bool TryNativeTap(object element)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return false;

        try
        {
            var appType = FindType(
                "Microsoft.UI.Xaml.Application",
                "Windows.UI.Xaml.Application");
            var app = appType?.GetProperty("Current", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            var window = GetPropertyValueAny(app, "MainWindow")
                ?? GetPropertyValueAny(app, "CurrentWindow");

            var hwnd = window != null ? GetWindowHandle(window) : IntPtr.Zero;
            if (hwnd == IntPtr.Zero)
                hwnd = GetSkiaWin32WindowHandle();
            if (hwnd == IntPtr.Zero)
                return false;

            var clickPoint = TryGetElementClickPoint(element, hwnd);
            if (clickPoint == null)
                return false;

            return WindowsNativeActions.TryTap(() => clickPoint);
        }
        catch
        {
            return false;
        }
    }

    [SupportedOSPlatform("windows")]
    private static IntPtr GetWindowHandle(object window)
    {
        var windowNativeType = FindType("WinRT.Interop.WindowNative");
        var getWindowHandle = windowNativeType?.GetMethod("GetWindowHandle", BindingFlags.Public | BindingFlags.Static);
        var handleValue = getWindowHandle?.Invoke(null, new[] { window });
        return handleValue switch
        {
            IntPtr value => value,
            long value => new IntPtr(value),
            int value => new IntPtr(value),
            _ => IntPtr.Zero
        };
    }

    [SupportedOSPlatform("windows")]
    private static IntPtr GetSkiaWin32WindowHandle()
    {
        // On the net10.0-desktop TFM Uno runs on Skia-Win32, so WinRT.Interop.WindowNative
        // doesn't apply. The Win32 backend exposes the live HWND set via a public static
        // accessor (Win32WindowWrapper.GetHwnds). Reach it through reflection so this
        // assembly does not need a hard reference on Uno.UI.Runtime.Skia.Win32.
        var wrapperType = FindType("Uno.UI.Runtime.Skia.Win32.Win32WindowWrapper");
        var getHwnds = wrapperType?.GetMethod("GetHwnds", BindingFlags.Public | BindingFlags.Static);
        if (getHwnds?.Invoke(null, null) is not System.Collections.IEnumerable hwnds)
            return IntPtr.Zero;

        foreach (var hwnd in hwnds)
        {
            return hwnd switch
            {
                IntPtr value => value,
                long value => new IntPtr(value),
                int value => new IntPtr(value),
                _ => IntPtr.Zero
            };
        }

        return IntPtr.Zero;
    }

    [SupportedOSPlatform("windows")]
    private static WindowsScreenPoint? TryGetElementClickPoint(object element, IntPtr hwnd)
    {
        var transformToVisual = element.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(m => string.Equals(m.Name, "TransformToVisual", StringComparison.Ordinal) && m.GetParameters().Length == 1);
        var root = GetRootForTransform(element);
        if (transformToVisual == null || root == null)
            return null;

        var transform = transformToVisual.Invoke(element, new[] { root });
        if (transform == null)
            return null;

        var actualWidth = GetDoubleProperty(element, "ActualWidth");
        var actualHeight = GetDoubleProperty(element, "ActualHeight");
        if (actualWidth is null or <= 0 || actualHeight is null or <= 0)
            return null;

        var pointType = FindType("Windows.Foundation.Point");
        if (pointType == null)
            return null;

        var center = Activator.CreateInstance(pointType, actualWidth.Value / 2d, actualHeight.Value / 2d);
        var transformPoint = transform.GetType().GetMethod("TransformPoint", BindingFlags.Public | BindingFlags.Instance);
        var transformed = transformPoint?.Invoke(transform, new[] { center });
        if (transformed == null)
            return null;

        var x = GetDoubleProperty(transformed, "X");
        var y = GetDoubleProperty(transformed, "Y");
        if (x is null || y is null)
            return null;

        var screenPoint = new POINT
        {
            X = (int)Math.Round(x.Value),
            Y = (int)Math.Round(y.Value)
        };

        if (!ClientToScreen(hwnd, ref screenPoint))
            return null;

        return new WindowsScreenPoint(screenPoint.X, screenPoint.Y);
    }

    private static object? GetRootForTransform(object element)
    {
        var current = element;
        object? parent = current;
        while (parent != null)
        {
            current = parent;
            parent = GetParent(parent);
        }

        return current;
    }

    private static bool TryNativeTextInput(object element, string text, bool replace)
    {
        // Native input synthesises OS keystrokes that flow through the focused
        // control. Only elements that actually accept keyboard text (TextBox,
        // PasswordBox, RichEditBox, AutoSuggestBox) can absorb those events;
        // for read-only controls like TextBlock the keystrokes go to the void
        // and the property-mutation fallback is the only thing that works, so
        // refuse native up front rather than claiming a phantom success.
        if (!IsKeyboardEditableElement(element))
            return false;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return TryNativeAction(element, resolver => WindowsNativeActions.TryTextInput(resolver, text, replace));

        if (!PosixNativeActions.IsAvailable)
            return false;

        TryFocusElement(element);
        return PosixNativeActions.TryTextInput(text, replace);
    }

    private static bool TryNativeKeyInput(object element, string normalizedKey, string? insertText)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return TryNativeAction(element, resolver => WindowsNativeActions.TryKeyInput(resolver, normalizedKey, insertText));

        if (!PosixNativeActions.IsAvailable)
            return false;

        // Same reasoning as TryNativeTextInput, but Enter on a non-editable
        // element is still legitimately a no-op the caller should be told
        // about; the semantic path will report success in that case.
        if (!IsKeyboardEditableElement(element))
            return false;

        TryFocusElement(element);
        return PosixNativeActions.TryKeyInput(normalizedKey, insertText);
    }

    private static bool IsKeyboardEditableElement(object element)
    {
        for (var type = element.GetType(); type != null; type = type.BaseType)
        {
            switch (type.Name)
            {
                case "TextBox":
                case "PasswordBox":
                case "RichEditBox":
                case "AutoSuggestBox":
                    return true;
            }
        }

        return false;
    }

    private static bool TryNativeAction(object element, Func<Func<WindowsScreenPoint?>, bool> action)
    {
        try
        {
            var appType = FindType(
                "Microsoft.UI.Xaml.Application",
                "Windows.UI.Xaml.Application");
            var app = appType?.GetProperty("Current", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            var window = GetPropertyValueAny(app, "MainWindow")
                ?? GetPropertyValueAny(app, "CurrentWindow");

            var hwnd = window != null ? GetWindowHandle(window) : IntPtr.Zero;
            if (hwnd == IntPtr.Zero)
                hwnd = GetSkiaWin32WindowHandle();
            if (hwnd == IntPtr.Zero)
                return false;

            return action(() => TryGetElementClickPoint(element, hwnd));
        }
        catch
        {
            return false;
        }
    }

    private static bool IsElementEnabled(object element)
    {
        var isEnabled = GetPropertyValue(element, "IsEnabled");
        return isEnabled is not bool enabled || enabled;
    }

    private static void TryFocusElement(object element)
    {
        var targetType = element.GetType();
        var focusNoArgs = targetType.GetMethod("Focus", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
        if (focusNoArgs != null)
        {
            focusNoArgs.Invoke(element, null);
            return;
        }

        var focusWithState = targetType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(m =>
            {
                if (!string.Equals(m.Name, "Focus", StringComparison.Ordinal))
                    return false;

                var parameters = m.GetParameters();
                return parameters.Length == 1 && parameters[0].ParameterType.IsEnum;
            });
        if (focusWithState == null)
            return;

        var enumType = focusWithState.GetParameters()[0].ParameterType;
        var programmatic = Enum.GetNames(enumType).FirstOrDefault(n => string.Equals(n, "Programmatic", StringComparison.OrdinalIgnoreCase));
        var state = programmatic != null ? Enum.Parse(enumType, programmatic) : Enum.ToObject(enumType, 0);
        focusWithState.Invoke(element, new[] { state });
    }

    private static bool TryInvokeOnClick(object element)
    {
        var onClick = element.GetType()
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy)
            .FirstOrDefault(method => method.Name == "OnClick");
        if (onClick != null)
        {
            var args = onClick.GetParameters()
                .Select(parameter => parameter.ParameterType.IsValueType ? Activator.CreateInstance(parameter.ParameterType) : null)
                .ToArray();
            onClick.Invoke(element, args);
            return true;
        }

        return false;
    }

    private static bool TryInvokeAutomationPattern(object element)
    {
        var peerType = FindType(
            "Microsoft.UI.Xaml.Automation.Peers.ButtonAutomationPeer",
            "Windows.UI.Xaml.Automation.Peers.ButtonAutomationPeer");
        if (peerType == null)
            return false;

        var constructor = peerType.GetConstructors()
            .FirstOrDefault(ctor =>
            {
                var parameters = ctor.GetParameters();
                return parameters.Length == 1 && parameters[0].ParameterType.IsInstanceOfType(element);
            });
        if (constructor == null)
            return false;

        try
        {
            var peer = constructor.Invoke(new[] { element });
            var patternInterfaceType = FindType(
                "Microsoft.UI.Xaml.Automation.Peers.PatternInterface",
                "Windows.UI.Xaml.Automation.Peers.PatternInterface");
            if (patternInterfaceType == null)
                return false;

            var invokeValue = Enum.Parse(patternInterfaceType, "Invoke");
            var getPattern = peerType.GetMethod("GetPattern", BindingFlags.Public | BindingFlags.Instance);
            var provider = getPattern?.Invoke(peer, new[] { invokeValue });
            var invoke = provider?.GetType().GetMethod("Invoke", BindingFlags.Public | BindingFlags.Instance);
            if (invoke == null)
                return false;

            invoke.Invoke(provider, Array.Empty<object>());
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static object? GetPropertyValue(object? target, string propertyName)
    {
        if (target == null)
            return null;

        var property = target.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        return property?.GetValue(target);
    }

    private static object? GetPropertyValueAny(object? target, string propertyName)
    {
        if (target == null)
            return null;

        var property = target.GetType().GetProperty(
            propertyName,
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
        return property?.GetValue(target);
    }

    private static double? GetDoubleProperty(object target, string propertyName)
    {
        var value = GetPropertyValue(target, propertyName);
        if (value is double d)
            return d;

        if (value is float f)
            return f;

        if (value is int i)
            return i;

        if (value is long l)
            return l;

        if (value is string s && double.TryParse(s, out var parsed))
            return parsed;

        return null;
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
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ClientToScreen(nint hWnd, ref POINT lpPoint);
}
