using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection;
using System.Text.Json;
using System.Windows.Forms;
using LeXtudio.DevFlow.Agent.Core;
using Microsoft.Maui.DevFlow.Agent.Core;

namespace LeXtudio.DevFlow.Agent.WinForms;

public sealed class WinFormsAgentService(AgentOptions? options = null) : DevFlowAgentServiceBase(options)
{
    private readonly WinFormsVisualTreeWalker _walker = new();

    protected override string AgentId => "LeXtudio.DevFlow.Agent";
    protected override string AgentName => "LeXtudio.DevFlow.Agent";
    protected override string FrameworkName => "winforms";

    protected override object GetCapabilities() => new
    {
        screenshots = true,
        elementScreenshots = true,
        selectorScreenshots = true,
        tap = true,
        scroll = true,
        structuredErrors = true,
        appTheme = false,
        webview = true,
        webviewCdp = true,
        multiWindow = true
    };

    protected override Task<string?> GetApplicationNameAsync() => Task.FromResult(Application.ProductName);
    protected override Task<List<ElementInfo>> BuildTreeAsync() => Task.FromResult(_walker.WalkTree());
    protected override Task<ElementInfo?> FindElementAsync(string id) => Task.FromResult(_walker.FindElementById(id));

    protected override Task<object?> GetWebViewContextsAsync()
    {
        return InvokeOnUiThread<object?>(GetWebViewContextsOnUiThread);
    }

    protected override Task<byte[]?> CaptureWebViewScreenshotAsync(string? contextId = null)
    {
        return InvokeOnUiThreadAsync(() => CaptureWebViewScreenshotOnUiThreadAsync(contextId));
    }

    protected override Task<object?> SendWebViewCdpCommandAsync(string? contextId, string method, JsonElement? @params)
    {
        return InvokeOnUiThreadAsync<object?>(() => SendWebViewCdpCommandOnUiThreadAsync(contextId, method, @params));
    }

    protected override Task<List<ElementInfo>> QueryElementsAsync(string? type = null, string? automationId = null, string? text = null, int maxResults = 50, int maxDepth = 24)
    {
        var all = new List<ElementInfo>();
        foreach (var root in _walker.WalkTree()) Flatten(root, all);
        return Task.FromResult(all.Where(e =>
            (string.IsNullOrWhiteSpace(type) || string.Equals(e.Type, type, StringComparison.OrdinalIgnoreCase)) &&
            (string.IsNullOrWhiteSpace(automationId) || string.Equals(e.AutomationId, automationId, StringComparison.OrdinalIgnoreCase)) &&
            (string.IsNullOrWhiteSpace(text) || (e.Text?.Contains(text, StringComparison.OrdinalIgnoreCase) == true))
        ).ToList());
    }

    protected override Task<byte[]?> CaptureScreenshotAsync(string? elementId = null, string? selector = null)
    {
        Control? control = null;
        if (!string.IsNullOrWhiteSpace(elementId))
            control = _walker.ResolveControlById(elementId);
        else if (!string.IsNullOrWhiteSpace(selector))
            control = ResolveBySelector(selector);

        if (control != null)
            return Task.FromResult(CaptureControl(control));

        var form = Application.OpenForms.Cast<Form>().FirstOrDefault(f => f.Visible);
        return Task.FromResult(form == null ? null : CaptureControl(form));
    }

    protected override Task<bool> TryTapAsync(string elementId)
    {
        return InvokeOnUiThread(() =>
        {
            var control = _walker.ResolveControlById(elementId);
            if (control is Button button)
            {
                button.PerformClick();
                return true;
            }

            if (control == null)
                return false;

            _ = control.Focus();
            return true;
        });
    }

    protected override Task<object?> TryTapResponseAsync(string elementId)
    {
        return InvokeOnUiThread<object?>(() =>
        {
            var control = _walker.ResolveControlById(elementId);
            if (control == null || !control.Enabled)
                return null;

            return ActionSimulationExecutor.Execute(
                () => WindowsNativeActions.TryTap(control, TryGetScreenPoint) ? CreateSuccessResult(SimulationModes.Native, elementId) : null,
                () =>
                {
                    if (control is Button button)
                    {
                        button.PerformClick();
                        return CreateSuccessResult(SimulationModes.Semantic, elementId);
                    }

                    _ = control.Focus();
                    return CreateSuccessResult(SimulationModes.Semantic, elementId);
                });
        });
    }

    protected override Task<bool> TryScrollAsync(string elementId, double deltaX, double deltaY)
    {
        return InvokeOnUiThread(() =>
        {
            var control = _walker.ResolveControlById(elementId);
            if (control is ScrollableControl scrollable)
            {
                var current = scrollable.AutoScrollPosition;
                var x = Math.Max(0, -current.X + (int)deltaX);
                var y = Math.Max(0, -current.Y + (int)deltaY);
                scrollable.AutoScrollPosition = new Point(x, y);
                return true;
            }

            return false;
        });
    }

