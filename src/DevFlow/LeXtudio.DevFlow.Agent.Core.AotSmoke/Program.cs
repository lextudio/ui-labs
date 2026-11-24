using System.Net.Http;
using System.Text.Json;
using LeXtudio.DevFlow.Agent.Core;
using Microsoft.Maui.DevFlow.Agent.Core;

if (JsonSerializer.IsReflectionEnabledByDefault)
    throw new InvalidOperationException("This smoke must run with JSON reflection disabled.");

var port = 19223;
using var agent = new SmokeAgent(new AgentOptions { Port = port });
agent.Start();

using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
var failures = new List<string>();

async Task Check(string path, Func<JsonElement, bool> ok)
{
    try
    {
        var text = await http.GetStringAsync($"http://localhost:{port}{path}");
        using var doc = JsonDocument.Parse(text);
        if (!ok(doc.RootElement))
            failures.Add($"{path}: unexpected body {text}");
        else
            Console.WriteLine($"ok {path}");
    }
    catch (Exception ex)
    {
        failures.Add($"{path}: {ex.GetType().Name}: {ex.Message}");
    }
}

await Check("/api/v1/agent/status", r => r.GetProperty("framework").GetString() == "aot-smoke" && r.GetProperty("capabilities").GetProperty("tap").GetBoolean());
await Check("/api/v1/ui/tree", r => r.ValueKind == JsonValueKind.Array && r[0].GetProperty("type").GetString() == "Root" && r[0].GetProperty("children")[0].GetProperty("type").GetString() == "Leaf");

if (failures.Count > 0)
{
    foreach (var failure in failures)
        Console.Error.WriteLine("FAIL " + failure);
    return 1;
}

Console.WriteLine("AOT smoke passed");
return 0;

sealed class SmokeAgent(AgentOptions options) : DevFlowAgentServiceBase(options)
{
    protected override string AgentId => "aot-smoke";
    protected override string AgentName => "aot-smoke";
    protected override string FrameworkName => "aot-smoke";

    protected override Task<List<ElementInfo>> BuildTreeAsync() => Task.FromResult(new List<ElementInfo>
    {
        new() { Id = "root", Type = "Root", FullType = "Smoke.Root", Framework = "aot-smoke", Children = [new() { Id = "leaf", ParentId = "root", Type = "Leaf", FullType = "Smoke.Leaf", Framework = "aot-smoke" }] },
    });

    protected override Task<ElementInfo?> FindElementAsync(string id) => Task.FromResult<ElementInfo?>(null);
    protected override Task<List<ElementInfo>> QueryElementsAsync(string? type = null, string? automationId = null, string? text = null, int maxResults = 50, int maxDepth = 24) => Task.FromResult(new List<ElementInfo>());
    protected override Task<byte[]?> CaptureScreenshotAsync(string? elementId = null, string? selector = null) => Task.FromResult<byte[]?>(null);
    protected override Task<bool> TryTapAsync(string elementId) => Task.FromResult(false);
    protected override Task<bool> TryScrollAsync(string elementId, double deltaX, double deltaY) => Task.FromResult(false);
    protected override Task<bool> TryFillAsync(string elementId, string text) => Task.FromResult(false);
    protected override Task<bool> TryClearAsync(string elementId) => Task.FromResult(false);
    protected override Task<bool> TryFocusAsync(string elementId) => Task.FromResult(false);
    protected override Task<object?> TryKeyAsync(string? elementId, string? key, string? text) => Task.FromResult<object?>(null);
    protected override Task<bool> TryBackAsync() => Task.FromResult(false);
    protected override Task<object?> GetThemeAsync() => Task.FromResult<object?>(null);
    protected override Task<object?> SetThemeAsync(string theme) => Task.FromResult<object?>(null);
    protected override Task<string?> GetApplicationNameAsync() => Task.FromResult<string?>("aot-smoke");
}
