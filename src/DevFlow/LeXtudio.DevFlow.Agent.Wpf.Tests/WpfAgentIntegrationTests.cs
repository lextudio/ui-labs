using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace LeXtudio.DevFlow.Agent.Wpf.Tests;

public class WpfAgentIntegrationTests
{
    [Fact]
    public async Task AgentStatusTreeAndScreenshot_ReturnsValidData()
    {
        var port = GetFreePort();
        await using var host = await StartWpfAgentHostAsync(port);

        using var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}") };
        var status = await PollAgentStatusAsync(client, TimeSpan.FromSeconds(15));

        Assert.True(status.GetProperty("running").GetBoolean());
        Assert.Equal("LeXtudio.DevFlow.Agent", status.GetProperty("name").GetString());

        using var treeResponse = await client.GetAsync("/api/v1/ui/tree");
        treeResponse.EnsureSuccessStatusCode();
        using var treeDoc = JsonDocument.Parse(await treeResponse.Content.ReadAsStreamAsync());
        Assert.True(treeDoc.RootElement.GetArrayLength() > 0);

        using var screenshotResponse = await client.GetAsync("/api/v1/ui/screenshot");
        screenshotResponse.EnsureSuccessStatusCode();
        var screenshotBytes = await screenshotResponse.Content.ReadAsByteArrayAsync();

        Assert.NotEmpty(screenshotBytes);
        Assert.True(IsPng(screenshotBytes));
    }

    [Fact]
    public async Task TapButton_UpdatesResponseText()
    {
        var port = GetFreePort();
        await using var host = await StartWpfAgentHostAsync(port);

        using var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}") };
        var status = await PollAgentStatusAsync(client, TimeSpan.FromSeconds(15));

        Assert.True(status.GetProperty("running").GetBoolean());

        using var tapResponse = await LeXtudio.DevFlow.Driver.DevFlowMutation.SendAsync(
            client,
            HttpMethod.Post,
            "/api/v1/ui/actions/tap",
            new StringContent("{ \"id\": \"ActionButton\" }", System.Text.Encoding.UTF8, "application/json"),
            TestContext.Current.CancellationToken);
        tapResponse.EnsureSuccessStatusCode();

        using var elementResponse = await client.GetAsync("/api/v1/ui/elements/ResponseText");
        elementResponse.EnsureSuccessStatusCode();
        using var elementDoc = JsonDocument.Parse(await elementResponse.Content.ReadAsStreamAsync());
        var text = elementDoc.RootElement.GetProperty("text").GetString();

        Assert.False(string.IsNullOrWhiteSpace(text));
        Assert.Contains("Button clicked at", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// macOS only: with DEVFLOW_POINTER_MODE=in-process a drag is delivered through the app's own input
    /// pipeline (press, moves, release), so it needs no Accessibility permission - CliclickSharp's
    /// OS-level events are silently discarded without it while still reporting success. The response
    /// must say "portable-wpf", and the app must have really received the release over the button.
    /// </summary>
    [Fact]
    public async Task Drag_InProcessPointerMode_DeliversRealInputWithoutAccessibility()
    {
        if (!OperatingSystem.IsMacOS())
            return;

        var port = GetFreePort();
        await using var host = await StartWpfAgentHostAsync(port, ("DEVFLOW_POINTER_MODE", "in-process"));

        using var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}") };
        await PollAgentStatusAsync(client, TimeSpan.FromSeconds(30));

        using var before = await client.GetAsync("/api/v1/ui/elements/ResponseText");
        using var beforeDoc = JsonDocument.Parse(await before.Content.ReadAsStreamAsync());
        Assert.DoesNotContain("Button clicked at", beforeDoc.RootElement.GetProperty("text").GetString() ?? "", StringComparison.Ordinal);

        using var dragResponse = await LeXtudio.DevFlow.Driver.DevFlowMutation.SendAsync(
            client,
            HttpMethod.Post,
            "/api/v1/ui/actions/drag",
            new StringContent("{ \"fromId\": \"ActionButton\", \"dx\": 2, \"dy\": 0 }", System.Text.Encoding.UTF8, "application/json"),
            TestContext.Current.CancellationToken);
        dragResponse.EnsureSuccessStatusCode();
        using var dragDoc = JsonDocument.Parse(await dragResponse.Content.ReadAsStreamAsync());
        Assert.True(dragDoc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("portable-wpf", dragDoc.RootElement.GetProperty("mode").GetString());

        using var after = await client.GetAsync("/api/v1/ui/elements/ResponseText");
        using var afterDoc = JsonDocument.Parse(await after.Content.ReadAsStreamAsync());
        Assert.Contains("Button clicked at", afterDoc.RootElement.GetProperty("text").GetString(), StringComparison.Ordinal);
    }

    private static async Task<JsonElement> PollAgentStatusAsync(HttpClient client, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var response = await client.GetAsync("/api/v1/agent/status");
                if (response.IsSuccessStatusCode)
                {
                    using var doc = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
                    return doc.RootElement.Clone();
                }
            }
            catch (HttpRequestException) { }
            catch (TaskCanceledException) { }

            await Task.Delay(250);
        }

        throw new InvalidOperationException("Agent status endpoint did not become available in time.");
    }

    private static bool IsPng(byte[] bytes)
    {
        var pngHeader = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 };
        return bytes.Length >= pngHeader.Length && bytes.Take(pngHeader.Length).SequenceEqual(pngHeader);
    }

    private static int GetFreePort()
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

    private static async Task<IAsyncDisposable> StartWpfAgentHostAsync(int port, params (string Name, string Value)[] environment)
    {
        var projectDir = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "DevFlow", "WpfDevFlowTestApp"));

        var csproj = Directory.GetFiles(projectDir, "*.csproj").FirstOrDefault()
            ?? throw new InvalidOperationException($"No csproj found in {projectDir}");

        var psi = new ProcessStartInfo("dotnet", $"run --project \"{csproj}\" -c Debug")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.Environment["DEVFLOW_AGENT_PORT"] = port.ToString();
        foreach (var (name, value) in environment)
            psi.Environment[name] = value;

        var process = new Process { StartInfo = psi };
        process.Start();

        return new AgentHost(process);
    }

    private sealed class AgentHost : IAsyncDisposable
    {
        private readonly Process _process;
        public AgentHost(Process process) => _process = process;

        public async ValueTask DisposeAsync()
        {
            if (!_process.HasExited)
                _process.Kill(entireProcessTree: true);
            _process.Dispose();
            await Task.CompletedTask;
        }
    }
}
