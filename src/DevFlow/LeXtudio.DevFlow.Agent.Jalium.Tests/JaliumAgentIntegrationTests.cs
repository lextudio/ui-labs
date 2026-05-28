using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace LeXtudio.DevFlow.Agent.Jalium.Tests;

public class JaliumAgentIntegrationTests
{
    public JaliumAgentIntegrationTests()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            Assert.Skip("Jalium integration tests require Windows — jalium.native.platform is Windows-only.");
    }

    [Fact]
    public async Task JaliumDevFlowTestApp_AgentStatus_ReturnsRunning()
    {
        var port = GetFreePort();
        await using var host = await StartJaliumAgentHostAsync(port);

        using var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}") };
        var status = await PollAgentStatusAsync(client, TimeSpan.FromSeconds(20));

        Assert.True(status.GetProperty("running").GetBoolean());
        Assert.Equal("LeXtudio.DevFlow.Agent", status.GetProperty("name").GetString());
        Assert.Equal("jalium", status.GetProperty("framework").GetString());
    }

    [Fact]
    public async Task TapButton_UpdatesResponseText()
    {
        var port = GetFreePort();
        await using var host = await StartJaliumAgentHostAsync(port);

        using var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}") };
        await PollAgentStatusAsync(client, TimeSpan.FromSeconds(20));

        using var tapResponse = await PostAsync(client, "/api/v1/ui/tap", new StringContent("{ \"id\": \"ActionButton\" }", Encoding.UTF8, "application/json"));
        tapResponse.EnsureSuccessStatusCode();
        using var tapDoc = JsonDocument.Parse(await ReadAsStreamAsync(tapResponse.Content));
        Assert.True(tapDoc.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains(tapDoc.RootElement.GetProperty("simulationMode").GetString(), new[] { "native", "reflection" });

        using var elementResponse = await GetAsync(client, "/api/v1/ui/element?id=ResponseText");
        elementResponse.EnsureSuccessStatusCode();
        using var elementDoc = JsonDocument.Parse(await ReadAsStreamAsync(elementResponse.Content));
        var text = elementDoc.RootElement.GetProperty("text").GetString();
        Assert.False(string.IsNullOrWhiteSpace(text));
        Assert.Contains("Button pressed", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Screenshot_ReturnsValidPng()
    {
        var port = GetFreePort();
        await using var host = await StartJaliumAgentHostAsync(port);

        using var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}") };
        await PollAgentStatusAsync(client, TimeSpan.FromSeconds(20));

        using var screenshotResponse = await GetAsync(client, "/api/v1/ui/screenshot");
        screenshotResponse.EnsureSuccessStatusCode();

        var screenshotBytes = await ReadAsByteArrayAsync(screenshotResponse.Content);
        Assert.NotEmpty(screenshotBytes);
        Assert.True(IsPng(screenshotBytes));
    }

    [Fact]
    public async Task FillAndClear_UpdatesElementText()
    {
        var port = GetFreePort();
        await using var host = await StartJaliumAgentHostAsync(port);

        using var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}") };
        await PollAgentStatusAsync(client, TimeSpan.FromSeconds(20));

        using var fillResponse = await PostAsync(client,
            "/api/v1/ui/actions/fill",
            new StringContent("{\"elementId\":\"ResponseText\",\"text\":\"Filled by test\"}", Encoding.UTF8, "application/json"));
        fillResponse.EnsureSuccessStatusCode();
        using var fillResultDoc = JsonDocument.Parse(await ReadAsStreamAsync(fillResponse.Content));
        Assert.True(fillResultDoc.RootElement.GetProperty("success").GetBoolean());

        using var afterFill = await GetAsync(client, "/api/v1/ui/element?id=ResponseText");
        afterFill.EnsureSuccessStatusCode();
        using var fillDoc = JsonDocument.Parse(await ReadAsStreamAsync(afterFill.Content));
        Assert.Equal("Filled by test", fillDoc.RootElement.GetProperty("text").GetString());

        using var clearResponse = await PostAsync(client,
            "/api/v1/ui/actions/clear",
            new StringContent("{\"elementId\":\"ResponseText\"}", Encoding.UTF8, "application/json"));
        clearResponse.EnsureSuccessStatusCode();

        using var afterClear = await GetAsync(client, "/api/v1/ui/element?id=ResponseText");
        afterClear.EnsureSuccessStatusCode();
        using var clearDoc = JsonDocument.Parse(await ReadAsStreamAsync(afterClear.Content));
        Assert.Equal(string.Empty, clearDoc.RootElement.GetProperty("text").GetString());
    }

    [Fact]
    public async Task BatchActions_SucceedsForTapAndFill()
    {
        var port = GetFreePort();
        await using var host = await StartJaliumAgentHostAsync(port);

        using var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}") };
        await PollAgentStatusAsync(client, TimeSpan.FromSeconds(20));

        const string body = """
                            {
                              "actions": [
                                { "action": "tap", "elementId": "ActionButton" },
                                { "action": "fill", "elementId": "ResponseText", "text": "Batch updated" }
                              ]
                            }
                            """;

        using var batchResponse = await PostAsync(client,
            "/api/v1/ui/actions/batch",
            new StringContent(body, Encoding.UTF8, "application/json"));
        batchResponse.EnsureSuccessStatusCode();
        using var batchDoc = JsonDocument.Parse(await ReadAsStreamAsync(batchResponse.Content));
        Assert.True(batchDoc.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(2, batchDoc.RootElement.GetProperty("results").GetArrayLength());
    }

    private static async Task<JsonElement> PollAgentStatusAsync(HttpClient client, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var response = await GetAsync(client, "/api/v1/agent/status");
                if (response.IsSuccessStatusCode)
                {
                    using var doc = JsonDocument.Parse(await ReadAsStreamAsync(response.Content));
                    return doc.RootElement.Clone();
                }
            }
            catch (HttpRequestException) { }
            catch (TaskCanceledException) { }

            await Delay(250);
        }

        throw new InvalidOperationException("Agent status endpoint did not become available in time.");
    }

    private static int GetFreePort()
    {
        using var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        return ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static async Task<DisposableProcessHost> StartJaliumAgentHostAsync(int port)
    {
        var rid = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "win-arm64" : "win-x64";

        var repoRoot = FindRepositoryRoot(Directory.GetCurrentDirectory());
        var hostProjectPath = Path.GetFullPath(Path.Combine(repoRoot, "src", "DevFlow", "JaliumDevFlowTestApp", "JaliumDevFlowTestApp.csproj"));
        if (!File.Exists(hostProjectPath))
            throw new InvalidOperationException($"Unable to locate Jalium host project at {hostProjectPath}");

        var hostProjectDirectory = Path.GetDirectoryName(hostProjectPath)!;
        if (!RunCommand("dotnet", $"build \"{hostProjectPath}\" -c Debug -r {rid}", hostProjectDirectory, out var buildOutput, out var buildError))
            throw new InvalidOperationException($"Failed to build Jalium host project:\n{buildError}\n{buildOutput}");

        var outputPath = Path.Combine(hostProjectDirectory, "bin", "Debug", "net10.0", rid);
        var exePath = Path.Combine(outputPath, "JaliumDevFlowTestApp.exe");

        if (!File.Exists(exePath))
            throw new InvalidOperationException($"Unable to locate Jalium host executable at {exePath}");

        var process = StartHiddenProcess(exePath, hostProjectDirectory, port);
        if (process == null || process.HasExited)
            throw new InvalidOperationException("Failed to start the external Jalium host process.");

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
        var startInfo = new ProcessStartInfo
        {
            FileName = exePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.Environment["DEVFLOW_AGENT_PORT"] = port.ToString();

        var process = Process.Start(startInfo);
        if (process != null)
        {
            process.OutputDataReceived += (_, e) => { if (e.Data != null) Console.WriteLine("[Jalium host] " + e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data != null) Console.Error.WriteLine("[Jalium host] " + e.Data); };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }

        return process;
    }

    private static bool IsPng(byte[] bytes)
    {
        var pngHeader = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 };
        if (bytes.Length < pngHeader.Length) return false;
        for (var i = 0; i < pngHeader.Length; i++)
            if (bytes[i] != pngHeader[i]) return false;
        return true;
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

        if (!process.Start()) { output = string.Empty; error = "Failed to start process."; return false; }

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

        public DisposableProcessHost(Process process) { _process = process; }

        public ValueTask DisposeAsync()
        {
            if (_disposed) return ValueTask.CompletedTask;
            _disposed = true;
            if (!_process.HasExited) { _process.Kill(true); _process.WaitForExit(5000); }
            _process.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    private static Task<HttpResponseMessage> GetAsync(HttpClient client, string requestUri) => client.GetAsync(requestUri, TestContext.Current.CancellationToken);
    private static Task<HttpResponseMessage> PostAsync(HttpClient client, string requestUri, HttpContent content) => client.PostAsync(requestUri, content, TestContext.Current.CancellationToken);
    private static Task<Stream> ReadAsStreamAsync(HttpContent content) => content.ReadAsStreamAsync(TestContext.Current.CancellationToken);
    private static Task<byte[]> ReadAsByteArrayAsync(HttpContent content) => content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
    private static Task Delay(int millisecondsTimeout) => Task.Delay(millisecondsTimeout, TestContext.Current.CancellationToken);
}
