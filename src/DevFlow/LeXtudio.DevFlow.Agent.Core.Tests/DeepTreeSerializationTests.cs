using System.Net.Http;
using System.Text.Json;
using Microsoft.Maui.DevFlow.Agent.Core;
using Xunit;

namespace LeXtudio.DevFlow.Agent.Core.Tests;

/// <summary>
/// Guards the JSON behavior that lets DevFlow serve host applications with deep or self-referencing
/// UI trees.
///
/// <para>
/// This is not upstream MAUI-Labs behavior. It came from a LeXtudio fork patch that raises
/// <c>MaxDepth</c> and sets <c>ReferenceHandler.IgnoreCycles</c> on the agent's JSON options.
/// Measured effect: a UI tree nests up to 127 levels instead of 32, and a self-referencing tree
/// serializes instead of throwing. Without it, <c>GET /api/v1/ui/tree</c> fails on real docking
/// layouts with a "possible object cycle was detected" error that reads like a false positive.
/// </para>
///
/// <para>
/// Upstream rewrote JSON serialization when the agent moved into <c>Agent.Abstractions</c>, so this
/// file is the acceptance gate for re-implementing the patch on the new base.
/// </para>
/// </summary>
public class DeepTreeSerializationTests
{
    /// <summary>
    /// Comfortably below the verified 127-level ceiling of <c>MaxDepth = 256</c>, and far above both
    /// the System.Text.Json default and the 32-level limit that exists without the fork patch.
    /// </summary>
    private const int RealisticTreeDepth = 120;

    [Fact]
    public async Task UiTree_WithTreeDeeperThanSystemTextJsonDefault_SerializesSuccessfully()
    {
        var port = AgentTestHarness.GetFreePort();
        using var service = new StubAgentService([AgentTestHarness.BuildDeepChain(RealisticTreeDepth)], new AgentOptions { Port = port });
        service.Start();
        await using var _ = new AsyncDisposable(service);

        using var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}") };
        await AgentTestHarness.WaitForServerAsync(client, TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);

        using var response = await client.GetAsync("/api/v1/ui/tree", TestContext.Current.CancellationToken);

        Assert.True(
            response.IsSuccessStatusCode,
            $"GET /api/v1/ui/tree returned {(int)response.StatusCode} for a {RealisticTreeDepth}-deep tree.");
    }

    [Fact]
    public async Task UiTree_WithDeepTree_PreservesTheWholeChain()
    {
        var port = AgentTestHarness.GetFreePort();
        using var service = new StubAgentService([AgentTestHarness.BuildDeepChain(RealisticTreeDepth)], new AgentOptions { Port = port });
        service.Start();
        await using var _ = new AsyncDisposable(service);

        using var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}") };
        await AgentTestHarness.WaitForServerAsync(client, TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);

        var json = await AgentTestHarness.GetJsonAsync(client, "/api/v1/ui/tree", TestContext.Current.CancellationToken);

        Assert.Equal(RealisticTreeDepth, CountNestingLevels(json[0]));
    }

    [Fact]
    public async Task UiTree_WithSelfReferencingTree_SerializesSuccessfully()
    {
        var port = AgentTestHarness.GetFreePort();
        using var service = new StubAgentService([AgentTestHarness.BuildCyclicTree()], new AgentOptions { Port = port });
        service.Start();
        await using var _ = new AsyncDisposable(service);

        using var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}") };
        await AgentTestHarness.WaitForServerAsync(client, TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);

        using var response = await client.GetAsync("/api/v1/ui/tree", TestContext.Current.CancellationToken);

        Assert.True(
            response.IsSuccessStatusCode,
            $"GET /api/v1/ui/tree returned {(int)response.StatusCode} for a self-referencing tree.");
    }

    [Fact]
    public void DevFlowJson_WithSelfReferencingTree_DoesNotThrow()
    {
        var response = DevFlowJson.Json(new { elements = new List<ElementInfo> { AgentTestHarness.BuildCyclicTree() } });

        Assert.Equal(200, response.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(response.Body));
    }

    [Fact]
    public void HttpResponseJson_WithSelfReferencingTree_DoesNotThrow()
    {
        // The LeXtudio fork carries a patch onto AgentHttpServer's JsonOptions, so the upstream response
        // path handles cycles too. That complements DevFlowJson rather than replacing it: DevFlowJson
        // covers the payloads our own handlers serialize, this covers the responses the server writes.
        var response = HttpResponse.Json(new { elements = new List<ElementInfo> { AgentTestHarness.BuildCyclicTree() } });
        Assert.False(string.IsNullOrEmpty(response.Body));
    }

    [Fact]
    public void HttpResponseJson_WithTreeBeyondSupportedDepth_ThrowsRatherThanTruncating()
    {
        Assert.Throws<JsonException>(
            () => HttpResponse.Json(new { elements = new List<ElementInfo> { AgentTestHarness.BuildDeepChain(5000) } }));
    }

    private static int CountNestingLevels(JsonElement element)
    {
        var levels = 0;

        while (true)
        {
            levels++;

            if (!element.TryGetProperty("children", out var children)
                || children.ValueKind != JsonValueKind.Array
                || children.GetArrayLength() == 0)
            {
                return levels;
            }

            element = children[0];
        }
    }

    private sealed class AsyncDisposable(DevFlowAgentServiceBase service) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync() => await service.StopAsync();
    }
}