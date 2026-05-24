using System.Reflection;
using System.Text.Json;
using Microsoft.Maui.DevFlow.Agent.Core;

namespace LeXtudio.DevFlow.Agent.Core;

public abstract class DevFlowAgentServiceBase : IDisposable
{
    private readonly AgentHttpServer _server;
    private bool _started;

    protected DevFlowAgentServiceBase(AgentOptions? options = null)
    {
        Options = options ?? new AgentOptions();
        _server = new AgentHttpServer(Options.Port);
        RegisterRoutes();
    }

    protected AgentOptions Options { get; }

    public bool IsRunning => _server.IsRunning;
    public int Port => _server.Port;

    public void Start()
    {
        if (_started) return;
        _server.Start();
        _started = true;
    }

    public Task StopAsync() => _server.StopAsync();

    protected abstract string AgentId { get; }
    protected abstract string AgentName { get; }
    protected abstract string FrameworkName { get; }
    protected abstract Task<List<ElementInfo>> BuildTreeAsync();
    protected abstract Task<ElementInfo?> FindElementAsync(string id);
    protected abstract Task<List<ElementInfo>> QueryElementsAsync(string? type = null, string? automationId = null, string? text = null);
    protected abstract Task<byte[]?> CaptureScreenshotAsync(string? elementId = null, string? selector = null);
    protected virtual Task<object?> GetWebViewContextsAsync() => Task.FromResult<object?>(new { contexts = Array.Empty<object>() });
    protected virtual Task<byte[]?> CaptureWebViewScreenshotAsync(string? contextId = null) => Task.FromResult<byte[]?>(null);
    protected virtual Task<object?> SendWebViewCdpCommandAsync(string? contextId, string method, JsonElement? @params) => Task.FromResult<object?>(null);
    protected abstract Task<bool> TryTapAsync(string elementId);
    protected abstract Task<bool> TryScrollAsync(string elementId, double deltaX, double deltaY);
    protected abstract Task<bool> TryFillAsync(string elementId, string text);
    protected abstract Task<bool> TryClearAsync(string elementId);
    protected abstract Task<bool> TryFocusAsync(string elementId);
    protected abstract Task<object?> TryKeyAsync(string? elementId, string? key, string? text);
    protected abstract Task<bool> TryBackAsync();
    protected abstract Task<object?> GetThemeAsync();
    protected abstract Task<object?> SetThemeAsync(string theme);
    protected abstract Task<string?> GetApplicationNameAsync();
    protected virtual object GetCapabilities() => new
    {
        screenshots = true,
        elementScreenshots = true,
        selectorScreenshots = true,
        tap = true,
        scroll = true,
        structuredErrors = true,
        webview = false,
        webviewCdp = false,
        multiWindow = false
    };

    private void RegisterRoutes()
    {
        _server.MapGet("/api/v1/agent/status", HandleStatusAsync);
        _server.MapGet("/api/v1/ui/tree", HandleTreeAsync);
        _server.MapGet("/api/v1/ui/element", HandleElementAsync);
        _server.MapGet("/api/v1/ui/elements", HandleQueryAsync);
        _server.MapGet("/api/v1/ui/screenshot", HandleScreenshotAsync);
        _server.MapGet("/api/v1/webview/contexts", HandleWebViewContextsAsync);
        _server.MapGet("/api/v1/webview/screenshot", HandleWebViewScreenshotAsync);
        _server.MapPost("/api/v1/webview/cdp", HandleWebViewCdpAsync);
        _server.MapPost("/api/v1/ui/tap", HandleTapAsync);
        _server.MapPost("/api/v1/ui/actions/fill", HandleFillAsync);
        _server.MapPost("/api/v1/ui/actions/clear", HandleClearAsync);
        _server.MapPost("/api/v1/ui/actions/focus", HandleFocusAsync);
        _server.MapPost("/api/v1/ui/actions/key", HandleKeyAsync);
        _server.MapPost("/api/v1/ui/actions/back", HandleBackAsync);
        _server.MapPost("/api/v1/ui/actions/batch", HandleBatchAsync);
        _server.MapPost("/api/v1/ui/actions/scroll", HandleScrollAsync);
        _server.MapGet("/api/v1/device/app/theme", HandleThemeGetAsync);
        _server.MapPut("/api/v1/device/app/theme", HandleThemeSetAsync);
        _server.MapGet("/api/v1/invoke/actions", HandleListInvokeActionsAsync);
        _server.MapPost("/api/v1/invoke/actions/{name}", HandleInvokeActionAsync);
    }

