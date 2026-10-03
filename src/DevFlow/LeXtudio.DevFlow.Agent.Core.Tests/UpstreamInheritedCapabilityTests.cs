using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Maui.DevFlow.Agent.Core;
using Xunit;

namespace LeXtudio.DevFlow.Agent.Core.Tests;

/// <summary>
/// Covers the agent surface the desktop agents inherit from upstream rather than register themselves.
/// </summary>
/// <remarks>
/// The desktop agents re-register their own routes on top of the upstream base, but everything upstream
/// adds beyond that set - storage, device info, profiling - is served by the base class and reaches us
/// only because the options passed in are forwarded. These tests pin the two behaviours that are easy to
/// regress silently: a capability that is merely switched on, and the framework name reported to clients.
/// They run against <see cref="StubAgentService"/>, so no UI framework is involved.
/// </remarks>
public sealed class UpstreamInheritedCapabilityTests
{
    [Fact]
    public async Task ProfilerCapabilities_ReportUnavailableUntilItIsEnabled()
    {
        using var service = new StubAgentService([], new AgentOptions { Port = AgentTestHarness.GetFreePort() });
        service.Start();

        using var client = CreateClient(service.Port);
        await AgentTestHarness.WaitForServerAsync(client, TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);

        var capabilities = await AgentTestHarness.GetJsonAsync(
            client, "/api/v1/profiler/capabilities", TestContext.Current.CancellationToken);

        Assert.False(capabilities.GetProperty("featureEnabled").GetBoolean());
        Assert.False(capabilities.GetProperty("available").GetBoolean());
    }

    [Fact]
    public async Task ProfilerSession_YieldsSamplesWhenEnabled()
    {
        using var service = new StubAgentService([], new AgentOptions
        {
            Port = AgentTestHarness.GetFreePort(),
            EnableProfiler = true,
        });
        service.Start();

        using var client = CreateClient(service.Port);
        await AgentTestHarness.WaitForServerAsync(client, TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);

        var capabilities = await AgentTestHarness.GetJsonAsync(
            client, "/api/v1/profiler/capabilities", TestContext.Current.CancellationToken);
        Assert.True(capabilities.GetProperty("featureEnabled").GetBoolean());
        Assert.True(capabilities.GetProperty("available").GetBoolean());

        var leaseId = await ClaimLeaseAsync(client);

        using var start = await SendMutationAsync(
            client, HttpMethod.Post, "/api/v1/profiler/sessions", "{\"durationMs\":3000}", leaseId);
        start.EnsureSuccessStatusCode();

        using var startDoc = JsonDocument.Parse(await start.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var sessionId = startDoc.RootElement.GetProperty("session").GetProperty("SessionId").GetString();
        Assert.False(string.IsNullOrWhiteSpace(sessionId));

        // The collector samples on an interval, so give it a couple of ticks before reading.
        await Task.Delay(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        using var samples = await SendMutationAsync(
            client, HttpMethod.Get, $"/api/v1/profiler/sessions/{sessionId}/samples", body: null, leaseId);
        samples.EnsureSuccessStatusCode();

        using var samplesDoc = JsonDocument.Parse(await samples.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var collected = samplesDoc.RootElement.GetProperty("Samples");
        Assert.True(collected.GetArrayLength() > 0, "The profiler session produced no samples.");
    }

    [Fact]
    public async Task Tree_AnswersWithABareArraySoTheSharedClientCanReadIt()
    {
        // The shared AgentClient deserializes a bare array here and reads { "elements": [...] } only when
        // envelope=true is requested. Answering with an object by default left that client reporting an
        // empty tree against these agents, with no error to explain it.
        using var service = new StubAgentService([AgentTestHarness.BuildCyclicTree()], new AgentOptions
        {
            Port = AgentTestHarness.GetFreePort(),
        });
        service.Start();

        using var client = CreateClient(service.Port);
        await AgentTestHarness.WaitForServerAsync(client, TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);

        using var bare = await client.GetAsync("/api/v1/ui/tree", TestContext.Current.CancellationToken);
        using var bareDoc = JsonDocument.Parse(await bare.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(JsonValueKind.Array, bareDoc.RootElement.ValueKind);

        using var enveloped = await client.GetAsync("/api/v1/ui/tree?envelope=true", TestContext.Current.CancellationToken);
        using var envelopeDoc = JsonDocument.Parse(await enveloped.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(JsonValueKind.Object, envelopeDoc.RootElement.ValueKind);
        Assert.True(envelopeDoc.RootElement.TryGetProperty("elements", out _));
        Assert.False(string.IsNullOrWhiteSpace(envelopeDoc.RootElement.GetProperty("revision").GetString()));
    }

    [Fact]
    public async Task Tree_HonorsTheDepthParameter()
    {
        using var service = new StubAgentService([AgentTestHarness.BuildCyclicTree()], new AgentOptions
        {
            Port = AgentTestHarness.GetFreePort(),
        });
        service.Start();

        using var client = CreateClient(service.Port);
        await AgentTestHarness.WaitForServerAsync(client, TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);

        using var response = await client.GetAsync("/api/v1/ui/tree?depth=1", TestContext.Current.CancellationToken);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        foreach (var root in document.RootElement.EnumerateArray())
        {
            // The roots are kept; their subtree is what the depth limit removes.
            Assert.Empty(root.GetProperty("children").EnumerateArray());
        }
    }

    [Fact]
    public async Task UnsupportedCapability_BlamesThisBackendRatherThanTheHostPlatform()
    {
        using var service = new StubAgentService([], new AgentOptions { Port = AgentTestHarness.GetFreePort() });
        service.Start();

        using var client = CreateClient(service.Port);
        await AgentTestHarness.WaitForServerAsync(client, TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);

        using var response = await client.GetAsync("/api/v1/storage/preferences", TestContext.Current.CancellationToken);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        Assert.Equal("not_supported", document.RootElement.GetProperty("error").GetString());
        Assert.Equal("storage.preferences", document.RootElement.GetProperty("capability").GetString());

        // Upstream derives this text from UiFrameworkName, which defaults to the host operating system
        // ("appkit" on macOS, "winui" on Windows). A desktop agent must name itself instead, otherwise the
        // reason blames a backend the app never uses.
        var reason = document.RootElement.GetProperty("reason").GetString();
        Assert.Contains("'stub'", reason, StringComparison.Ordinal);
        Assert.DoesNotContain("appkit", reason, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("winui", reason, StringComparison.OrdinalIgnoreCase);
    }

    private static HttpClient CreateClient(int port)
        => new() { BaseAddress = new Uri($"http://localhost:{port}"), Timeout = TimeSpan.FromSeconds(30) };

    private static async Task<string> ClaimLeaseAsync(HttpClient client)
    {
        var payload = new Dictionary<string, object?>
        {
            ["action"] = "claim",
            ["leaseId"] = Guid.NewGuid().ToString("N"),
            ["holderKind"] = "test",
        };

        // Serialize first: JsonContent falls back to a chunked body, which the agent's HTTP server does
        // not read, so the claim would fail with a dropped connection instead of a lease.
        using var content = new StringContent(
            JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var response = await client.PostAsync("/api/v1/agent/lease", content, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return document.RootElement.GetProperty("leaseId").GetString()!;
    }

    private static async Task<HttpResponseMessage> SendMutationAsync(
        HttpClient client, HttpMethod method, string path, string? body, string leaseId)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.TryAddWithoutValidation("X-DevFlow-Lease", leaseId);
        request.Headers.TryAddWithoutValidation("X-DevFlow-Holder", "test");
        if (body is not null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }
}
