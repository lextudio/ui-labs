using System.Net.Http;
using System.Text;
using System.Text.Json;
using LeXtudio.DevFlow.Agent.Core;
using Microsoft.Maui.DevFlow.Agent.Core;

if (JsonSerializer.IsReflectionEnabledByDefault)
    throw new InvalidOperationException("This smoke must run with JSON reflection disabled.");

var port = 19223;
using var agent = new SmokeAgent(new AgentOptions { Port = port, RequireMutationLease = false });
agent.Start();

using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
var failures = new List<string>();

async Task Check(string path, Func<JsonElement, bool> ok, string? postBody = null)
{
    try
    {
        var url = $"http://localhost:{port}{path}";
        var text = postBody == null
            ? await http.GetStringAsync(url)
            : await (await http.PostAsync(url, new StringContent(postBody, Encoding.UTF8, "application/json"))).Content.ReadAsStringAsync();
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

await Check("/api/v1/ui/query-selector?selector=Leaf", r => r.GetProperty("count").GetInt32() == 1 && r.GetProperty("elements")[0].GetProperty("id").GetString() == "leaf");
await Check("/api/v1/ui/hit-test?x=1&y=1", r => r.TryGetProperty("elements", out _));
await Check("/api/v1/network/requests", r => r.GetProperty("total").GetInt32() == 0);
await Check("/api/v1/webview/contexts", r => r.GetProperty("contexts").GetArrayLength() == 0);
await Check("/api/v1/invoke/actions", r => r.GetProperty("actions").ValueKind == JsonValueKind.Array);
await Check("/api/v1/ui/assert", r => r.GetProperty("success").GetBoolean() && r.GetProperty("matchCount").GetInt32() == 1, """{"selector":"Leaf","exists":true}""");
await Check("/api/v1/ui/actions/batch", r => r.GetProperty("results").GetArrayLength() == 1 && !r.GetProperty("success").GetBoolean(), """{"actions":[{"action":"tap","elementId":"leaf"}]}""");

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
