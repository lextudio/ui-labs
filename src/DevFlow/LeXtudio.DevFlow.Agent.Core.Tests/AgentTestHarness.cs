using System.Collections;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text.Json;
using Microsoft.Maui.DevFlow.Agent.Core;

namespace LeXtudio.DevFlow.Agent.Core.Tests;

/// <summary>
/// A concrete <see cref="DevFlowAgentServiceBase"/> for tests that need a real agent surface without
/// a UI framework. Every UI seam answers from an in-memory tree so the tests stay framework-neutral
/// and runnable on every host, including macOS.
/// </summary>
internal sealed class StubAgentService(List<ElementInfo> tree, AgentOptions? options = null)
    : DevFlowAgentServiceBase(options)
{
    private readonly List<ElementInfo> _tree = tree;

    protected override string AgentId => "stub-agent";
    protected override string AgentName => "LeXtudio.DevFlow.Agent";
    protected override string FrameworkName => "stub";

    private ITreeSource? _source;

    /// <summary>Lets a test vary the tree between requests instead of fixing it at construction.</summary>
    internal void SetTreeSource(ITreeSource source) => _source = source;

    protected override Task<List<ElementInfo>> BuildTreeAsync()
        => _source is null ? Task.FromResult(_tree) : _source.GetAsync();

    protected override Task<ElementInfo?> FindElementAsync(string id)
        => Task.FromResult(Flatten(_tree).FirstOrDefault(e => e.Id == id));

    protected override Task<List<ElementInfo>> QueryElementsAsync(
        string? type = null,
        string? automationId = null,
        string? text = null,
        int maxResults = 50,
        int maxDepth = 24)
        => Task.FromResult(Flatten(_tree)
            .Where(e => (type is null || e.Type == type)
                && (automationId is null || e.AutomationId == automationId)
                && (text is null || e.Text == text))
            .Take(maxResults)
            .ToList());

    protected override Task<byte[]?> CaptureScreenshotAsync(string? elementId = null, string? selector = null)
        => Task.FromResult<byte[]?>(null);

    protected override Task<bool> TryTapAsync(string elementId) => Task.FromResult(true);

    protected override Task<bool> TryScrollAsync(string elementId, double deltaX, double deltaY)
        => Task.FromResult(true);

    protected override Task<bool> TryFillAsync(string elementId, string text) => Task.FromResult(true);

    protected override Task<bool> TryClearAsync(string elementId) => Task.FromResult(true);

    protected override Task<bool> TryFocusAsync(string elementId) => Task.FromResult(true);

    protected override Task<object?> TryKeyAsync(string? elementId, string? key, string? text)
        => Task.FromResult<object?>(null);

    protected override Task<bool> TryBackAsync() => Task.FromResult(true);

    protected override Task<object?> GetThemeAsync() => Task.FromResult<object?>(new { theme = "light" });

    protected override Task<object?> SetThemeAsync(string theme) => Task.FromResult<object?>(null);

    protected override Task<string?> GetApplicationNameAsync() => Task.FromResult<string?>("Stub");

    internal static IEnumerable<ElementInfo> Flatten(IEnumerable<ElementInfo> elements)
    {
        foreach (var element in elements)
        {
            yield return element;

            if (element.Children is null)
            {
                continue;
            }

            foreach (var child in Flatten(element.Children))
            {
                yield return child;
            }
        }
    }
}

/// <summary>Supplies the visual tree for a stub agent, so a test can change it between requests.</summary>
internal interface ITreeSource
{
    Task<List<ElementInfo>> GetAsync();
}

internal static class AgentTestHarness
{
    /// <summary>
    /// Builds a single chain of nested elements <paramref name="depth"/> levels deep. Real docking
    /// layouts nest far past the System.Text.Json default of 64, which is what used to make
    /// <c>GET /api/v1/ui/tree</c> throw "maximum configured depth exceeded".
    /// </summary>
    internal static ElementInfo BuildDeepChain(int depth)
    {
        var root = new ElementInfo { Id = "e0", Type = "Panel", ParentId = null };

        var current = root;
        for (var i = 1; i < depth; i++)
        {
            var child = new ElementInfo { Id = $"e{i}", Type = "Panel", ParentId = current.Id };
            current.Children = [child];
            current = child;
        }

        return root;
    }

    /// <summary>
    /// Builds a tree that points back at its own root through <see cref="ElementInfo.Children"/>,
    /// so a serializer without cycle handling throws instead of returning a response.
    /// </summary>
    internal static ElementInfo BuildCyclicTree()
    {
        var root = new ElementInfo { Id = "root", Type = "Panel" };
        var child = new ElementInfo { Id = "child", Type = "Panel", ParentId = "root" };
        var grandChild = new ElementInfo { Id = "grandchild", Type = "Panel", ParentId = "child" };

        root.Children = [child];
        child.Children = [grandChild];
        grandChild.Children = [root];

        return root;
    }

    internal static int GetFreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    internal static readonly JsonDocumentOptions DeepTreeReaderOptions = new() { MaxDepth = 256 };

    internal static async Task<JsonElement> GetJsonAsync(HttpClient client, string path, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(path, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken), DeepTreeReaderOptions);
        return document.RootElement.Clone();
    }

    internal static async Task WaitForServerAsync(HttpClient client, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var response = await client.GetAsync("/api/v1/agent/status", cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch (HttpRequestException)
            {
            }
            catch (TaskCanceledException)
            {
            }

            await Task.Delay(50, cancellationToken);
        }

        throw new InvalidOperationException("Agent status endpoint did not become available in time.");
    }

    /// <summary>
    /// Reads the route table the agent registered with its <c>AgentHttpServer</c>. This is the
    /// surface clients actually see, which is what the protocol parity tests compare against the
    /// upstream OpenAPI contract.
    /// </summary>
    internal static IReadOnlyList<(string Method, string Path)> GetRegisteredRoutes(DevFlowAgentServiceBase service)
    {
        var serverField = typeof(DevFlowAgentServiceBase).GetField("_server", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("DevFlowAgentServiceBase._server was not found.");
        var server = serverField.GetValue(service)
            ?? throw new InvalidOperationException("DevFlowAgentServiceBase._server was null.");

        var serverType = server.GetType();
        var routes = new List<(string Method, string Path)>();

        foreach (var (method, fieldName) in new[]
        {
            ("GET", "_getRoutes"),
            ("POST", "_postRoutes"),
            ("PUT", "_putRoutes"),
            ("DELETE", "_deleteRoutes"),
        })
        {
            var field = serverType.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException($"AgentHttpServer.{fieldName} was not found.");
            var table = field.GetValue(server) as IEnumerable
                ?? throw new InvalidOperationException($"AgentHttpServer.{fieldName} was not a dictionary.");

            foreach (var entry in table)
            {
                var key = entry?.GetType().GetProperty("Key")?.GetValue(entry) as string;
                if (key is not null)
                {
                    routes.Add((method, key));
                }
            }
        }

        return routes;
    }
}