    private async Task<HttpResponse> HandleStatusAsync(HttpRequest request)
    {
        var status = new
        {
            name = AgentName,
            id = AgentId,
            framework = FrameworkName,
            version = GetType().Assembly.GetName().Version?.ToString() ?? "0.0.0",
            running = true,
            port = Port,
            application = await GetApplicationNameAsync().ConfigureAwait(false),
            capabilities = GetCapabilities()
        };
        return HttpResponse.Json(status);
    }

    private async Task<HttpResponse> HandleTreeAsync(HttpRequest request)
    {
        var tree = await BuildTreeAsync().ConfigureAwait(false);
        return HttpResponse.Json(new { elements = tree });
    }

    private async Task<HttpResponse> HandleElementAsync(HttpRequest request)
    {
        if (!request.QueryParams.TryGetValue("id", out var id) || string.IsNullOrWhiteSpace(id))
            return HttpResponse.Error("Missing required query parameter 'id'", 400);

        var element = await FindElementAsync(id).ConfigureAwait(false);
        return element is null ? HttpResponse.NotFound($"Element '{id}' not found") : HttpResponse.Json(element);
    }

    private async Task<HttpResponse> HandleQueryAsync(HttpRequest request)
    {
        request.QueryParams.TryGetValue("type", out var type);
        request.QueryParams.TryGetValue("automationId", out var automationId);
        request.QueryParams.TryGetValue("text", out var text);

        if (type == null && automationId == null && text == null)
            return HttpResponse.Error("At least one query parameter required: type, automationId, text", 400);

        var results = await QueryElementsAsync(type, automationId, text).ConfigureAwait(false);
        return HttpResponse.Json(results);
    }

    private async Task<HttpResponse> HandleScreenshotAsync(HttpRequest request)
    {
        request.QueryParams.TryGetValue("id", out var elementId);
        request.QueryParams.TryGetValue("selector", out var selector);
        var bytes = await CaptureScreenshotAsync(
            string.IsNullOrWhiteSpace(elementId) ? null : elementId,
            string.IsNullOrWhiteSpace(selector) ? null : selector).ConfigureAwait(false);
        return bytes != null ? HttpResponse.Png(bytes) : HttpResponse.Error("Screenshot capture failed", 500);
    }

    private async Task<HttpResponse> HandleWebViewContextsAsync(HttpRequest request)
    {
        var contexts = await GetWebViewContextsAsync().ConfigureAwait(false);
        return HttpResponse.Json(contexts ?? new { contexts = Array.Empty<object>() });
    }

    private async Task<HttpResponse> HandleWebViewScreenshotAsync(HttpRequest request)
    {
        request.QueryParams.TryGetValue("context", out var contextId);
        var bytes = await CaptureWebViewScreenshotAsync(string.IsNullOrWhiteSpace(contextId) ? null : contextId).ConfigureAwait(false);
        return bytes != null ? HttpResponse.Png(bytes) : HttpResponse.Error("WebView screenshot capture failed", 500);
    }

    private async Task<HttpResponse> HandleWebViewCdpAsync(HttpRequest request)
    {
        var payload = request.BodyAs<WebViewCdpRequest>();
        if (payload == null || string.IsNullOrWhiteSpace(payload.Method))
            return HttpResponse.Error("Request must include a JSON body with a 'method' field", 400);

        var result = await SendWebViewCdpCommandAsync(payload.Context, payload.Method, payload.Params).ConfigureAwait(false);
        return result != null ? HttpResponse.Json(result) : HttpResponse.Error("WebView CDP command failed", 500);
    }

    private async Task<HttpResponse> HandleTapAsync(HttpRequest request)
    {
        var payload = request.BodyAs<TapRequest>();
        if (payload == null || string.IsNullOrWhiteSpace(payload.Id))
            return HttpResponse.Error("Request must include a JSON body with an 'id' field", 400);

        var result = await TryTapAsync(payload.Id).ConfigureAwait(false);
        return result ? HttpResponse.Ok() : HttpResponse.Error($"Tap target '{payload.Id}' could not be activated", 404);
    }

