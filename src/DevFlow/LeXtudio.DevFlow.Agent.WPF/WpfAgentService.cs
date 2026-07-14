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
    private Window? _cachedMainWindow;

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
        drag = true,
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
        var app = Application.Current;
        return Task.FromResult(app?.GetType().Name);
    }

    protected override Task<List<Microsoft.Maui.DevFlow.Agent.Core.ElementInfo>> BuildTreeAsync()
    {
        return Application.Current?.Dispatcher.InvokeAsync(() => _treeWalker.WalkTree()).Task
               ?? Task.FromResult(new List<Microsoft.Maui.DevFlow.Agent.Core.ElementInfo>());
    }

    protected override Task<T> DispatchOnUIThreadAsync<T>(Func<T> callback)
    {
        return DispatchToApplicationAsync(callback);
    }

    protected override Task<IReadOnlyList<object>> GetInvokeActionTargetsAsync()
    {
        return Application.Current?.Dispatcher.InvokeAsync<IReadOnlyList<object>>(() =>
        {
            var targets = new List<object>();
            if (Application.Current != null)
            {
                targets.Add(Application.Current);
                foreach (Window window in Application.Current.Windows)
                {
                    targets.Add(window);
                }
            }

            return targets;
        }).Task ?? Task.FromResult<IReadOnlyList<object>>(Array.Empty<object>());
    }

    protected override Task<Microsoft.Maui.DevFlow.Agent.Core.ElementInfo?> FindElementAsync(string id)
    {
        return Application.Current?.Dispatcher.InvokeAsync(() => _treeWalker.FindElementById(id)).Task
               ?? Task.FromResult<Microsoft.Maui.DevFlow.Agent.Core.ElementInfo?>(null);
    }

    protected override Task<List<ElementInfo>> QueryElementsAsync(string? type = null, string? automationId = null, string? text = null, int maxResults = 50, int maxDepth = 24)
    {
        return Application.Current?.Dispatcher.InvokeAsync(() =>
            _treeWalker.QueryElements(type, automationId, text, maxResults, maxDepth)).Task ?? Task.FromResult(new List<ElementInfo>());
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

    protected override async Task<object?> TryDragResponseAsync(DragRequest request)
    {
        var resolved = await DispatchToApplicationAsync<ResolvedDrag?>(() =>
        {
            if (request.Global && request.FromX.HasValue && request.FromY.HasValue)
            {
                double toX = request.ToX ?? (request.FromX.Value + (request.Dx ?? 0));
                double toY = request.ToY ?? (request.FromY.Value + (request.Dy ?? 0));
                var steps = request.Steps is > 0 ? request.Steps.Value : 24;
                return new ResolvedDrag(request.FromX.Value, request.FromY.Value, toX, toY, steps, "native-global", null);
            }

            if (!TryResolveScreenPoint(request.FromId, request.FromX, request.FromY, out var fromX, out var fromY))
                return new ResolvedDrag(0, 0, 0, 0, 0, "native", "could not resolve source point (need fromId or fromX/fromY)");

            double targetX, targetY;
            if (TryResolveScreenPoint(request.ToId, request.ToX, request.ToY, out var tx, out var ty))
            {
                targetX = tx; targetY = ty;
            }
            else if (request.Dx.HasValue || request.Dy.HasValue)
            {
                targetX = fromX + (request.Dx ?? 0);
                targetY = fromY + (request.Dy ?? 0);
            }
            else
            {
                return new ResolvedDrag(0, 0, 0, 0, 0, "native", "could not resolve target point (need toId, toX/toY, or dx/dy)");
            }

            var stepCount = request.Steps is > 0 ? request.Steps.Value : 24;
            return new ResolvedDrag(fromX, fromY, targetX, targetY, stepCount, "native", null);
        }).ConfigureAwait(false);

        if (resolved == null)
            return new { ok = false, reason = "no Application" };
        if (resolved.Error != null)
            return new { ok = false, reason = resolved.Error };

        // Prefer real OS-level events via cliclick when available: they drive the genuine native input
        // path (cross-window capture, overlay windows, GLFW routing) that the portable ProcessInput
        // injector bypasses, so DevFlow tests reproduce what a real user hits.
        if (CliclickInput.IsAvailable)
        {
            var clickDragOk = await Task.Run(() =>
                CliclickInput.TryDrag(resolved.FromX, resolved.FromY, resolved.ToX, resolved.ToY, resolved.Steps)).ConfigureAwait(false);
            if (clickDragOk)
            {
                return new
                {
                    ok = true,
                    mode = "cliclick",
                    from = new { x = resolved.FromX, y = resolved.FromY },
                    to = new { x = resolved.ToX, y = resolved.ToY },
                    steps = resolved.Steps
                };
            }
        }

        // Global coordinates are Quartz screen coordinates and must stay on the native path.
        // The portable WPF injector expects coordinates resolved against a WPF window.
        if (OperatingSystem.IsMacOS() && !request.Global)
        {
            var portableSuccess = await TryPortableWpfMouseDragAsync(resolved.FromX, resolved.FromY, resolved.ToX, resolved.ToY, resolved.Steps).ConfigureAwait(false);
            if (portableSuccess)
            {
                return new
                {
                    ok = true,
                    mode = "portable-wpf",
                    from = new { x = resolved.FromX, y = resolved.FromY },
                    to = new { x = resolved.ToX, y = resolved.ToY },
                    steps = resolved.Steps
                };
            }
        }

        var success = await Task.Run(() => TryNativeMouseDrag(resolved.FromX, resolved.FromY, resolved.ToX, resolved.ToY, resolved.Steps)).ConfigureAwait(false);
        return new
        {
            ok = success,
            mode = resolved.Mode,
            from = new { x = resolved.FromX, y = resolved.FromY },
            to = new { x = resolved.ToX, y = resolved.ToY },
            steps = resolved.Steps,
            note = BuildNativeMouseNote(success)
        };
    }

    protected override async Task<object?> TryClickResponseAsync(ClickRequest request)
    {
        var resolved = await DispatchToApplicationAsync<ResolvedClick?>(() =>
        {
            double x, y;
            if (request.Global)
            {
                x = request.X!.Value;
                y = request.Y!.Value;
            }
            else if (TryResolveScreenPoint(null, request.X, request.Y, out var sx, out var sy))
            {
                x = sx; y = sy;
            }
            else
            {
                return new ResolvedClick(0, 0, request.Global ? "native-global" : "native", request.ClickCount, "could not resolve click coordinates");
            }

            return new ResolvedClick(x, y, request.Global ? "native-global" : "native", request.ClickCount, null);
        }).ConfigureAwait(false);

        if (resolved == null)
            return new { ok = false, reason = "no Application" };
        if (resolved.Error != null)
            return new { ok = false, reason = resolved.Error };

        if (CliclickInput.IsAvailable)
        {
            var clickOk = await Task.Run(() => CliclickInput.TryClick(resolved.X, resolved.Y, resolved.ClickCount)).ConfigureAwait(false);
            if (clickOk)
                return new { ok = true, mode = "cliclick", x = resolved.X, y = resolved.Y };
        }

        var ok = await Task.Run(() => TryNativeMouseClick(resolved.X, resolved.Y, resolved.ClickCount)).ConfigureAwait(false);
        return new { ok, mode = resolved.Mode, x = resolved.X, y = resolved.Y, note = BuildNativeMouseNote(ok) };
    }

    protected override async Task<object?> TryMoveResponseAsync(MoveRequest request)
    {
        // Prefer real OS-level cursor movement via cliclick when available (drives the genuine native
        // path); fall back to the portable synthetic move otherwise.
        if (CliclickInput.IsAvailable)
        {
            var resolvedMove = await DispatchToApplicationAsync<(bool Ok, double X, double Y)>(() =>
            {
                var ok = TryResolveScreenPoint(request.ElementId, request.X, request.Y, out var x, out var y);
                return (ok, x, y);
            }).ConfigureAwait(false);

            if (resolvedMove.Ok)
            {
                var moveOk = await Task.Run(() => CliclickInput.TryMove(resolvedMove.X, resolvedMove.Y)).ConfigureAwait(false);
                if (moveOk)
                    return new { ok = true, mode = "cliclick", x = resolvedMove.X, y = resolvedMove.Y };
            }
        }

        if (!string.IsNullOrWhiteSpace(request.ElementId))
        {
            var portable = await DispatchToApplicationAsync(() =>
            {
                var target = ResolveElementObject(request.ElementId) as FrameworkElement;
                if (target == null)
                    return false;
                target.UpdateLayout();
                return target.IsVisible && target.ActualWidth > 0 && target.ActualHeight > 0 &&
                    TryProcessPortableWpfMouseMove(target);
            }).ConfigureAwait(false);
            if (portable)
                return new { ok = true, mode = "portable", elementId = request.ElementId };
        }

        var resolved = await DispatchToApplicationAsync<(bool Ok, double X, double Y)>(() =>
        {
            var ok = TryResolveScreenPoint(request.ElementId, request.X, request.Y, out var x, out var y);
            return (ok, x, y);
        }).ConfigureAwait(false);

        if (!resolved.Ok)
            return null;

        var ok = await Task.Run(() => TryNativeMouseMove(resolved.X, resolved.Y)).ConfigureAwait(false);
        return new { ok, mode = "native", x = resolved.X, y = resolved.Y, note = BuildNativeMouseNote(ok) };
    }

    private static bool TryProcessPortableWpfMouseMove(FrameworkElement element)
    {
        try
        {
            var source = PresentationSource.FromVisual(element);
            if (source?.RootVisual is not Visual root)
                return false;

            var center = new Point(element.ActualWidth / 2d, element.ActualHeight / 2d);
            var rootPoint = element.TransformToAncestor(root).Transform(center);
            var assembly = typeof(Window).Assembly;
            var serviceType = assembly.GetType("System.Windows.PortableWindowActivationService");
            var inputType = assembly.GetType("System.Windows.PortableInputEventArgs");
            var kindType = assembly.GetType("System.Windows.PortableInputEventKind");
            var buttonType = assembly.GetType("System.Windows.PortableMouseButton");
            var modifiersType = assembly.GetType("System.Windows.PortableInputModifiers");
            var processInput = serviceType?.GetMethod(
                "ProcessInputForSource", BindingFlags.NonPublic | BindingFlags.Static);
            var ctor = inputType?.GetConstructor(
                BindingFlags.NonPublic | BindingFlags.Instance,
                binder: null,
                types: [kindType!, typeof(string), typeof(int), typeof(char?), typeof(double),
                    typeof(double), typeof(double), typeof(double), buttonType!, modifiersType!],
                modifiers: null);
            if (processInput == null || ctor == null || kindType == null || buttonType == null || modifiersType == null)
                return false;

            var input = ctor.Invoke([
                Enum.ToObject(kindType, 3), null, 0, null, rootPoint.X, rootPoint.Y,
                0d, 0d, Enum.ToObject(buttonType, 0), Enum.ToObject(modifiersType, 0)]);
            processInput.Invoke(null, [source, input]);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private sealed record ResolvedDrag(double FromX, double FromY, double ToX, double ToY, int Steps, string Mode, string? Error);
    private sealed record ResolvedClick(double X, double Y, string Mode, int ClickCount, string? Error);

    private async Task<bool> TryPortableWpfMouseDragAsync(double fromX, double fromY, double toX, double toY, int steps)
    {
        if (steps < 1)
            steps = 1;

        var dragWindow = await ResolvePortableWpfInputWindowAsync(fromX, fromY).ConfigureAwait(false);
        if (dragWindow == null)
            return false;

        if (!await TryProcessPortableWpfMouseInputAsync(dragWindow, 3, fromX, fromY, 0).ConfigureAwait(false))
            return false;
        await Task.Delay(16).ConfigureAwait(false);

        if (!await TryProcessPortableWpfMouseInputAsync(dragWindow, 4, fromX, fromY, 1).ConfigureAwait(false))
            return false;
        await Task.Delay(200).ConfigureAwait(false);

        for (var i = 1; i <= steps; i++)
        {
            var t = (double)i / steps;
            var x = fromX + (toX - fromX) * t;
            var y = fromY + (toY - fromY) * t;
            if (!await TryProcessPortableWpfMouseInputAsync(dragWindow, 3, x, y, 0).ConfigureAwait(false))
                return false;
            await Task.Delay(16).ConfigureAwait(false);
        }

        return await TryProcessPortableWpfMouseInputAsync(dragWindow, 5, toX, toY, 1).ConfigureAwait(false);
    }

    private Task<Window?> ResolvePortableWpfInputWindowAsync(double screenX, double screenY)
    {
        return DispatchToApplicationAsync(() =>
        {
            var app = Application.Current;
            if (app == null)
                return null;

            foreach (Window window in app.Windows.OfType<Window>().Reverse())
            {
                if (!window.IsVisible)
                    continue;

                var point = window.PointFromScreen(new System.Windows.Point(screenX, screenY));
                if (point.X >= 0 && point.Y >= 0 && point.X <= window.ActualWidth && point.Y <= window.ActualHeight)
                    return window;
            }

            return app.MainWindow ?? app.Windows.OfType<Window>().FirstOrDefault(w => w.IsVisible);
        });
    }

    private Task<bool> TryProcessPortableWpfMouseInputAsync(Window window, int kind, double screenX, double screenY, int button)
    {
        return DispatchToApplicationAsync(() =>
        {
            if (!window.IsVisible)
                return false;

            var rootPoint = window.PointFromScreen(new System.Windows.Point(screenX, screenY));
            return TryProcessPortableWpfMouseInput(window, kind, rootPoint.X, rootPoint.Y, button);
        });
    }

    private static bool TryProcessPortableWpfMouseInput(Window window, int kind, double x, double y, int button)
    {
        try
        {
            var serviceType = typeof(Window).Assembly.GetType("System.Windows.PortableWindowActivationService");
            var inputType = typeof(Window).Assembly.GetType("System.Windows.PortableInputEventArgs");
            var kindType = typeof(Window).Assembly.GetType("System.Windows.PortableInputEventKind");
            var buttonType = typeof(Window).Assembly.GetType("System.Windows.PortableMouseButton");
            var modifiersType = typeof(Window).Assembly.GetType("System.Windows.PortableInputModifiers");
            var processInput = serviceType?.GetMethod(
                "ProcessInput",
                BindingFlags.NonPublic | BindingFlags.Static,
                binder: null,
                types: [typeof(Window), inputType!],
                modifiers: null);
            var ctor = inputType?.GetConstructor(
                BindingFlags.NonPublic | BindingFlags.Instance,
                binder: null,
                types:
                [
                    kindType!,
                    typeof(string),
                    typeof(int),
                    typeof(char?),
                    typeof(double),
                    typeof(double),
                    typeof(double),
                    typeof(double),
                    buttonType!,
                    modifiersType!,
                ],
                modifiers: null);
            if (processInput == null || ctor == null || kindType == null || buttonType == null || modifiersType == null)
                return false;

            var input = ctor.Invoke(
            [
                Enum.ToObject(kindType, kind),
                null,
                0,
                null,
                x,
                y,
                0d,
                0d,
                Enum.ToObject(buttonType, button),
                Enum.ToObject(modifiersType, 0),
            ]);

            processInput.Invoke(null, [window, input]);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private Task<T> DispatchToApplicationAsync<T>(Func<T> callback)
    {
        var app = Application.Current;
        if (app == null)
            return Task.FromResult(callback());

        if (app.Dispatcher.CheckAccess())
        {
            CacheMainWindow(app);
            return Task.FromResult(callback());
        }

        var operation = app.Dispatcher.InvokeAsync(callback);
        TryWakeProGpuHost(_cachedMainWindow ?? TryGetMainWindowUnsafe(app));
        return operation.Task;
    }

    private void CacheMainWindow(Application? app)
    {
        if (app == null)
            return;

        try
        {
            _cachedMainWindow = app.MainWindow;
        }
        catch
        {
        }
    }

    private void TryWakeProGpuHost(Window? window)
    {
        if (window == null)
            return;

        try
        {
            var type = Type.GetType("System.Windows.Media.ProGPU.ProGpuWpfDiagnostics, ProGPU.Wpf");
            var renderMethod = type?.GetMethod("TryRequestRender", BindingFlags.Public | BindingFlags.Static);
            var wakeMethod = type?.GetMethod("TryWakeNativeLoop", BindingFlags.Public | BindingFlags.Static);
            renderMethod?.Invoke(null, new object?[] { window });
            wakeMethod?.Invoke(null, new object?[] { window });
        }
        catch
        {
        }
    }

    private static Window? TryGetMainWindowUnsafe(Application app)
    {
        try
        {
            var field = typeof(Application).GetField("_mainWindow", BindingFlags.Instance | BindingFlags.NonPublic);
            return field?.GetValue(app) as Window;
        }
        catch
        {
            return null;
        }
    }

    private static bool TryNativeMouseDrag(double fromX, double fromY, double toX, double toY, int steps)
    {
        if (OperatingSystem.IsWindows())
            return WindowsNativeInput.TryMouseDrag(fromX, fromY, toX, toY, steps);

        if (OperatingSystem.IsMacOS())
            return MacOSNativeInput.TryMouseDrag(fromX, fromY, toX, toY, steps);

        return false;
    }

    private static bool TryNativeMouseClick(double x, double y, int clickCount)
    {
        if (OperatingSystem.IsWindows())
            return WindowsNativeInput.TryMouseClick((int)Math.Round(x), (int)Math.Round(y), clickCount);

        if (OperatingSystem.IsMacOS())
            return MacOSNativeInput.TryMouseClick(x, y, clickCount);

        return false;
    }

    private static bool TryNativeMouseMove(double x, double y)
    {
        if (OperatingSystem.IsMacOS())
            return MacOSNativeInput.TryMouseMove(x, y);

        return false;
    }

    private static string? BuildNativeMouseNote(bool ok)
    {
        if (ok)
            return null;

        if (OperatingSystem.IsMacOS())
            return "CGEventPost may require Accessibility (TCC) permission for the host process.";

        if (!OperatingSystem.IsWindows())
            return "native mouse injection is supported on Windows and macOS only.";

        return null;
    }

    private bool TryResolveScreenPoint(string? elementId, double? winX, double? winY, out double x, out double y)
    {
        x = 0; y = 0;

        if (winX.HasValue && winY.HasValue && string.IsNullOrWhiteSpace(elementId))
        {
            var window = Application.Current?.MainWindow;
            if (window == null) return false;
            var screenPt = window.PointToScreen(new System.Windows.Point(winX.Value, winY.Value));
            x = screenPt.X; y = screenPt.Y;
            return true;
        }

        if (!string.IsNullOrWhiteSpace(elementId))
        {
            var element = _treeWalker.FindElementById(elementId);
            if (element == null) return false;
            var target = _treeWalker.ResolveElementByStableId(element.Id);
            if (target is not System.Windows.FrameworkElement fe) return false;
            if (!fe.IsVisible || fe.ActualWidth <= 0 || fe.ActualHeight <= 0) return false;

            var center = new System.Windows.Point(fe.ActualWidth / 2d, fe.ActualHeight / 2d);
            var screenPt = fe.PointToScreen(center);
            x = screenPt.X; y = screenPt.Y;
            return true;
        }

        return false;
    }

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
