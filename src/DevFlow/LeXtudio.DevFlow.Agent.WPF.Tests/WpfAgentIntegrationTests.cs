using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Maui.DevFlow.Agent.Core;
using Xunit;

namespace LeXtudio.DevFlow.Agent.WPF.Tests;

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
        Assert.True(treeDoc.RootElement.GetProperty("elements").GetArrayLength() > 0);

        using var screenshotResponse = await client.GetAsync("/api/v1/ui/screenshot");
        screenshotResponse.EnsureSuccessStatusCode();
        var screenshotBytes = await screenshotResponse.Content.ReadAsByteArrayAsync();

        Assert.NotEmpty(screenshotBytes);
        Assert.True(IsPng(screenshotBytes));
    }

    [Fact]
    public async Task Screenshot_ReturnsValidPng()
    {
        var port = GetFreePort();
        await using var host = await StartWpfAgentHostAsync(port);

        using var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}") };
        await PollAgentStatusAsync(client, TimeSpan.FromSeconds(15));

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

        using var tapResponse = await client.PostAsync("/api/v1/ui/tap", new StringContent("{ \"id\": \"ActionButton\" }", System.Text.Encoding.UTF8, "application/json"));
        tapResponse.EnsureSuccessStatusCode();

        using var elementResponse = await client.GetAsync("/api/v1/ui/element?id=ResponseText");
        elementResponse.EnsureSuccessStatusCode();
        using var elementDoc = JsonDocument.Parse(await elementResponse.Content.ReadAsStreamAsync());
        var text = elementDoc.RootElement.GetProperty("text").GetString();

        Assert.False(string.IsNullOrWhiteSpace(text));
        Assert.Contains("Button clicked at", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ScrollViewer_UpdatesVerticalOffset()
    {
        var port = GetFreePort();
        await using var host = await StartWpfAgentHostAsync(port);

        using var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}") };
        await PollAgentStatusAsync(client, TimeSpan.FromSeconds(15));

        using var scrollResponse = await client.PostAsync(
            "/api/v1/ui/actions/scroll",
            new StringContent("{ \"id\": \"MainScrollViewer\", \"deltaY\": 150 }", Encoding.UTF8, "application/json"));
        scrollResponse.EnsureSuccessStatusCode();

        using var elementResponse = await client.GetAsync("/api/v1/ui/element?id=MainScrollViewer");
        elementResponse.EnsureSuccessStatusCode();
        using var elementDoc = JsonDocument.Parse(await elementResponse.Content.ReadAsStreamAsync());
        var offset = elementDoc.RootElement
            .GetProperty("frameworkProperties")
            .GetProperty("verticalOffset")
            .GetString();

        Assert.True(double.TryParse(offset, out var offsetValue) && offsetValue > 0);
    }

    [Fact]
    public async Task ScrollTargetText_IsPresentAfterScroll()
    {
        var port = GetFreePort();
        await using var host = await StartWpfAgentHostAsync(port);

        using var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}") };
        await PollAgentStatusAsync(client, TimeSpan.FromSeconds(15));

        using var scrollResponse = await client.PostAsync(
            "/api/v1/ui/actions/scroll",
            new StringContent("{ \"id\": \"MainScrollViewer\", \"deltaY\": 150 }", Encoding.UTF8, "application/json"));
        scrollResponse.EnsureSuccessStatusCode();

        using var targetResponse = await client.GetAsync("/api/v1/ui/element?id=ScrollTargetText");
        targetResponse.EnsureSuccessStatusCode();
        using var targetDoc = JsonDocument.Parse(await targetResponse.Content.ReadAsStreamAsync());
        var text = targetDoc.RootElement.GetProperty("text").GetString();

        Assert.Equal("Scroll target is here!", text);
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
            catch (HttpRequestException)
            {
            }
            catch (TaskCanceledException)
            {
            }

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
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static async Task<DisposableProcessHost> StartWpfAgentHostAsync(int port)
    {
        var repoRoot = FindRepositoryRoot(Directory.GetCurrentDirectory());
        var hostProjectPath = Path.GetFullPath(Path.Combine(repoRoot, "src", "DevFlow", "WpfDevFlowTestApp", "WpfDevFlowTestApp.csproj"));
        if (!File.Exists(hostProjectPath))
            throw new InvalidOperationException($"Unable to locate WPF host project at {hostProjectPath}");

        var hostProjectDirectory = Path.GetDirectoryName(hostProjectPath)!;
        if (!RunCommand("dotnet", $"build \"{hostProjectPath}\" -c Debug", hostProjectDirectory, out var buildOutput, out var buildError))
            throw new InvalidOperationException($"Failed to build WPF host project:\n{buildError}\n{buildOutput}");

        var exePath = Path.Combine(hostProjectDirectory, "bin", "Debug", "net10.0-windows", "WpfDevFlowTestApp.exe");
        if (!File.Exists(exePath))
            throw new InvalidOperationException($"Unable to locate WPF host executable at {exePath}");

        var process = StartHiddenProcess(exePath, hostProjectDirectory, port);
        if (process == null || process.HasExited)
            throw new InvalidOperationException("Failed to start the external WPF host process.");

        return new DisposableProcessHost(process);
    }

    private static string FindRepositoryRoot(string startFolder)
    {
        var current = new DirectoryInfo(startFolder);
        while (current != null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "src", "DevFlow")))
                return current.FullName;

            current = current.Parent;
        }

        throw new InvalidOperationException("Unable to locate the repository root containing src/DevFlow.");
    }

    private static Process? StartHiddenProcess(string exePath, string workingDirectory, int port)
    {
        var startInfo = new ProcessStartInfo(exePath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory,
        };

        startInfo.Environment["DEVFLOW_AGENT_PORT"] = port.ToString();
        startInfo.Environment["DEVFLOW_HIDE_WINDOW"] = "true";

        return Process.Start(startInfo);
    }

    private static bool RunCommand(string command, string arguments, string workingDirectory, out string output, out string error)
    {
        using var process = new Process();
        process.StartInfo.FileName = command;
        process.StartInfo.Arguments = arguments;
        process.StartInfo.WorkingDirectory = workingDirectory;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.CreateNoWindow = true;
        process.StartInfo.UseShellExecute = false;

        var stdout = new StringWriter();
        var stderr = new StringWriter();
        process.OutputDataReceived += (_, e) => { if (e.Data != null) stdout.WriteLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) stderr.WriteLine(e.Data); };

        if (!process.Start())
        {
            output = string.Empty;
            error = "Failed to start process.";
            return false;
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        process.WaitForExit();

        output = stdout.ToString();
        error = stderr.ToString();
        return process.ExitCode == 0;
    }

    private sealed class DisposableProcessHost : IAsyncDisposable
    {
        private readonly Process _process;
        private bool _disposed;

        public DisposableProcessHost(Process process)
        {
            _process = process;
        }

        public ValueTask DisposeAsync()
        {
            if (_disposed)
                return ValueTask.CompletedTask;

            _disposed = true;
            if (!_process.HasExited)
            {
                _process.Kill(true);
                _process.WaitForExit(5000);
            }

            _process.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