    protected override Task<bool> TryFillAsync(string elementId, string text)
    {
        return InvokeOnUiThread(() =>
        {
            var control = _walker.ResolveControlById(elementId);
            switch (control)
            {
                case TextBoxBase tb:
                    tb.Text = text;
                    return true;
                case ComboBox cb:
                    cb.Text = text;
                    return true;
                default:
                    return false;
            }
        });
    }

    protected override Task<object?> TryFillResponseAsync(string elementId, string text)
    {
        return InvokeOnUiThread<object?>(() =>
        {
            var control = _walker.ResolveControlById(elementId);
            if (control == null || !control.Enabled)
                return null;

            return ActionSimulationExecutor.Execute(
                () => WindowsNativeActions.TryTextInput(control, TryGetScreenPoint, text, replace: true) ? CreateSuccessResult(SimulationModes.Native, elementId, text: text) : null,
                () =>
                {
                    var success = control switch
                    {
                        TextBoxBase tb => SetText(tb, text),
                        ComboBox cb => SetText(cb, text),
                        _ => false
                    };

                    return success ? CreateSuccessResult(SimulationModes.PropertyMutation, elementId, text: text) : null;
                });
        });
    }

    protected override Task<bool> TryClearAsync(string elementId) => TryFillAsync(elementId, string.Empty);
    protected override Task<object?> TryClearResponseAsync(string elementId) => TryFillResponseAsync(elementId, string.Empty);
    protected override Task<bool> TryFocusAsync(string elementId)
    {
        return InvokeOnUiThread(() =>
        {
            var control = _walker.ResolveControlById(elementId);
            if (control == null)
                return false;

            _ = control.Focus();
            return true;
        });
    }

    protected override Task<object?> TryFocusResponseAsync(string elementId)
    {
        return InvokeOnUiThread<object?>(() =>
        {
            var control = _walker.ResolveControlById(elementId);
            if (control == null || !control.Enabled)
                return null;

            return ActionSimulationExecutor.Execute(
                () => WindowsNativeActions.TryTap(control, TryGetScreenPoint) ? CreateSuccessResult(SimulationModes.Native, elementId) : null,
                () =>
                {
                    _ = control.Focus();
                    return CreateSuccessResult(SimulationModes.Semantic, elementId);
                });
        });
    }

    protected override Task<object?> TryKeyAsync(string? elementId, string? key, string? text)
    {
        if (string.IsNullOrWhiteSpace(elementId))
            return Task.FromResult<object?>(new { success = false, reason = "elementId required" });

        return InvokeOnUiThread<object?>(() =>
        {
            var control = _walker.ResolveControlById(elementId);
            if (control is not TextBoxBase tb || !tb.Enabled)
                return null;

            var normalized = (key ?? text ?? string.Empty).Trim().ToLowerInvariant();
            var keyValue = key ?? text ?? string.Empty;
            var insert = text ?? (string.IsNullOrWhiteSpace(key) ? null : key);

            if (WindowsNativeActions.TryKeyInput(tb, TryGetScreenPoint, normalized, insert))
                return CreateSuccessResult(SimulationModes.Native, elementId, key: keyValue, text: text);

            if (normalized is "backspace" or "delete")
            {
                if (!string.IsNullOrEmpty(tb.Text))
                    tb.Text = tb.Text[..^1];
                return CreateSuccessResult(SimulationModes.PropertyMutation, elementId, key: keyValue, text: text);
            }

            if (!string.IsNullOrEmpty(insert) && insert!.Length == 1)
            {
                tb.Text += insert;
                return CreateSuccessResult(SimulationModes.PropertyMutation, elementId, key: keyValue, text: text);
            }

            if (normalized is "enter" or "return")
                return CreateSuccessResult(SimulationModes.Semantic, elementId, key: keyValue, text: text);

            return null;
        });
    }

    protected override Task<bool> TryBackAsync() => Task.FromResult(false);
    protected override Task<object?> TryBackResponseAsync() => Task.FromResult<object?>(null);
    protected override Task<object?> GetThemeAsync() => Task.FromResult<object?>(new { theme = "system", supportedThemes = new[] { "system" } });
    protected override Task<object?> SetThemeAsync(string theme) => Task.FromResult<object?>(null);

    private Control? ResolveBySelector(string selector)
    {
        var s = selector.Trim();
        if (s.StartsWith("#", StringComparison.Ordinal))
            return _walker.ResolveControlById(s[1..]);
        return null;
    }

    private static object GetWebViewContextsOnUiThread()
    {
        var webViewType = FindType("Microsoft.Web.WebView2.WinForms.WebView2");
        if (webViewType == null)
            return new { contexts = Array.Empty<object>() };