    private async Task<HttpResponse> HandleScrollAsync(HttpRequest request)
    {
        var payload = request.BodyAs<ScrollRequest>();
        if (payload == null || string.IsNullOrWhiteSpace(payload.Id))
            return HttpResponse.Error("Request must include a JSON body with an 'id' field", 400);

        var result = await TryScrollAsync(payload.Id, payload.DeltaX, payload.DeltaY).ConfigureAwait(false);
        return result ? HttpResponse.Ok() : HttpResponse.Error($"Scroll target '{payload.Id}' could not be scrolled", 404);
    }

    private async Task<HttpResponse> HandleFillAsync(HttpRequest request)
    {
        var payload = request.BodyAs<FillRequest>();
        if (payload == null || string.IsNullOrWhiteSpace(payload.ElementId) || payload.Text == null)
            return HttpResponse.Error("elementId and text are required", 400);

        var result = await TryFillAsync(payload.ElementId, payload.Text).ConfigureAwait(false);
        return result ? HttpResponse.Ok("Text set") : HttpResponse.Error("Element does not accept text input", 404);
    }

    private async Task<HttpResponse> HandleClearAsync(HttpRequest request)
    {
        var payload = request.BodyAs<ActionRequest>();
        if (payload == null || string.IsNullOrWhiteSpace(payload.ElementId))
            return HttpResponse.Error("elementId is required", 400);

        var result = await TryClearAsync(payload.ElementId).ConfigureAwait(false);
        return result ? HttpResponse.Ok("Cleared") : HttpResponse.Error("Element does not accept text input", 404);
    }

    private async Task<HttpResponse> HandleKeyAsync(HttpRequest request)
    {
        var payload = request.BodyAs<KeyRequest>();
        if (payload == null || (string.IsNullOrWhiteSpace(payload.Key) && string.IsNullOrWhiteSpace(payload.Text)))
            return HttpResponse.Error("key or text is required", 400);

        var result = await TryKeyAsync(payload.ElementId, payload.Key, payload.Text).ConfigureAwait(false);
        return result != null ? HttpResponse.Json(result) : HttpResponse.Error("Key action failed", 404);
    }

    private async Task<HttpResponse> HandleFocusAsync(HttpRequest request)
    {
        var payload = request.BodyAs<ActionRequest>();
        if (payload == null || string.IsNullOrWhiteSpace(payload.ElementId))
            return HttpResponse.Error("elementId is required", 400);

        var result = await TryFocusAsync(payload.ElementId).ConfigureAwait(false);
        return result ? HttpResponse.Ok("Focused") : HttpResponse.Error("Element could not be focused", 404);
    }

    private async Task<HttpResponse> HandleBackAsync(HttpRequest request)
    {
        var result = await TryBackAsync().ConfigureAwait(false);
        return result ? HttpResponse.Ok("Navigated back") : HttpResponse.Error("Back navigation failed", 404);
    }

    private async Task<HttpResponse> HandleBatchAsync(HttpRequest request)
    {
        var payload = request.BodyAs<BatchRequest>();
        if (payload?.Actions == null || payload.Actions.Count == 0)
            return HttpResponse.Error("actions are required", 400);

        var results = new List<object>(payload.Actions.Count);
        var allSucceeded = true;

        foreach (var action in payload.Actions)
        {
            var actionName = (action.Action ?? action.Type ?? string.Empty).Trim().ToLowerInvariant();
            HttpResponse response;

            switch (actionName)
            {
                case "tap":
                    response = await HandleTapAsync(new HttpRequest { Method = "POST", Body = JsonSerializer.Serialize(new TapRequest { Id = action.ElementId }) }).ConfigureAwait(false);
                    break;
                case "fill":
                    response = await HandleFillAsync(new HttpRequest { Method = "POST", Body = JsonSerializer.Serialize(new FillRequest { ElementId = action.ElementId, Text = action.Text ?? string.Empty }) }).ConfigureAwait(false);
                    break;
                case "clear":
                    response = await HandleClearAsync(new HttpRequest { Method = "POST", Body = JsonSerializer.Serialize(new ActionRequest { ElementId = action.ElementId }) }).ConfigureAwait(false);
                    break;
                case "focus":
                    response = await HandleFocusAsync(new HttpRequest { Method = "POST", Body = JsonSerializer.Serialize(new ActionRequest { ElementId = action.ElementId }) }).ConfigureAwait(false);
                    break;
                case "scroll":
                    response = await HandleScrollAsync(new HttpRequest { Method = "POST", Body = JsonSerializer.Serialize(new ScrollRequest { Id = action.ElementId, DeltaX = action.DeltaX, DeltaY = action.DeltaY }) }).ConfigureAwait(false);
                    break;
                case "back":
                    response = await HandleBackAsync(new HttpRequest { Method = "POST" }).ConfigureAwait(false);
                    break;
                case "key":
                    response = await HandleKeyAsync(new HttpRequest { Method = "POST", Body = JsonSerializer.Serialize(new KeyRequest { ElementId = action.ElementId, Key = action.Key, Text = action.Text }) }).ConfigureAwait(false);
                    break;
                default:
                    response = HttpResponse.Error($"Unsupported batch action '{actionName}'", 400);
                    break;
            }

            var succeeded = response.StatusCode < 400;
            allSucceeded &= succeeded;
            results.Add(new { action = actionName, success = succeeded, statusCode = response.StatusCode, response = response.Body });
            if (!succeeded && !payload.ContinueOnError)
                break;
        }

        return HttpResponse.Json(new { success = allSucceeded, results });
    }

