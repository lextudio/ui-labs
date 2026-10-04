using System.Reflection;
using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LeXtudio.DevFlow.Agent.Core;
using Microsoft.Maui.DevFlow.Agent.Core;

namespace LeXtudio.DevFlow.Agent.Avalonia;

/// <summary>
/// DevFlow agent for Avalonia desktop applications (Windows, macOS and Linux).
/// </summary>
/// <remarks>
/// Element bounds are device independent units: a window reports its screen position, every other
/// element its position relative to its window. The global input actions (click, press, drag-move,
/// release, drag, move) take screen pixels, which is what <see cref="Visual.PointToScreen"/> returns.
/// </remarks>
public sealed class AvaloniaAgentService : DevFlowAgentServiceBase
{
    private static readonly MethodInfo? s_buttonOnClick =
        typeof(Button).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic, binder: null, Type.EmptyTypes, modifiers: null);

    private readonly AvaloniaVisualTreeWalker _treeWalker = new();

    public AvaloniaAgentService(AgentOptions? options = null)
        : base(options)
    {
    }

    protected override string AgentId => "LeXtudio.DevFlow.Agent";
    protected override string AgentName => "LeXtudio.DevFlow.Agent";
    protected override string FrameworkName => "avalonia";

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
        webview = false,
        webviewCdp = false,
        multiWindow = true
    };

    // ===== Threading =====

    private static Task<T> RunOnUIThreadAsync<T>(Func<T> callback)
    {
        if (Application.Current == null)
            return Task.FromResult(callback());

        if (Dispatcher.UIThread.CheckAccess())
            return Task.FromResult(callback());

        return Dispatcher.UIThread.InvokeAsync(callback).GetTask();
    }

    protected override Task<T> DispatchOnUIThreadAsync<T>(Func<T> callback) => RunOnUIThreadAsync(callback);

    protected override Task<IReadOnlyList<object>> GetInvokeActionTargetsAsync()
    {
        return RunOnUIThreadAsync<IReadOnlyList<object>>(() =>
        {
            var targets = new List<object>();
            if (Application.Current != null)
                targets.Add(Application.Current);

            foreach (var window in AvaloniaVisualTreeWalker.GetWindows())
            {
                targets.Add(window);
                if (window.DataContext != null && window.DataContext != window)
                    targets.Add(window.DataContext);
            }

            return targets;
        });
    }

    // ===== Application =====

    protected override Task<string?> GetApplicationNameAsync()
        => RunOnUIThreadAsync(() =>
        {
            var app = Application.Current;
            return string.IsNullOrEmpty(app?.Name) ? app?.GetType().Name : app.Name;
        });

    protected override Task<object?> GetThemeAsync() => RunOnUIThreadAsync(BuildThemePayload);

    protected override Task<object?> SetThemeAsync(string theme)
    {
        return RunOnUIThreadAsync(() =>
        {
            var app = Application.Current;
            if (app == null)
                return null;

            ThemeVariant? variant = theme.Trim().ToLowerInvariant() switch
            {
                "light" => ThemeVariant.Light,
                "dark" => ThemeVariant.Dark,
                "system" => ThemeVariant.Default,
                _ => null
            };

            if (variant == null)
                return null;

            app.RequestedThemeVariant = variant;
            return BuildThemePayload();
        });
    }

    private static object? BuildThemePayload()
    {
        var app = Application.Current;
        if (app == null)
            return null;

        var requested = ToThemeName(app.RequestedThemeVariant);
        var effective = app.ActualThemeVariant == ThemeVariant.Dark ? "dark" : "light";

        return new
        {
            theme = effective,
            requestedTheme = requested,
            userAppTheme = requested,
            effectiveTheme = effective,
            supportedThemes = new[] { "light", "dark", "system" }
        };
    }

    private static string ToThemeName(ThemeVariant? variant)
    {
        if (variant == ThemeVariant.Light)
            return "light";
        if (variant == ThemeVariant.Dark)
            return "dark";
        return "system";
    }

    // ===== Tree =====

    protected override Task<List<ElementInfo>> BuildTreeAsync()
        => RunOnUIThreadAsync(() => _treeWalker.WalkTree());

    protected override Task<ElementInfo?> FindElementAsync(string id)
        => RunOnUIThreadAsync(() => _treeWalker.FindElementById(id));

    protected override Task<List<ElementInfo>> QueryElementsAsync(string? type = null, string? automationId = null, string? text = null, int maxResults = 50, int maxDepth = 24)
    {
        return RunOnUIThreadAsync(() =>
        {
            var all = new List<ElementInfo>();
            foreach (var root in _treeWalker.WalkTree())
                Flatten(root, all);

            return all.Where(e =>
                    (string.IsNullOrWhiteSpace(type) || string.Equals(e.Type, type, StringComparison.OrdinalIgnoreCase))
                    && (string.IsNullOrWhiteSpace(automationId) || string.Equals(e.AutomationId, automationId, StringComparison.OrdinalIgnoreCase))
                    && (string.IsNullOrWhiteSpace(text) || e.Text?.Contains(text, StringComparison.OrdinalIgnoreCase) == true))
                .Take(maxResults)
                .ToList();
        });
    }

    private static void Flatten(ElementInfo element, List<ElementInfo> list)
    {
        list.Add(element);
        if (element.Children == null)
            return;

        foreach (var child in element.Children)
            Flatten(child, list);
    }

    private StyledElement? ResolveElementObject(string elementId)
    {
        var element = _treeWalker.FindElementById(elementId);
        return element == null ? null : _treeWalker.ResolveElementByStableId(element.Id);
    }

    // ===== Screenshots =====

    protected override Task<byte[]?> CaptureScreenshotAsync(string? elementId = null, string? selector = null)
        => RunOnUIThreadAsync(() => CaptureScreenshotOnUiThread(elementId, selector));

    private byte[]? CaptureScreenshotOnUiThread(string? elementId, string? selector)
    {
        if (string.IsNullOrWhiteSpace(elementId) && !string.IsNullOrWhiteSpace(selector))
            elementId = ResolveElementIdBySelector(selector);

        if (!string.IsNullOrWhiteSpace(elementId) && ResolveElementObject(elementId) is Visual visual)
        {
            var bytes = CaptureVisual(visual);
            if (bytes != null)
                return bytes;
        }

        var window = AvaloniaVisualTreeWalker.GetMainWindow();
        return window == null ? null : CaptureVisual(window);
    }

    private static byte[]? CaptureVisual(Visual visual)
    {
        try
        {
            var scaling = TopLevel.GetTopLevel(visual)?.RenderScaling ?? 1d;
            var width = (int)Math.Ceiling(visual.Bounds.Width * scaling);
            var height = (int)Math.Ceiling(visual.Bounds.Height * scaling);
            if (width <= 0 || height <= 0)
                return null;

            using var bitmap = new RenderTargetBitmap(new PixelSize(width, height), new Vector(96 * scaling, 96 * scaling));
            bitmap.Render(visual);
            using var stream = new MemoryStream();
            bitmap.Save(stream, new PngBitmapEncoderOptions());
            return stream.ToArray();
        }
        catch
        {
            return null;
        }
    }

    private string? ResolveElementIdBySelector(string selector)
    {
        var normalized = selector.Trim();
        if (normalized.StartsWith('#'))
            return normalized[1..];

        foreach (var root in _treeWalker.WalkTree())
        {
            var match = FindBySelector(root, normalized);
            if (match != null)
                return match;
        }

        return null;
    }

    private static string? FindBySelector(ElementInfo element, string selector)
    {
        if (string.Equals(element.Type, selector, StringComparison.OrdinalIgnoreCase)
            || string.Equals(element.AutomationId, selector, StringComparison.OrdinalIgnoreCase))
            return element.Id;

        if (element.Children == null)
            return null;

        foreach (var child in element.Children)
        {
            var found = FindBySelector(child, selector);
            if (found != null)
                return found;
        }

        return null;
    }

    // ===== Element actions =====

    protected override async Task<bool> TryTapAsync(string elementId)
        => await TryTapResponseAsync(elementId).ConfigureAwait(false) != null;

    protected override async Task<object?> TryTapResponseAsync(string elementId)
    {
        // Like the WPF agent: a real click on the element's centre where the platform can inject one, so
        // the element sees the pointer events a user's click produces. Only the point is resolved on the
        // UI thread; the click runs off it so the app keeps pumping and receives the events.
        var point = await RunOnUIThreadAsync(() => ResolveClickablePoint(elementId)).ConfigureAwait(false);
        if (point is { } p && await Task.Run(() => TryNativeClick(p.X, p.Y, rightButton: false)).ConfigureAwait(false))
            return CreateSuccessResult(SimulationModes.Native, elementId);

        return await RunOnUIThreadAsync<object?>(() =>
        {
            var target = ResolveElementObject(elementId);
            if (target == null)
                return null;

            return TryInvokeOnElement(target) ? CreateSuccessResult(SimulationModes.Semantic, elementId) : null;
        }).ConfigureAwait(false);
    }

    protected override async Task<object?> TryRightTapResponseAsync(string elementId)
    {
        var point = await RunOnUIThreadAsync(() => ResolveClickablePoint(elementId)).ConfigureAwait(false);
        if (point is { } p && await Task.Run(() => TryNativeClick(p.X, p.Y, rightButton: true)).ConfigureAwait(false))
            return CreateSuccessResult(SimulationModes.Native, elementId);

        // Without native input: what a right click leads to, a context request.
        return await RunOnUIThreadAsync<object?>(() =>
        {
            if (ResolveElementObject(elementId) is not Control control)
                return null;

            control.RaiseEvent(new ContextRequestedEventArgs());
            return CreateSuccessResult(SimulationModes.Semantic, elementId);
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// The screen point a native click on the element should go to: its centre, provided the element is
    /// what the pointer would hit there. Null if the element is hidden or covered, or there is no native
    /// input on this platform. Must be called on the UI thread.
    /// </summary>
    private PixelPoint? ResolveClickablePoint(string elementId)
    {
        if (!CanInjectNativeClicks || ResolveElementObject(elementId) is not Visual visual)
            return null;

        if (TryGetScreenCenter(visual) is not { } center || TopLevel.GetTopLevel(visual) is not { } topLevel)
            return null;

        var local = visual.TranslatePoint(new Point(visual.Bounds.Width / 2d, visual.Bounds.Height / 2d), topLevel);
        if (local is not { } hitPoint || topLevel.InputHitTest(hitPoint) is not Visual hit)
            return null;

        return hit == visual || visual.IsVisualAncestorOf(hit) ? center : null;
    }

    private static bool CanInjectNativeClicks
        => OperatingSystem.IsWindows()
            || (OperatingSystem.IsLinux() && LinuxNativeInput.IsAvailable)
            || (OperatingSystem.IsMacOS() && CliclickInput.IsAvailable);

    private static bool TryNativeClick(double x, double y, bool rightButton)
    {
        if (OperatingSystem.IsWindows())
        {
            var ix = (int)Math.Round(x);
            var iy = (int)Math.Round(y);
            return rightButton ? WindowsNativeInput.TrySendRightClick(ix, iy) : WindowsNativeInput.TrySendClick(ix, iy);
        }

        if (OperatingSystem.IsLinux())
            return rightButton ? LinuxNativeInput.TryMouseRightClick(x, y) : LinuxNativeInput.TryMouseClick(x, y, 1);

        if (OperatingSystem.IsMacOS())
        {
            if (!rightButton)
                return CliclickInput.TryClick(x, y, 1);

            return CliclickInput.TryPressDown(x, y, CliclickInput.MouseButton.Right)
                && CliclickInput.TryRelease(x, y, CliclickInput.MouseButton.Right);
        }

        return false;
    }

    /// <summary>
    /// Clicks a button the way a pointer click does - <c>Button.OnClick</c> raises <c>Click</c>, runs the
    /// command and toggles a toggle button - and focuses any other input element.
    /// </summary>
    private static bool TryInvokeOnElement(StyledElement target)
    {
        try
        {
            if (target is Button button)
            {
                if (!button.IsEffectivelyEnabled)
                    return false;

                if (s_buttonOnClick != null)
                    s_buttonOnClick.Invoke(button, null);
                else
                    button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, button));
                return true;
            }

            if (target is InputElement input)
            {
                input.Focus();
                return true;
            }
        }
        catch
        {
        }

        return false;
    }

    protected override Task<bool> TryScrollAsync(string elementId, double deltaX, double deltaY)
    {
        return RunOnUIThreadAsync(() =>
        {
            var target = ResolveElementObject(elementId);
            var scrollViewer = target as ScrollViewer ?? (target as Visual)?.FindAncestorOfType<ScrollViewer>();
            if (scrollViewer == null)
                return false;

            var offset = scrollViewer.Offset;
            scrollViewer.Offset = new Vector(Math.Max(0, offset.X + deltaX), Math.Max(0, offset.Y + deltaY));
            return true;
        });
    }

    protected override async Task<bool> TryFillAsync(string elementId, string text)
        => await TryFillResponseAsync(elementId, text).ConfigureAwait(false) != null;

    protected override Task<object?> TryFillResponseAsync(string elementId, string text)
    {
        return RunOnUIThreadAsync<object?>(() =>
        {
            if (ResolveElementObject(elementId) is not TextBox textBox)
                return null;

            textBox.Text = text;
            return CreateSuccessResult(SimulationModes.PropertyMutation, elementId, text: text);
        });
    }

    protected override Task<bool> TryClearAsync(string elementId) => TryFillAsync(elementId, string.Empty);

    protected override Task<object?> TryClearResponseAsync(string elementId) => TryFillResponseAsync(elementId, string.Empty);

    protected override async Task<bool> TryFocusAsync(string elementId)
        => await TryFocusResponseAsync(elementId).ConfigureAwait(false) != null;

    protected override Task<object?> TryFocusResponseAsync(string elementId)
    {
        return RunOnUIThreadAsync<object?>(() =>
            ResolveElementObject(elementId) is InputElement input && input.Focus()
                ? CreateSuccessResult(SimulationModes.Semantic, elementId)
                : null);
    }

    protected override async Task<object?> TryKeyAsync(string? elementId, string? key, string? text)
    {
        // Without a target element, a key goes to the focused window, as a user's key press does.
        if (string.IsNullOrWhiteSpace(elementId) && NativeKeyboard.IsAvailable && NativeKeyboard.CanSend(key))
        {
            await RunOnUIThreadAsync(ActivateMainWindowIfNoneIsActive).ConfigureAwait(false);
            if (await Task.Run(() => NativeKeyboard.TrySendChord(key!)).ConfigureAwait(false))
                return CreateSuccessResult(SimulationModes.Native, elementId, key: key, text: text);
        }

        return await RunOnUIThreadAsync<object?>(() =>
        {
            var keyValue = key ?? text ?? string.Empty;
            var normalized = keyValue.Trim().ToLowerInvariant();
            var insertText = text ?? (keyValue.Length == 1 ? keyValue : null);

            if (string.IsNullOrWhiteSpace(elementId))
                return CreateSuccessResult(SimulationModes.Semantic, elementId, key: keyValue, text: text);

            var target = ResolveElementObject(elementId);
            if (target is not TextBox textBox)
                return null;

            if (normalized is "enter" or "return")
            {
                textBox.RaiseEvent(new KeyEventArgs
                {
                    RoutedEvent = InputElement.KeyDownEvent,
                    Key = Key.Enter,
                    Source = textBox
                });
                return CreateSuccessResult(SimulationModes.Semantic, elementId, key: keyValue, text: text);
            }

            if (normalized is "backspace" or "delete")
            {
                if (!string.IsNullOrEmpty(textBox.Text))
                    textBox.Text = textBox.Text[..^1];
                return CreateSuccessResult(SimulationModes.PropertyMutation, elementId, key: keyValue, text: text);
            }

            if (!string.IsNullOrEmpty(insertText))
            {
                textBox.Text += insertText;
                return CreateSuccessResult(SimulationModes.PropertyMutation, elementId, key: keyValue, text: text);
            }

            return null;
        }).ConfigureAwait(false);
    }

    /// <summary>Gives the application the keyboard focus unless one of its windows has it already.</summary>
    private static bool ActivateMainWindowIfNoneIsActive()
    {
        if (AvaloniaVisualTreeWalker.GetWindows().Any(w => w.IsActive))
            return true;

        AvaloniaVisualTreeWalker.GetMainWindow()?.Activate();
        return true;
    }

    protected override Task<bool> TryBackAsync()
    {
        return RunOnUIThreadAsync(() =>
        {
            // Closes the active secondary window, or else the most recently opened one, so the result does
            // not depend on which window the window manager gave focus to. The main window is never closed.
            var main = AvaloniaVisualTreeWalker.GetMainWindow();
            var secondary = AvaloniaVisualTreeWalker.GetWindows().Where(w => w != main && w.IsVisible).ToList();
            var target = secondary.FirstOrDefault(w => w.IsActive) ?? secondary.LastOrDefault();
            if (target == null)
                return false;

            target.Close();
            return true;
        });
    }

    protected override async Task<object?> TryBackResponseAsync()
        => await TryBackAsync().ConfigureAwait(false) ? CreateSuccessResult(SimulationModes.Semantic) : null;

    // ===== Native (OS-level) pointer input =====

    private sealed record ResolvedDrag(double FromX, double FromY, double ToX, double ToY, int Steps, string? Error);

    protected override async Task<object?> TryDragResponseAsync(DragRequest request)
    {
        var resolved = await RunOnUIThreadAsync(() =>
        {
            var steps = request.Steps is > 0 ? request.Steps.Value : 24;
            if (request.Global && request.FromX.HasValue && request.FromY.HasValue)
            {
                var toX = request.ToX ?? (request.FromX.Value + (request.Dx ?? 0));
                var toY = request.ToY ?? (request.FromY.Value + (request.Dy ?? 0));
                return new ResolvedDrag(request.FromX.Value, request.FromY.Value, toX, toY, steps, null);
            }

            if (!TryResolveScreenPoint(request.FromId, request.FromX, request.FromY, out var fromX, out var fromY))
                return new ResolvedDrag(0, 0, 0, 0, 0, "could not resolve source point (need fromId or fromX/fromY)");

            if (TryResolveScreenPoint(request.ToId, request.ToX, request.ToY, out var targetX, out var targetY))
                return new ResolvedDrag(fromX, fromY, targetX, targetY, steps, null);

            if (request.Dx.HasValue || request.Dy.HasValue)
                return new ResolvedDrag(fromX, fromY, fromX + (request.Dx ?? 0), fromY + (request.Dy ?? 0), steps, null);

            return new ResolvedDrag(0, 0, 0, 0, 0, "could not resolve target point (need toId, toX/toY, or dx/dy)");
        }).ConfigureAwait(false);

        if (resolved.Error != null)
            return new { ok = false, reason = resolved.Error };

        if (CliclickInput.IsAvailable
            && await Task.Run(() => CliclickInput.TryDrag(resolved.FromX, resolved.FromY, resolved.ToX, resolved.ToY, resolved.Steps)).ConfigureAwait(false))
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

        var ok = await Task.Run(() => TryNativeMouseDrag(resolved.FromX, resolved.FromY, resolved.ToX, resolved.ToY, resolved.Steps)).ConfigureAwait(false);
        return new
        {
            ok,
            mode = "native",
            from = new { x = resolved.FromX, y = resolved.FromY },
            to = new { x = resolved.ToX, y = resolved.ToY },
            steps = resolved.Steps,
            note = BuildNativeMouseNote(ok)
        };
    }

    protected override async Task<object?> TryClickResponseAsync(ClickRequest request)
    {
        var point = await ResolveClickPointAsync(request).ConfigureAwait(false);
        if (point == null)
            return new { ok = false, reason = "could not resolve click coordinates" };

        var (x, y) = point.Value;
        if (CliclickInput.IsAvailable && await Task.Run(() => CliclickInput.TryClick(x, y, request.ClickCount)).ConfigureAwait(false))
            return new { ok = true, mode = "cliclick", x, y };

        var ok = await Task.Run(() => TryNativeMouseClick(x, y, request.ClickCount)).ConfigureAwait(false);
        return new { ok, mode = request.Global ? "native-global" : "native", x, y, note = BuildNativeMouseNote(ok) };
    }

    private async Task<(double X, double Y)?> ResolveClickPointAsync(ClickRequest request)
    {
        if (request.Global)
            return (request.X!.Value, request.Y!.Value);

        return await RunOnUIThreadAsync<(double X, double Y)?>(() =>
            TryResolveScreenPoint(null, request.X, request.Y, out var x, out var y) ? (x, y) : null).ConfigureAwait(false);
    }

    protected override Task<object?> TryPressResponseAsync(ClickRequest request)
        => RunHeldButtonActionAsync(request, PointerStep.Press);

    protected override Task<object?> TryDragMoveResponseAsync(ClickRequest request)
        => RunHeldButtonActionAsync(request, PointerStep.Move);

    protected override Task<object?> TryReleaseResponseAsync(ClickRequest request)
        => RunHeldButtonActionAsync(request, PointerStep.Release);

    private enum PointerStep
    {
        Press,
        Move,
        Release
    }

    /// <summary>
    /// The decomposed drag: press, any number of moves while held, release. Each step is a separate
    /// request, so a caller can inspect the UI (e.g. the drop targets on show) between them.
    /// </summary>
    private static async Task<object?> RunHeldButtonActionAsync(ClickRequest request, PointerStep step)
    {
        var x = request.X!.Value;
        var y = request.Y!.Value;
        if (!CliclickInput.TryParseButton(request.Button, out var button))
            return new { ok = false, reason = $"Unknown button '{request.Button}'; use left, right or middle" };

        // XTest keeps the button state across separate calls, which is what holding a drag open between
        // requests needs.
        if (OperatingSystem.IsLinux() && LinuxNativeInput.IsAvailable)
        {
            if (button != CliclickInput.MouseButton.Left)
                return new { ok = false, reason = "only the left button is supported on Linux (XTest)" };

            var linuxOk = await LinuxPointerStepAsync(step, x, y).ConfigureAwait(false);
            return new { ok = linuxOk, mode = "xtest", x, y, button = "left" };
        }

        if (OperatingSystem.IsWindows())
        {
            var left = button == CliclickInput.MouseButton.Left;
            var ix = (int)Math.Round(x);
            var iy = (int)Math.Round(y);
            var windowsOk = await Task.Run(() => step switch
            {
                PointerStep.Press => WindowsNativeInput.TryMousePress(ix, iy, left),
                PointerStep.Move => WindowsNativeInput.TryMouseDragMove(ix, iy),
                _ => WindowsNativeInput.TryMouseRelease(ix, iy, left)
            }).ConfigureAwait(false);
            return new { ok = windowsOk, mode = "sendinput", x, y, button = button.ToString().ToLowerInvariant() };
        }

        if (!CliclickInput.IsAvailable)
            return new { ok = false, reason = "decomposed press/drag-move/release needs cliclick on macOS, or an X display (X11/XWayland) on Linux" };

        var ok = await Task.Run(() => step switch
        {
            PointerStep.Press => CliclickInput.TryPressDown(x, y, button),
            PointerStep.Move => CliclickInput.TryDragMoveTo(x, y, button),
            _ => CliclickInput.TryRelease(x, y, button)
        }).ConfigureAwait(false);
        return new { ok, mode = "cliclick", x, y, button = button.ToString().ToLowerInvariant() };
    }

    protected override async Task<object?> TryKeyDownResponseAsync(string key)
    {
        if (!CliclickInput.IsAvailable)
            return new { ok = false, reason = "keydown/keyup needs cliclick on macOS - not implemented for other platforms yet" };

        var ok = await Task.Run(() => CliclickInput.TryKeyDown(key)).ConfigureAwait(false);
        return new { ok, mode = "cliclick", key };
    }

    protected override async Task<object?> TryKeyUpResponseAsync(string key)
    {
        if (!CliclickInput.IsAvailable)
            return new { ok = false, reason = "keydown/keyup needs cliclick on macOS - not implemented for other platforms yet" };

        var ok = await Task.Run(() => CliclickInput.TryKeyUp(key)).ConfigureAwait(false);
        return new { ok, mode = "cliclick", key };
    }

    protected override async Task<object?> TryMoveResponseAsync(MoveRequest request)
    {
        var resolved = await RunOnUIThreadAsync<(bool Ok, double X, double Y)>(() =>
        {
            if (request.Global && string.IsNullOrWhiteSpace(request.ElementId) && request.X.HasValue && request.Y.HasValue)
                return (true, request.X.Value, request.Y.Value);

            var ok = TryResolveScreenPoint(request.ElementId, request.X, request.Y, out var x, out var y);
            return (ok, x, y);
        }).ConfigureAwait(false);

        if (!resolved.Ok)
            return null;

        if (CliclickInput.IsAvailable && await Task.Run(() => CliclickInput.TryMove(resolved.X, resolved.Y)).ConfigureAwait(false))
            return new { ok = true, mode = "cliclick", x = resolved.X, y = resolved.Y };

        var ok = await Task.Run(() => TryNativeMouseMove(resolved.X, resolved.Y)).ConfigureAwait(false);
        return new { ok, mode = "native", x = resolved.X, y = resolved.Y, note = BuildNativeMouseNote(ok) };
    }

    /// <summary>
    /// Resolves the centre of an element, or a point relative to the main window, to screen pixels.
    /// Must be called on the UI thread.
    /// </summary>
    private bool TryResolveScreenPoint(string? elementId, double? winX, double? winY, out double x, out double y)
    {
        x = 0;
        y = 0;

        if (!string.IsNullOrWhiteSpace(elementId))
        {
            if (ResolveElementObject(elementId) is not Visual visual || TryGetScreenCenter(visual) is not { } center)
                return false;

            x = center.X;
            y = center.Y;
            return true;
        }

        if (winX.HasValue && winY.HasValue && AvaloniaVisualTreeWalker.GetMainWindow() is { } window)
        {
            var screen = window.PointToScreen(new Point(winX.Value, winY.Value));
            x = screen.X;
            y = screen.Y;
            return true;
        }

        return false;
    }

    private static PixelPoint? TryGetScreenCenter(Visual visual)
    {
        if (!visual.IsEffectivelyVisible || visual.Bounds.Width <= 0 || visual.Bounds.Height <= 0 || TopLevel.GetTopLevel(visual) == null)
            return null;

        try
        {
            return visual.PointToScreen(new Point(visual.Bounds.Width / 2d, visual.Bounds.Height / 2d));
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

        if (OperatingSystem.IsLinux())
            return LinuxNativeInput.TryMouseDrag(fromX, fromY, toX, toY, steps);

        return false;
    }

    private static bool TryNativeMouseClick(double x, double y, int clickCount)
    {
        if (OperatingSystem.IsWindows())
            return WindowsNativeInput.TryMouseClick((int)Math.Round(x), (int)Math.Round(y), clickCount);

        if (OperatingSystem.IsMacOS())
            return MacOSNativeInput.TryMouseClick(x, y, clickCount);

        if (OperatingSystem.IsLinux())
            return LinuxNativeInput.TryMouseClick(x, y, clickCount);

        return false;
    }

    private static bool TryNativeMouseMove(double x, double y)
    {
        if (OperatingSystem.IsMacOS())
            return MacOSNativeInput.TryMouseMove(x, y);

        if (OperatingSystem.IsLinux())
            return LinuxNativeInput.TryMouseMove(x, y);

        return false;
    }

    private static string? BuildNativeMouseNote(bool ok)
    {
        if (ok)
            return null;

        if (OperatingSystem.IsMacOS())
            return "CGEventPost may require Accessibility (TCC) permission for the host process.";

        if (OperatingSystem.IsLinux())
            return "XTest injection needs a reachable X display (X11 or XWayland); it is unavailable on a pure Wayland session.";

        if (!OperatingSystem.IsWindows())
            return "native mouse injection is supported on Windows, macOS and Linux (X11/XWayland) only.";

        return null;
    }

    // CA1416 cannot follow an OperatingSystem.IsLinux() guard into a lambda, so the LinuxNativeInput calls
    // (which are [SupportedOSPlatform("linux")]) live in an attributed wrapper.
    [SupportedOSPlatform("linux")]
    private static Task<bool> LinuxPointerStepAsync(PointerStep step, double x, double y)
        => Task.Run(() => step switch
        {
            PointerStep.Press => LinuxNativeInput.TryMousePressDown(x, y),
            PointerStep.Move => LinuxNativeInput.TryMouseMove(x, y),
            _ => LinuxNativeInput.TryMouseRelease(x, y)
        });
}
