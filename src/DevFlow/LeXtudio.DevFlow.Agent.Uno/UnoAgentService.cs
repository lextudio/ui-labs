using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Versioning;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
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

    /// <summary>
    /// Registers the host's main <c>Microsoft.UI.Xaml.Window</c> so the agent can find the visual
    /// tree on WinUI 3 / WindowsAppSDK, which has no global window registry (Window.Current is null
    /// and Application exposes no window list). Not needed on Uno desktop, where windows are
    /// discovered automatically. Call once after creating/activating the main window.
    /// </summary>
    public static void RegisterWindow(object window) => UnoVisualTreeWalker.RegisteredWindow = window;

    protected override string AgentId => "LeXtudio.DevFlow.Agent";
    protected override string AgentName => "LeXtudio.DevFlow.Agent";
    protected override string FrameworkName => "uno";
    protected override object GetCapabilities() => new
    {
        screenshots = true,
        elementScreenshots = true,
        selectorScreenshots = false,
        tap = true,
        rightTap = RuntimeInformation.IsOSPlatform(OSPlatform.Windows),
        scroll = true,
        drag = RuntimeInformation.IsOSPlatform(OSPlatform.OSX),
        structuredErrors = true,
        appTheme = true,
        webview = true,
        webviewCdp = true,
        multiWindow = true,
        windowContentOrigin = TryDescribeWindowOrigin(),
    };

    // Diagnostic: the window content origin (screen points) + scale used to
    // map element/window coordinates to the global space for drag injection.
    private static object? TryDescribeWindowOrigin()
    {
        try
        {
            var m = TryGetWindowMetrics();
            return m is null ? null : new { x = m.Value.OriginX, y = m.Value.OriginY, scale = m.Value.Scale };
        }
        catch { return null; }
    }

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

    protected override Task<List<ElementInfo>> QueryElementsAsync(string? type = null, string? automationId = null, string? text = null, int maxResults = 50, int maxDepth = 24)
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

            if (TryInvokeSelectionItemPattern(target))
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
                () => TryInvokeOnClick(target) ? CreateSuccessResult(SimulationModes.Reflection, elementId) : null,
                () => TryInvokeSelectionItemPattern(target) ? CreateSuccessResult(SimulationModes.Semantic, elementId) : null);
        });
    }

    protected override Task<object?> TryRightTapResponseAsync(string elementId)
    {
        return InvokeOnUiThreadAsync<object?>(() =>
        {
            var target = _treeWalker.FindElementObjectById(elementId);
            if (target == null)
                return null;

            // A right-click only requires a hit-testable, visible element — unlike a left
            // tap there is no semantic/automation fallback, since opening a context menu is
            // inherently a pointer gesture. So inject a native secondary click at the element
            // centre; this drives the real WinUI right-tap pipeline (RightTapped / flyouts).
            return TryNativeRightTap(target)
                ? CreateSuccessResult(SimulationModes.Native, elementId)
                : null;
        });
    }

    private static bool TryNativeRightTap(object element)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return false;

        try
        {
            var hwnd = ResolveUnoHwnd();
            if (hwnd == IntPtr.Zero)
                return false;

            WindowsNativeInput.TryBringToForeground(hwnd);

            var clickPoint = TryGetElementClickPoint(element, hwnd);
            if (clickPoint == null)
                return false;

            return WindowsNativeActions.TryRightTap(() => clickPoint);
        }
        catch
        {
            return false;
        }
    }

    private static readonly string DragLogPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "devflow_drag.log");

    internal static void DragLog(string message)
    {
        try
        {
            System.IO.File.AppendAllText(DragLogPath,
                $"{DateTime.Now:HH:mm:ss.fff} {message}{Environment.NewLine}");
        }
        catch { /* logging must never throw */ }
    }

    /// <summary>
    /// Injects a global-coordinate left-click via CGEvent (macOS only).
    /// Accepts x/y in global screen coordinates (same space as CGEventGetLocation).
    /// When global=false, x/y are window-relative logical points converted to screen.
    /// </summary>
    protected override Task<object?> TryClickResponseAsync(ClickRequest request)
    {
        return InvokeOnUiThreadAsync<object?>(() =>
        {
            DragLog($"--- click request x={request.X} y={request.Y} global={request.Global} clicks={request.ClickCount}");
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                return new { ok = false, reason = "click injection implemented for macOS only" };

            TryActivateMainWindow();

            double x, y;
            if (request.Global)
            {
                x = request.X!.Value;
                y = request.Y!.Value;
            }
            else
            {
                // Convert window-relative logical points to global screen points
                var metrics = TryGetWindowMetrics();
                if (metrics is null)
                    return new { ok = false, reason = "could not resolve window metrics" };
                var m = metrics.Value;
                x = m.OriginX + request.X!.Value * m.Scale;
                y = m.OriginY + request.Y!.Value * m.Scale;
            }

            DragLog($"click: injecting at ({x:F1},{y:F1}) count={request.ClickCount}");
            var ok = MacOSNativeInput.TryMouseClick(x, y, request.ClickCount);
            DragLog($"click: TryMouseClick returned {ok}");
            return new { ok, mode = request.Global ? "native-global" : "native-window", x, y };
        });
    }

    protected override Task<object?> TryDragResponseAsync(DragRequest request)
    {
        // OS-level press → drag → release. Needed because some gestures (e.g.
        // the Reactor docking tab tear-off) poll the *global* cursor / button
        // state rather than listening to XAML pointer events, so only a real
        // synthesized OS drag exercises them.
        return InvokeOnUiThreadAsync<object?>(() =>
        {
            DragLog($"--- drag request from=({request.FromX},{request.FromY}) to=({request.ToX},{request.ToY}) d=({request.Dx},{request.Dy}) fromId={request.FromId} toId={request.ToId} steps={request.Steps}");
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                return new { ok = false, reason = "drag injection is implemented for macOS only" };

            TryActivateMainWindow();

            // Diagnostic path: coordinates are already absolute global points.
            if (request.Global && request.FromX.HasValue && request.FromY.HasValue)
            {
                double gToX = request.ToX ?? (request.FromX.Value + (request.Dx ?? 0));
                double gToY = request.ToY ?? (request.FromY.Value + (request.Dy ?? 0));
                var gSteps = request.Steps is > 0 ? request.Steps.Value : 24;
                DragLog($"drag(global): from=({request.FromX},{request.FromY}) to=({gToX},{gToY}) steps={gSteps}");
                var gok = MacOSNativeInput.TryMouseDrag(request.FromX.Value, request.FromY.Value, gToX, gToY, gSteps);
                DragLog($"drag(global): TryMouseDrag returned {gok}");
                return new { ok = gok, mode = "native-global", from = new { x = request.FromX, y = request.FromY }, to = new { x = gToX, y = gToY }, steps = gSteps };
            }

            var metrics = TryGetWindowMetrics();
            if (metrics is null)
                return new { ok = false, reason = "could not resolve window metrics (AppWindow.Position)" };
            var m = metrics.Value;

            if (!TryResolveScreenPoint(request.FromId, request.FromX, request.FromY, m, out var fromX, out var fromY))
                return new { ok = false, reason = "could not resolve source point (need fromId or fromX/fromY)" };

            double toX, toY;
            if (TryResolveScreenPoint(request.ToId, request.ToX, request.ToY, m, out var tx, out var ty))
            {
                toX = tx; toY = ty;
            }
            else if (request.Dx.HasValue || request.Dy.HasValue)
            {
                // Deltas are in screenshot pixels too → convert to points.
                toX = fromX + (request.Dx ?? 0) / m.Scale;
                toY = fromY + (request.Dy ?? 0) / m.Scale;
            }
            else
            {
                return new { ok = false, reason = "could not resolve target point (need toId, toX/toY, or dx/dy)" };
            }

            var steps = request.Steps is > 0 ? request.Steps.Value : 24;
            DragLog($"drag: posting global from=({fromX},{fromY}) to=({toX},{toY}) steps={steps}");
            var ok = MacOSNativeInput.TryMouseDrag(fromX, fromY, toX, toY, steps);
            DragLog($"drag: TryMouseDrag returned {ok}");
            return new
            {
                ok,
                mode = "native",
                from = new { x = fromX, y = fromY },
                to = new { x = toX, y = toY },
                steps,
                note = ok ? null : "CGEventPost returned without delivery — grant Accessibility (TCC) permission to the host process",
            };
        });
    }

    // Resolves a drag endpoint to global screen points (top-left origin).
    // X/Y, when supplied, are WINDOW-CONTENT-relative points (matching the
    // coordinate space of a window screenshot at scale 1) — the window content
    // origin is added here. An element id resolves to that element's centre in
    // the same window-content space. Both then add the on-screen origin.
    private bool TryResolveScreenPoint(string? elementId, double? winX, double? winY,
        (double OriginX, double OriginY, double Scale) m, out double x, out double y)
    {
        x = 0; y = 0;

        // local point in window-content POINTS.
        (double X, double Y)? localPt = null;
        if (winX.HasValue && winY.HasValue)
        {
            // Request coords are screenshot PIXELS → divide by scale for points.
            localPt = (winX.Value / m.Scale, winY.Value / m.Scale);
        }
        else if (!string.IsNullOrWhiteSpace(elementId))
        {
            var target = _treeWalker.FindElementObjectById(elementId);
            if (target != null)
                localPt = TryGetElementWindowDip(target); // already in DIPs (points)
        }

        if (localPt is null)
        {
            DragLog($"resolve: local=null (winX={winX} winY={winY} id={elementId})");
            return false;
        }

        x = m.OriginX + localPt.Value.X;
        y = m.OriginY + localPt.Value.Y;
        DragLog($"resolve: localPt=({localPt.Value.X},{localPt.Value.Y}) origin=({m.OriginX},{m.OriginY}) scale={m.Scale} => globalPt=({x},{y})");
        return true;
    }

    // Element centre in window-content-relative DIPs (TransformToVisual(root)).
    private static (double X, double Y)? TryGetElementWindowDip(object element)
    {
        var transformToVisual = element.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(m => string.Equals(m.Name, "TransformToVisual", StringComparison.Ordinal) && m.GetParameters().Length == 1);
        var root = GetRootForTransform(element);
        if (transformToVisual == null || root == null)
            return null;

        var transform = transformToVisual.Invoke(element, new[] { root });
        var actualWidth = GetDoubleProperty(element, "ActualWidth");
        var actualHeight = GetDoubleProperty(element, "ActualHeight");
        if (transform == null || actualWidth is null or <= 0 || actualHeight is null or <= 0)
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

        return (x.Value, y.Value);
    }

    // The window's content-area top-left in global screen points.
    // AppWindow.Position is in physical pixels; divide by the rasterization
    // scale to get points (the space CGEvent mouse coordinates use).
    // Window content origin in screen POINTS plus the rasterization scale.
    // AppWindow.Position is in physical pixels; CGEvent mouse coordinates and
    // element DIPs are in points, so origin-in-points = positionPx / scale.
    private static (double OriginX, double OriginY, double Scale)? TryGetWindowMetrics()
    {
        var window = TryGetMainWindow();
        if (window == null)
        {
            DragLog("metrics: window=null");
            return null;
        }

        var appWindow = GetPropertyValueAny(window, "AppWindow");
        var position = appWindow != null ? GetPropertyValueAny(appWindow, "Position") : null;
        if (position == null)
        {
            DragLog($"metrics: position=null (appWindow={(appWindow == null ? "null" : "ok")})");
            return null;
        }

        var px = GetInt32Member(position, "X");
        var py = GetInt32Member(position, "Y");
        var scale = TryGetRasterizationScale(window);
        if (scale <= 0) scale = 1.0;
        DragLog($"metrics: posPx=({px},{py}) scale={scale}");
        if (px is null || py is null)
            return null;

        return (px.Value / scale, py.Value / scale, scale);
    }

    // PointInt32.X/Y surface as fields (not properties) under Uno's projection,
    // so the property-only reader misses them. Read property OR field.
    private static int? GetInt32Member(object target, string name)
    {
        var t = target.GetType();
        var prop = t.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
        var raw = prop != null ? prop.GetValue(target)
            : t.GetField(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(target);
        return raw switch
        {
            int i => i,
            long l => (int)l,
            double d => (int)d,
            _ => null,
        };
    }

    private static object? TryGetMainWindow()
    {
        var appType = FindType("Microsoft.UI.Xaml.Application", "Windows.UI.Xaml.Application");
        var app = appType?.GetProperty("Current", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
        return GetPropertyValueAny(app, "MainWindow") ?? GetPropertyValueAny(app, "CurrentWindow");
    }

    // Bring the app window to the foreground so synthesized OS pointer events
    // land on it (and the docking tear-off's PointerPressed routing fires).
    private static void TryActivateMainWindow()
    {
        try
        {
            var window = TryGetMainWindow();
            var activate = window?.GetType().GetMethod("Activate", BindingFlags.Public | BindingFlags.Instance, Type.EmptyTypes);
            activate?.Invoke(window, null);
            DragLog($"activate: window={(window == null ? "null" : "ok")} activateInvoked={activate != null}");
        }
        catch (Exception ex) { DragLog($"activate: ex {ex.Message}"); }
    }

    private static double TryGetRasterizationScale(object window)
    {
        var content = GetPropertyValueAny(window, "Content");
        var xamlRoot = content != null ? GetPropertyValueAny(content, "XamlRoot") : null;
        var scale = xamlRoot != null ? GetDoubleProperty(xamlRoot, "RasterizationScale") : null;
        return scale ?? 1.0;
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
        return InvokeOnUiThreadAsync(() => SendWebViewCdpCommandOnUiThreadAsync(contextId, method, @params));
    }

    protected override Task<object?> GetWebViewContextsAsync()
    {
        return InvokeOnUiThreadAsync<object?>(GetWebViewContextsOnUiThread);
    }

    private object GetWebViewContextsOnUiThread()
    {
        var webView2Type = FindType("Microsoft.UI.Xaml.Controls.WebView2");
        if (webView2Type == null)
            return new { contexts = Array.Empty<object>() };

        var webViews = EnumerateWebView2Descendants(GetRootVisual(), webView2Type).ToList();
        var contexts = new List<object>();

        foreach (var webView in webViews)
        {
            var name = SafeGetPropertyString(webView, "Name");
            var automationId = SafeGetPropertyString(webView, "AutomationId");
            var id = !string.IsNullOrWhiteSpace(automationId)
                ? automationId
                : !string.IsNullOrWhiteSpace(name) ? name : $"webview-{contexts.Count + 1}";
            contexts.Add(new { id, type = "webview2", title = name ?? id });
        }

        return new { contexts };
    }

    private static string? SafeGetPropertyString(object target, string propertyName)
    {
        try { return GetPropertyValue(target, propertyName) as string; }
        catch { return null; }
    }

    protected override Task<T> DispatchOnUIThreadAsync<T>(Func<T> callback)
    {
        return InvokeOnUiThreadAsync(callback);
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

            // Element capture targets a single element; whole-window capture composites
            // EVERY open window (main + floating child windows) so child windows are visible.
            if (!string.IsNullOrWhiteSpace(elementId))
            {
                var element = _treeWalker.FindElementObjectById(elementId);
                if (element == null)
                {
                    LogScreenshotFailure("root visual is null.");
                    return null;
                }

                var single = await RenderObjectToBgraAsync(element).ConfigureAwait(false);
                if (single == null)
                {
                    if (OperatingSystem.IsWindows())
                    {
                        var windowCapture = CaptureWindowsWindowScreenshot();
                        if (windowCapture != null)
                            return windowCapture;
                    }
                    LogScreenshotFailure("element render returned no pixels.");
                    return null;
                }

                var encoded = await EncodePngAsync(single.Value.Width, single.Value.Height, single.Value.Pixels).ConfigureAwait(false);
                if (encoded == null)
                    LogScreenshotFailure("EncodePngAsync returned null.");
                return encoded;
            }

            var composite = await CaptureAllWindowsCompositeAsync().ConfigureAwait(false);
            if (composite != null)
                return composite;

            if (OperatingSystem.IsWindows())
            {
                var windowCapture = CaptureWindowsWindowScreenshot();
                if (windowCapture != null)
                    return windowCapture;
            }

            LogScreenshotFailure("composite capture returned null.");
            return null;
        }
        catch (Exception ex)
        {
            LogScreenshotFailure(ex.ToString());
            return null;
        }
    }

    // Renders any XAML visual to a BGRA8 pixel buffer via RenderTargetBitmap. Returns null
    // when the visual has no renderable size. Shared by element capture and the multi-window
    // compositor.
    private async Task<(int Width, int Height, byte[] Pixels)?> RenderObjectToBgraAsync(object root)
    {
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
            return null;

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

        return (pixelWidth.GetValueOrDefault(), pixelHeight.GetValueOrDefault(), pixels);
    }

    // Captures EVERY open window (main + floating child windows) and composites them into a
    // single image positioned by each window's screen origin, so child windows appear where
    // they actually are. Falls back to a cascade offset when a window reports no usable
    // position (e.g. natively-positioned floating windows on macOS), so they remain visible
    // instead of stacking exactly on top of the main window.
    private async Task<byte[]?> CaptureAllWindowsCompositeAsync()
    {
        var windows = _treeWalker.GetAllWindows();
        DragLog($"composite: enumerated {windows.Count} window(s)");
        if (windows.Count == 0)
        {
            LogScreenshotFailure("no windows enumerated.");
            return null;
        }

        var mainWindow = TryGetMainWindow();
        var rendered = new List<(double X, double Y, int W, int H, byte[] Px, bool HasPos, bool IsMain)>();

        foreach (var window in windows)
        {
            object? root = _treeWalker.GetWindowContentRoot(window);
            var isMain = mainWindow != null && ReferenceEquals(window, mainWindow);
            if (root == null)
            {
                DragLog($"composite: skip isMain={isMain} reason=null-root");
                continue;
            }

            var rootW = GetDoubleProperty(root, "ActualWidth");
            var rootH = GetDoubleProperty(root, "ActualHeight");
            (int Width, int Height, byte[] Pixels)? bgra;
            try { bgra = await RenderObjectToBgraAsync(root).ConfigureAwait(false); }
            catch (Exception ex) { DragLog($"composite: render EXCEPTION isMain={isMain}: {ex.Message}"); continue; }
            if (bgra == null)
            {
                DragLog($"composite: skip isMain={isMain} reason=render-null rootSize={rootW:F0}x{rootH:F0} rootType={root.GetType().Name}");
                continue;
            }

            var (px, py, hasPos) = TryGetWindowOriginPixels(window);
            rendered.Add((px, py, bgra.Value.Width, bgra.Value.Height, bgra.Value.Pixels, hasPos, isMain));
            DragLog($"composite: window isMain={isMain} pos=({px:F0},{py:F0}) hasPos={hasPos} size={bgra.Value.Width}x{bgra.Value.Height}");
        }

        if (rendered.Count == 0)
        {
            LogScreenshotFailure("no windows rendered.");
            return null;
        }

        // Single window → return it directly (exact original behavior, no composite overhead).
        if (rendered.Count == 1)
            return await EncodePngAsync(rendered[0].W, rendered[0].H, rendered[0].Px).ConfigureAwait(false);

        // Draw order: main window first (bottom), child windows on top.
        rendered.Sort((a, b) => a.IsMain == b.IsMain ? 0 : (a.IsMain ? -1 : 1));

        // Faithful layout by reported screen position (correct on Windows). But AppWindow.Position
        // is unreliable for natively-positioned floating windows on macOS, producing a huge,
        // mostly-empty canvas. Measure the "fill ratio"; if positions look bogus, fall back to a
        // clean tiled contact-sheet so every child window is fully visible.
        double minX = rendered.Min(r => r.X);
        double minY = rendered.Min(r => r.Y);
        double posMaxX = rendered.Max(r => r.X + r.W);
        double posMaxY = rendered.Max(r => r.Y + r.H);
        long posCanvasArea = (long)Math.Ceiling(posMaxX - minX) * (long)Math.Ceiling(posMaxY - minY);
        long windowsArea = rendered.Sum(r => (long)r.W * r.H);
        bool allHavePos = rendered.All(r => r.HasPos || r.IsMain);
        double fillRatio = posCanvasArea > 0 ? (double)windowsArea / posCanvasArea : 0;

        if (allHavePos && fillRatio >= 0.5)
        {
            int cw = (int)Math.Ceiling(posMaxX - minX);
            int ch = (int)Math.Ceiling(posMaxY - minY);
            DragLog($"composite: faithful layout {cw}x{ch} fill={fillRatio:F2}");
            var canvas = new byte[cw * ch * 4];
            foreach (var r in rendered)
                BlitBgra(canvas, cw, ch, r.Px, r.W, r.H, (int)Math.Round(r.X - minX), (int)Math.Round(r.Y - minY));
            return await EncodePngAsync(cw, ch, canvas).ConfigureAwait(false);
        }

        // Tiled contact-sheet fallback: lay windows out left-to-right (main first), top-aligned,
        // with a gap and an opaque backdrop so transparent window edges read clearly.
        const int Gap = 24;
        int tileW = rendered.Sum(r => r.W) + Gap * (rendered.Count + 1);
        int tileH = rendered.Max(r => r.H) + Gap * 2;
        DragLog($"composite: tiled layout {tileW}x{tileH} fill={fillRatio:F2} allHavePos={allHavePos}");
        var tiled = new byte[tileW * tileH * 4];
        FillBgra(tiled, 0x30, 0x2D, 0x2D, 0xFF); // VS-style dark backdrop #2D2D30 (B,G,R,A)
        int cursorX = Gap;
        foreach (var r in rendered)
        {
            BlitBgra(tiled, tileW, tileH, r.Px, r.W, r.H, cursorX, Gap);
            cursorX += r.W + Gap;
        }
        return await EncodePngAsync(tileW, tileH, tiled).ConfigureAwait(false);
    }

    // Fills a BGRA buffer with a solid color.
    private static void FillBgra(byte[] buffer, byte b, byte g, byte r, byte a)
    {
        for (int i = 0; i + 3 < buffer.Length; i += 4)
        {
            buffer[i] = b;
            buffer[i + 1] = g;
            buffer[i + 2] = r;
            buffer[i + 3] = a;
        }
    }

    // Alpha-composites a source BGRA buffer onto the destination at (offX, offY).
    private static void BlitBgra(byte[] dst, int dstW, int dstH, byte[] src, int srcW, int srcH, int offX, int offY)
    {
        for (int y = 0; y < srcH; y++)
        {
            int dy = offY + y;
            if (dy < 0 || dy >= dstH) continue;
            for (int x = 0; x < srcW; x++)
            {
                int dx = offX + x;
                if (dx < 0 || dx >= dstW) continue;
                int si = (y * srcW + x) * 4;
                int di = (dy * dstW + dx) * 4;
                if (si + 3 >= src.Length) continue;
                byte sa = src[si + 3];
                if (sa == 0) continue; // fully transparent → keep what's underneath
                if (sa == 255)
                {
                    dst[di] = src[si];
                    dst[di + 1] = src[si + 1];
                    dst[di + 2] = src[si + 2];
                    dst[di + 3] = 255;
                    continue;
                }
                // Source-over alpha blend.
                int ia = 255 - sa;
                dst[di] = (byte)((src[si] * sa + dst[di] * ia) / 255);
                dst[di + 1] = (byte)((src[si + 1] * sa + dst[di + 1] * ia) / 255);
                dst[di + 2] = (byte)((src[si + 2] * sa + dst[di + 2] * ia) / 255);
                dst[di + 3] = (byte)(sa + dst[di + 3] * ia / 255);
            }
        }
    }

    // Returns a window's content-area screen origin in PIXELS, and whether a real position
    // was available. Uses AppWindow.Position (physical px). hasPos=false when it cannot be read.
    private static (double X, double Y, bool HasPos) TryGetWindowOriginPixels(object window)
    {
        try
        {
            var appWindow = GetPropertyValueAny(window, "AppWindow");
            var position = appWindow != null ? GetPropertyValueAny(appWindow, "Position") : null;
            if (position == null)
                return (0, 0, false);
            var px = GetInt32Member(position, "X");
            var py = GetInt32Member(position, "Y");
            if (px is null || py is null)
                return (0, 0, false);
            // (0,0) is treated as "no real position" because Uno reports it for windows whose
            // OS placement bypassed AppWindow.Move (native floating windows on macOS).
            var hasPos = px.Value != 0 || py.Value != 0;
            return (px.Value, py.Value, hasPos);
        }
        catch { return (0, 0, false); }
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
        return EnumerateDescendants(root, targetType).FirstOrDefault();
    }

    private static IEnumerable<object> EnumerateWebView2Descendants(object? root, Type webView2Type)
    {
        return EnumerateDescendants(root, webView2Type);
    }

    /// <summary>
    /// Enumerates descendants of <paramref name="root"/> matching <paramref name="targetType"/>, traversing
    /// both the visual tree (VisualTreeHelper) and the logical tree (Content / Items / Children) so that
    /// elements inside Frames, ContentControls, and ItemsControls are reachable even before the visual
    /// tree has finished realizing them.
    /// </summary>
    private static IEnumerable<object> EnumerateDescendants(object? root, Type targetType)
    {
        if (root == null)
            yield break;

        var visualTreeHelperType = FindType(
            "Microsoft.UI.Xaml.Media.VisualTreeHelper",
            "Windows.UI.Xaml.Media.VisualTreeHelper");
        var getChildrenCount = visualTreeHelperType?.GetMethod("GetChildrenCount", BindingFlags.Public | BindingFlags.Static);
        var getChild = visualTreeHelperType?.GetMethod("GetChild", BindingFlags.Public | BindingFlags.Static);

        var queue = new Queue<object>();
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        queue.Enqueue(root);
        visited.Add(root);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (targetType.IsInstanceOfType(current))
                yield return current;

            // 1) Visual-tree children (works once layout has realized children).
            if (getChildrenCount != null && getChild != null)
            {
                int count = 0;
                try { count = (int?)getChildrenCount.Invoke(null, [current]) ?? 0; }
                catch { count = 0; }

                for (var i = 0; i < count; i++)
                {
                    object? child = null;
                    try { child = getChild.Invoke(null, [current, i]); }
                    catch { /* swallow per-element errors */ }
                    if (child != null && visited.Add(child))
                        queue.Enqueue(child);
                }
            }

            // 2) Logical children that VisualTreeHelper does not expose
            //    (Frame.Content, ContentControl.Content, ItemsControl.Items, Panel.Children).
            EnqueueLogicalChild(queue, visited, current, "Content");
            EnqueueLogicalChild(queue, visited, current, "Child");
            EnqueueLogicalChildren(queue, visited, current, "Items");
            EnqueueLogicalChildren(queue, visited, current, "Children");
        }
    }

    private static void EnqueueLogicalChild(Queue<object> queue, HashSet<object> visited, object current, string propertyName)
    {
        object? value = null;
        try { value = GetPropertyValue(current, propertyName); }
        catch { return; }

        if (value == null || value is string)
            return;
        if (visited.Add(value))
            queue.Enqueue(value);
    }

    private static void EnqueueLogicalChildren(Queue<object> queue, HashSet<object> visited, object current, string propertyName)
    {
        object? value = null;
        try { value = GetPropertyValue(current, propertyName); }
        catch { return; }

        if (value is not IEnumerable enumerable || value is string)
            return;

        foreach (var item in enumerable)
        {
            if (item != null && visited.Add(item))
                queue.Enqueue(item);
        }
    }

    private async Task<object?> SendWebViewCdpCommandOnUiThreadAsync(string? contextId, string method, JsonElement? @params)
    {
        var webView2Type = FindType("Microsoft.UI.Xaml.Controls.WebView2");
        if (webView2Type == null)
            return new { error = "WebView2 type not found on this Uno target." };

        var webViews = EnumerateWebView2Descendants(GetRootVisual(), webView2Type).ToList();

        object? target = webViews.FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(contextId))
        {
            target = webViews.FirstOrDefault(w =>
                string.Equals(SafeGetPropertyString(w, "Name"), contextId, StringComparison.OrdinalIgnoreCase)
                || string.Equals(SafeGetPropertyString(w, "AutomationId"), contextId, StringComparison.OrdinalIgnoreCase));
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

            var raw = executeScript.Invoke(core, new object[] { expression });
            var scriptResult = await AwaitStringResultAsync(raw).ConfigureAwait(false);
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
            ?? GetPropertyValueAny(app, "CurrentWindow")
            ?? UnoVisualTreeWalker.RegisteredWindow; // WinUI 3: no Application window list
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
            // PW_RENDERFULLCONTENT (2) is required to capture WinUI 3 / WindowsAppSDK windows, whose
            // content is composed via DirectComposition; flags=0 yields a blank (white) frame.
            const uint PW_RENDERFULLCONTENT = 2;
            if (!PrintWindow(hwnd, hdc, PW_RENDERFULLCONTENT))
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

    private static async Task<string?> AwaitStringResultAsync(object? operation)
    {
        if (operation == null)
            return null;

        try
        {
            // CoreWebView2.ExecuteScriptAsync returns Task<string> on WPF/WinForms WebView2,
            // but IAsyncOperation<string> on WinAppSDK / Uno's Microsoft.UI.Xaml.Controls.WebView2.
            // For IAsyncOperation<T>, polling Status + calling GetResults() returns null because
            // WinRT requires consuming the operation via its awaiter/AsTask which sets the result
            // through a Completed handler. We use AsTask() via the WindowsRuntimeSystemExtensions.
            if (operation is Task plainTask)
            {
                await plainTask.ConfigureAwait(false);
                var resultProp = plainTask.GetType().GetProperty("Result", BindingFlags.Public | BindingFlags.Instance);
                return resultProp?.GetValue(plainTask)?.ToString();
            }

            // Try Windows-Runtime IAsyncOperation<T> -> Task<T> via AsTask extension.
            var asTaskMethod = FindAsTaskMethod(operation.GetType());
            if (asTaskMethod != null)
            {
                var task = asTaskMethod.Invoke(null, new[] { operation }) as Task;
                if (task != null)
                {
                    await task.ConfigureAwait(false);
                    var resultProp = task.GetType().GetProperty("Result", BindingFlags.Public | BindingFlags.Instance);
                    return resultProp?.GetValue(task)?.ToString();
                }
            }

            // Fallback for non-Task awaitables.
            var awaited = await AwaitAsync(operation).ConfigureAwait(false);
            return awaited?.ToString();
        }
        catch
        {
            return null;
        }
    }

    private static MethodInfo? FindAsTaskMethod(Type operationType)
    {
        // System.WindowsRuntimeSystemExtensions.AsTask(IAsyncOperation<TResult>) — generic.
        var extensionsType = FindType("System.WindowsRuntimeSystemExtensions");
        if (extensionsType == null)
            return null;

        // Find AsTask methods with a single parameter, then pick the IAsyncOperation<T> overload.
        var candidates = extensionsType.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.Name == "AsTask" && m.IsGenericMethodDefinition && m.GetParameters().Length == 1)
            .ToList();

        // Resolve the TResult type argument from the operation's IAsyncOperation<T> interface.
        var asyncOperationType = operationType.GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition().FullName == "Windows.Foundation.IAsyncOperation`1");
        if (asyncOperationType == null)
            return null;

        var resultType = asyncOperationType.GetGenericArguments()[0];
        foreach (var candidate in candidates)
        {
            var paramType = candidate.GetParameters()[0].ParameterType;
            if (paramType.IsGenericType && paramType.GetGenericTypeDefinition().Name.StartsWith("IAsyncOperation", StringComparison.Ordinal))
            {
                return candidate.MakeGenericMethod(resultType);
            }
        }
        return null;
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
                    if (IsSuccessfulScrollInvocation(method.Invoke(target, args)))
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
                    if (IsSuccessfulScrollInvocation(method.Invoke(target, args)))
                        return true;
                }
                catch
                {
                }
            }
        }

        return false;
    }

    private static bool IsSuccessfulScrollInvocation(object? result)
    {
        return result is not bool succeeded || succeeded;
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

    // Selection-container items — NavigationViewItem, ListViewItem, TreeViewItem,
    // and any other IsSelected-bearing container — have no Command and no
    // OnClick method, and ButtonAutomationPeer's constructor rejects a
    // non-Button element, so all three existing fallbacks miss them. They also
    // fall through TryNativeTap on non-Windows platforms (that whole path is
    // Windows-only SendInput). Rather than requiring a real pointer gesture,
    // this drives the same effect a user click has — toggling IsSelected — by
    // setting the property directly, mirroring TryExecuteCommand's
    // property/method reflection rather than pixel/native input simulation.
    private static bool TryInvokeSelectionItemPattern(object element)
    {
        var isSelectedProperty = element.GetType().GetProperty("IsSelected", BindingFlags.Instance | BindingFlags.Public);
        if (isSelectedProperty == null || isSelectedProperty.PropertyType != typeof(bool) || !isSelectedProperty.CanWrite)
            return false;

        if (isSelectedProperty.GetValue(element) is true)
            return true; // already selected — the tap would be a no-op anyway

        isSelectedProperty.SetValue(element, true);
        return true;
    }

    private static bool TryNativeTap(object element)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return false;

        try
        {
            var hwnd = ResolveUnoHwnd();
            if (hwnd == IntPtr.Zero)
                return false;

            // Bring our own window to the foreground first so SendInput's
            // mouse / keyboard events actually land here. Both the agent and
            // the Uno window live in the same process, so SetForegroundWindow
            // succeeds without the foreground-lock workaround.
            WindowsNativeInput.TryBringToForeground(hwnd);

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
    private static IntPtr ResolveUnoHwnd()
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
        return hwnd;
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
            var unwrapped = UnwrapHandle(hwnd);
            if (unwrapped != IntPtr.Zero)
                return unwrapped;
        }

        return IntPtr.Zero;
    }

    private static IntPtr UnwrapHandle(object? handle)
    {
        switch (handle)
        {
            case null:
                return IntPtr.Zero;
            case IntPtr value:
                return value;
            case long value:
                return new IntPtr(value);
            case int value:
                return new IntPtr(value);
        }

        // CsWin32 wraps native handles in structs like HWND { nint Value; }.
        // Probe for a Value field/property that yields an IntPtr-compatible
        // value so we don't depend on the concrete struct type.
        var type = handle.GetType();
        var valueField = type.GetField("Value", BindingFlags.Public | BindingFlags.Instance);
        if (valueField != null)
            return UnwrapHandle(valueField.GetValue(handle));

        var valueProperty = type.GetProperty("Value", BindingFlags.Public | BindingFlags.Instance);
        if (valueProperty != null)
            return UnwrapHandle(valueProperty.GetValue(handle));

        return IntPtr.Zero;
    }

    [SupportedOSPlatform("windows")]
    private static (int X, int Y)? TryGetElementClientPoint(object element)
    {
        // Same geometry pipeline as TryGetElementClickPoint but returns the
        // window-client-area coordinates (no ClientToScreen). PostMessage-based
        // input takes client coords directly, and stays valid even when the
        // window is hidden / not foreground.
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

        return ((int)Math.Round(x.Value), (int)Math.Round(y.Value));
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
            return TryWindowsNativeTextInput(element, text, replace);

        if (!PosixNativeActions.IsAvailable)
            return false;

        TryFocusElement(element);
        return PosixNativeActions.TryTextInput(text, replace);
    }

    [SupportedOSPlatform("windows")]
    private static bool TryWindowsNativeTextInput(object element, string text, bool replace)
    {
        var hwnd = ResolveUnoHwnd();
        if (hwnd == IntPtr.Zero)
            return false;

        WindowsNativeInput.TryBringToForeground(hwnd);
        return TryNativeAction(element, resolver => WindowsNativeActions.TryTextInput(resolver, text, replace));
    }

    private static bool TryNativeKeyInput(object element, string normalizedKey, string? insertText)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return TryWindowsNativeKeyInput(element, normalizedKey, insertText);

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

    [SupportedOSPlatform("windows")]
    private static bool TryWindowsNativeKeyInput(object element, string normalizedKey, string? insertText)
    {
        var hwnd = ResolveUnoHwnd();
        if (hwnd == IntPtr.Zero)
            return false;

        WindowsNativeInput.TryBringToForeground(hwnd);
        return TryNativeAction(element, resolver => WindowsNativeActions.TryKeyInput(resolver, normalizedKey, insertText));
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