    private async Task<HttpResponse> HandleThemeGetAsync(HttpRequest request)
    {
        var theme = await GetThemeAsync().ConfigureAwait(false);
        return theme != null ? HttpResponse.Json(theme) : HttpResponse.Error("Theme information unavailable", 500);
    }

    private async Task<HttpResponse> HandleThemeSetAsync(HttpRequest request)
    {
        var payload = request.BodyAs<ThemeSetRequest>();
        if (payload == null || string.IsNullOrWhiteSpace(payload.Theme))
            return HttpResponse.Error("theme is required", 400);

        var result = await SetThemeAsync(payload.Theme).ConfigureAwait(false);
        return result != null ? HttpResponse.Json(result) : HttpResponse.Error("Theme could not be changed", 400);
    }

    private sealed class TapRequest
    {
        public string? Id { get; set; }
    }

    private sealed class ScrollRequest
    {
        public string? Id { get; set; }
        public double DeltaX { get; set; }
        public double DeltaY { get; set; }
    }

    private sealed class FillRequest
    {
        public string? ElementId { get; set; }
        public string? Text { get; set; }
    }

    private sealed class ActionRequest
    {
        public string? ElementId { get; set; }
    }

    private sealed class KeyRequest
    {
        public string? ElementId { get; set; }
        public string? Key { get; set; }
        public string? Text { get; set; }
    }

    private sealed class WebViewCdpRequest
    {
        public string? Context { get; set; }
        public string? Method { get; set; }
        public JsonElement? Params { get; set; }
    }

    private sealed class BatchRequest
    {
        public bool ContinueOnError { get; set; }
        public List<BatchActionRequest> Actions { get; set; } = [];
    }

    private sealed class BatchActionRequest
    {
        public string? Action { get; set; }
        public string? Type { get; set; }
        public string? ElementId { get; set; }
        public string? Text { get; set; }
        public string? Key { get; set; }
        public double DeltaX { get; set; }
        public double DeltaY { get; set; }
    }

    private sealed class ThemeSetRequest
    {
        public string? Theme { get; set; }
    }

    private sealed class InvokeActionEntry
    {
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string DeclaringType { get; set; } = string.Empty;
        public MethodInfo Method { get; set; } = null!;
    }

    private sealed class InvokeActionRequest
    {
        public JsonElement[]? Args { get; set; }
    }