        var contexts = new List<object>();
        foreach (var webView in EnumerateControls().Where(webViewType.IsInstanceOfType))
        {
            var control = (Control)webView;
            var id = !string.IsNullOrWhiteSpace(control.Name) ? control.Name : $"webview-{contexts.Count + 1}";
            contexts.Add(new { id, type = "webview2", title = control.Name ?? id });
        }

        return new { contexts };
    }

    private static Task<byte[]?> CaptureWebViewScreenshotOnUiThreadAsync(string? contextId)
    {
        var target = FindWebView(contextId);
        return TryCaptureWebView2ScreenshotAsync(target);
    }

    private static async Task<object?> SendWebViewCdpCommandOnUiThreadAsync(string? contextId, string method, JsonElement? @params)
    {
        var target = FindWebView(contextId);
        if (target == null)
            return null;

        var core = target.GetType().GetProperty("CoreWebView2", BindingFlags.Public | BindingFlags.Instance)?.GetValue(target);
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
            var result = task == null ? null : await task.ConfigureAwait(true);
            return new { result = new { value = result } };
        }

        return new { error = $"Unsupported CDP method: {method}" };
    }

    private static Control? FindWebView(string? contextId)
    {
        var webViewType = FindType("Microsoft.Web.WebView2.WinForms.WebView2");
        if (webViewType == null)
            return null;

        return EnumerateControls()
            .FirstOrDefault(c =>
                webViewType.IsInstanceOfType(c) &&
                (string.IsNullOrWhiteSpace(contextId) || string.Equals(c.Name, contextId, StringComparison.OrdinalIgnoreCase)));
    }

    private static async Task<byte[]?> TryCaptureWebView2ScreenshotAsync(Control? target)
    {
        try
        {
            if (target == null)
                return null;

            var coreWebView2 = target.GetType().GetProperty("CoreWebView2", BindingFlags.Public | BindingFlags.Instance)?.GetValue(target);
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

            using (stream)
            {
                await task.ConfigureAwait(true);
                return stream.Length > 0 ? stream.ToArray() : null;
            }
        }
        catch
        {
            return target == null ? null : CaptureControl(target);
        }
    }

    private static IEnumerable<Control> EnumerateControls()
    {
        foreach (Form form in Application.OpenForms)
        {
            var queue = new Queue<Control>();
            queue.Enqueue(form);
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                yield return current;
                foreach (Control child in current.Controls)
                    queue.Enqueue(child);
            }
        }
    }

    private static bool SetText(TextBoxBase textBox, string text)
    {
        textBox.Text = text;
        return true;
    }

    private static bool SetText(ComboBox comboBox, string text)
    {
        comboBox.Text = text;
        return true;
    }

    private static WindowsScreenPoint? TryGetScreenPoint(Control control)
    {
        if (!OperatingSystem.IsWindows() || !control.Visible || control.Width <= 0 || control.Height <= 0)
            return null;

        var center = control.PointToScreen(new Point(control.Width / 2, control.Height / 2));
        return new WindowsScreenPoint(center.X, center.Y);
    }

    private static byte[]? CaptureControl(Control control)
    {
        if (control.Width <= 0 || control.Height <= 0)
            return null;

        using var bmp = new Bitmap(control.Width, control.Height);
        control.DrawToBitmap(bmp, new Rectangle(Point.Empty, bmp.Size));
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        return ms.ToArray();
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

    private static Task<T> InvokeOnUiThread<T>(Func<T> action)
    {
        var invoker = Application.OpenForms.Cast<Form>().FirstOrDefault();
        if (invoker == null || invoker.IsDisposed || !invoker.IsHandleCreated)
            return Task.FromResult(action());

        if (!invoker.InvokeRequired)
            return Task.FromResult(action());

        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        invoker.BeginInvoke(() =>
        {
            try
            {
                completion.SetResult(action());
            }
            catch (Exception ex)
            {
                completion.SetException(ex);
            }
        });

        return completion.Task;
    }

    private static Task<T> InvokeOnUiThreadAsync<T>(Func<Task<T>> action)
    {
        var invoker = Application.OpenForms.Cast<Form>().FirstOrDefault();
        if (invoker == null || invoker.IsDisposed || !invoker.IsHandleCreated)
            return action();

        if (!invoker.InvokeRequired)
            return action();

        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        invoker.BeginInvoke(async () =>
        {
            try
            {
                completion.SetResult(await action().ConfigureAwait(true));
            }
            catch (Exception ex)
            {
                completion.SetException(ex);
            }
        });

        return completion.Task;
    }

    private static void Flatten(ElementInfo node, List<ElementInfo> acc)
    {
        acc.Add(node);
        if (node.Children == null) return;
        foreach (var c in node.Children) Flatten(c, acc);
    }

}
