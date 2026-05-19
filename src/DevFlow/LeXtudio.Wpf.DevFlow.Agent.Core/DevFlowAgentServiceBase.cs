using System.Text.Json;
using Microsoft.Maui.DevFlow.Agent.Core;

namespace LeXtudio.Wpf.DevFlow.Agent.Core;

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
    protected abstract Task<byte[]?> CaptureScreenshotAsync();
    protected abstract Task<bool> TryTapAsync(string elementId);
    protected abstract Task<string?> GetApplicationNameAsync();

    private void RegisterRoutes()
    {
        _server.MapGet("/api/v1/agent/status", HandleStatusAsync);
        _server.MapGet("/api/v1/ui/tree", HandleTreeAsync);
        _server.MapGet("/api/v1/ui/element", HandleElementAsync);
        _server.MapGet("/api/v1/ui/screenshot", HandleScreenshotAsync);
        _server.MapPost("/api/v1/ui/tap", HandleTapAsync);
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
            application = await GetApplicationNameAsync().ConfigureAwait(false)
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

    private async Task<HttpResponse> HandleScreenshotAsync(HttpRequest request)
    {
        var bytes = await CaptureScreenshotAsync().ConfigureAwait(false);
        return bytes != null ? HttpResponse.Png(bytes) : HttpResponse.Error("Screenshot capture failed", 500);
    }

    private async Task<HttpResponse> HandleTapAsync(HttpRequest request)
    {
        var payload = request.BodyAs<TapRequest>();
        if (payload == null || string.IsNullOrWhiteSpace(payload.Id))
            return HttpResponse.Error("Request must include a JSON body with an 'id' field", 400);

        var result = await TryTapAsync(payload.Id).ConfigureAwait(false);
        return result ? HttpResponse.Ok() : HttpResponse.Error($"Tap target '{payload.Id}' could not be activated", 404);
    }

    private sealed class TapRequest
    {
        public string? Id { get; set; }
    }

    public void Dispose()
    {
        _ = _server.StopAsync();
    }
}