    private static InvokeActionEntry[] DiscoverActions()
    {
        var actions = new List<InvokeActionEntry>();
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (asm.IsDynamic)
                continue;

            Type[] types;
            try { types = asm.GetTypes(); } catch { continue; }

            foreach (var type in types)
            {
                foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static))
                {
                    var attr = method.GetCustomAttribute<DevFlowActionAttribute>();
                    if (attr == null)
                        continue;

                    actions.Add(new InvokeActionEntry
                    {
                        Name = attr.Name,
                        Description = attr.Description,
                        DeclaringType = type.FullName ?? type.Name,
                        Method = method
                    });
                }
            }
        }

        return actions
            .GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToArray();
    }

    private static object? ConvertInvokeArg(Type targetType, JsonElement argElement)
    {
        var t = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (argElement.ValueKind == JsonValueKind.Null)
            return null;
        if (t == typeof(string))
            return argElement.GetString();
        if (t == typeof(int))
            return argElement.ValueKind == JsonValueKind.Number ? argElement.GetInt32() : int.Parse(argElement.GetString()!);
        if (t == typeof(long))
            return argElement.ValueKind == JsonValueKind.Number ? argElement.GetInt64() : long.Parse(argElement.GetString()!);
        if (t == typeof(double))
            return argElement.ValueKind == JsonValueKind.Number ? argElement.GetDouble() : double.Parse(argElement.GetString()!);
        if (t == typeof(bool))
            return argElement.ValueKind == JsonValueKind.True || argElement.ValueKind == JsonValueKind.False ? argElement.GetBoolean() : bool.Parse(argElement.GetString()!);
        if (t.IsEnum)
            return argElement.ValueKind == JsonValueKind.Number
                ? Enum.ToObject(t, argElement.GetInt32())
                : Enum.Parse(t, argElement.GetString()!, true);

        throw new ArgumentException($"Cannot convert JSON {argElement.ValueKind} to {targetType.Name}");
    }

    private static object?[] ConvertInvokeArgs(ParameterInfo[] parameters, JsonElement[]? args)
    {
        var result = new object?[parameters.Length];
        for (var i = 0; i < parameters.Length; i++)
        {
            if (args != null && i < args.Length)
                result[i] = ConvertInvokeArg(parameters[i].ParameterType, args[i]);
            else if (parameters[i].HasDefaultValue)
                result[i] = parameters[i].DefaultValue;
            else
                throw new ArgumentException($"Missing required argument '{parameters[i].Name}' (parameter {i})");
        }

        return result;
    }

    private static async Task<(bool success, string? returnValue, string? returnType, string? error)> InvokeMethodAsync(MethodInfo method, object?[] args)
    {
        try
        {
            var result = method.Invoke(null, args);
            if (result is Task task)
            {
                await task.ConfigureAwait(false);
                if (task.GetType().IsGenericType)
                {
                    var value = task.GetType().GetProperty("Result")?.GetValue(task);
                    return (true, value?.ToString(), task.GetType().GetGenericArguments()[0].Name, null);
                }

                return (true, null, "void", null);
            }

            if (method.ReturnType == typeof(void))
                return (true, null, "void", null);

            return (true, result?.ToString(), method.ReturnType.Name, null);
        }
        catch (TargetInvocationException tie)
        {
            var inner = tie.InnerException ?? tie;
            return (false, null, null, $"{inner.GetType().Name}: {inner.Message}");
        }
        catch (Exception ex)
        {
            return (false, null, null, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private Task<HttpResponse> HandleListInvokeActionsAsync(HttpRequest request)
    {
        var actions = DiscoverActions();
        var result = actions.Select(a => new
        {
            name = a.Name,
            description = a.Description,
            declaringType = a.DeclaringType,
            parameters = a.Method.GetParameters().Select(p => new
            {
                name = p.Name,
                type = p.ParameterType.Name,
                isRequired = !p.HasDefaultValue
            })
        });

        return Task.FromResult(HttpResponse.Json(new { actions = result }));
    }

    private async Task<HttpResponse> HandleInvokeActionAsync(HttpRequest request)
    {
        if (!request.RouteParams.TryGetValue("name", out var actionName) || string.IsNullOrWhiteSpace(actionName))
            return HttpResponse.Error("Action name required", 400);

        var action = Array.Find(DiscoverActions(), a => string.Equals(a.Name, actionName, StringComparison.OrdinalIgnoreCase));
        if (action == null)
            return HttpResponse.Error($"Action '{actionName}' not found. Use GET /api/v1/invoke/actions to list available actions.", 404);

        try
        {
            var body = request.BodyAs<InvokeActionRequest>();
            var args = ConvertInvokeArgs(action.Method.GetParameters(), body?.Args);
            var (success, returnValue, returnType, error) = await InvokeMethodAsync(action.Method, args).ConfigureAwait(false);
            return success
                ? HttpResponse.Json(new { success = true, action = action.Name, returnValue, returnType })
                : HttpResponse.Error($"Action '{actionName}' failed: {error}", 400);
        }
        catch (Exception ex)
        {
            return HttpResponse.Error($"Argument error: {ex.Message}", 400);
        }
    }

    public void Dispose()
    {
        _ = _server.StopAsync();
    }
}
