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

namespace LeXtudio.DevFlow.Agent.MewUI.Tests;

public class MewUIAgentIntegrationTests
{
    [Fact]
    public async Task MewUIDevFlowTestApp_AgentStatus_ReturnsRunning()
    {
        var runtimeIdentifier = GetRuntimeIdentifier();
        var port = GetFreePort();
        await using var host = await StartMewUIAgentHostAsync(port, runtimeIdentifier);

        using var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}") };
        var status = await PollAgentStatusAsync(client, TimeSpan.FromSeconds(20));

        Assert.True(status.GetProperty("running").GetBoolean());
        Assert.Equal("LeXtudio.DevFlow.Agent", status.GetProperty("name").GetString());
        Assert.Equal("mewui", status.GetProperty("framework").GetString());
        Assert.True(status.GetProperty("capabilities").GetProperty("appTheme").GetBoolean());
    }

    [Fact]
    public async Task TapButton_UpdatesResponseText()
    {
        var runtimeIdentifier = GetRuntimeIdentifier();
        var port = GetFreePort();
        await using var host = await StartMewUIAgentHostAsync(port, runtimeIdentifier);

        using var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}") };
        await PollAgentStatusAsync(client, TimeSpan.FromSeconds(20));

        using var tapResponse = await client.PostAsync("/api/v1/ui/tap", new StringContent("{ \"id\": \"ActionButton\" }", Encoding.UTF8, "application/json"));
        tapResponse.EnsureSuccessStatusCode();
        using var tapDoc = JsonDocument.Parse(await tapResponse.Content.ReadAsStreamAsync());
        Assert.True(tapDoc.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains(tapDoc.RootElement.GetProperty("simulationMode").GetString(), new[] { "native", "reflection" });

        using var elementResponse = await client.GetAsync("/api/v1/ui/element?id=ResponseText");
        elementResponse.EnsureSuccessStatusCode();
        using var elementDoc = JsonDocument.Parse(await elementResponse.Content.ReadAsStreamAsync());
        var text = elementDoc.RootElement.GetProperty("text").GetString();

        Assert.False(string.IsNullOrWhiteSpace(text));
        Assert.Contains("Button pressed", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Screenshot_ReturnsValidPng()
    {
        var runtimeIdentifier = GetRuntimeIdentifier();
        var port = GetFreePort();
        await using var host = await StartMewUIAgentHostAsync(port, runtimeIdentifier);

        using var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}") };
        await PollAgentStatusAsync(client, TimeSpan.FromSeconds(20));

        using var screenshotResponse = await client.GetAsync("/api/v1/ui/screenshot");
        screenshotResponse.EnsureSuccessStatusCode();

        var screenshotBytes = await screenshotResponse.Content.ReadAsByteArrayAsync();
        Assert.NotEmpty(screenshotBytes);
        Assert.True(IsPng(screenshotBytes));
    }

    [Fact]
    public async Task FillAndClear_UpdatesElementText()
    {
        var runtimeIdentifier = GetRuntimeIdentifier();
        var port = GetFreePort();
        await using var host = await StartMewUIAgentHostAsync(port, runtimeIdentifier);

        using var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}") };
        await PollAgentStatusAsync(client, TimeSpan.FromSeconds(20));

        using var fillResponse = await client.PostAsync(
            "/api/v1/ui/actions/fill",
            new StringContent("{\"elementId\":\"ResponseText\",\"text\":\"Filled by test\"}", Encoding.UTF8, "application/json"));
        fillResponse.EnsureSuccessStatusCode();
        using var fillResultDoc = JsonDocument.Parse(await fillResponse.Content.ReadAsStreamAsync());
        Assert.True(fillResultDoc.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains(fillResultDoc.RootElement.GetProperty("simulationMode").GetString(), new[] { "native", "property-mutation" });

        using var afterFill = await client.GetAsync("/api/v1/ui/element?id=ResponseText");
        afterFill.EnsureSuccessStatusCode();
        using var fillDoc = JsonDocument.Parse(await afterFill.Content.ReadAsStreamAsync());
        Assert.Equal("Filled by test", fillDoc.RootElement.GetProperty("text").GetString());

        using var clearResponse = await client.PostAsync(
            "/api/v1/ui/actions/clear",
            new StringContent("{\"elementId\":\"ResponseText\"}", Encoding.UTF8, "application/json"));
        clearResponse.EnsureSuccessStatusCode();

        using var afterClear = await client.GetAsync("/api/v1/ui/element?id=ResponseText");
        afterClear.EnsureSuccessStatusCode();
        using var clearDoc = JsonDocument.Parse(await afterClear.Content.ReadAsStreamAsync());
        Assert.Equal(string.Empty, clearDoc.RootElement.GetProperty("text").GetString());
    }

    [Fact]
    public async Task KeyEnter_ReturnsSuccessPayload()
    {
        var runtimeIdentifier = GetRuntimeIdentifier();
        var port = GetFreePort();
        await using var host = await StartMewUIAgentHostAsync(port, runtimeIdentifier);

        using var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}") };
        await PollAgentStatusAsync(client, TimeSpan.FromSeconds(20));

        using var keyResponse = await client.PostAsync(
            "/api/v1/ui/actions/key",
            new StringContent("{\"elementId\":\"ResponseText\",\"key\":\"enter\"}", Encoding.UTF8, "application/json"));
        keyResponse.EnsureSuccessStatusCode();
        using var keyDoc = JsonDocument.Parse(await keyResponse.Content.ReadAsStreamAsync());
        Assert.True(keyDoc.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains(keyDoc.RootElement.GetProperty("simulationMode").GetString(), new[] { "native", "semantic" });
    }

    [Fact]
    public async Task Focus_ReturnsSuccess()
    {
        var runtimeIdentifier = GetRuntimeIdentifier();
        var port = GetFreePort();
        await using var host = await StartMewUIAgentHostAsync(port, runtimeIdentifier);

        using var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}") };
        await PollAgentStatusAsync(client, TimeSpan.FromSeconds(20));

        using var focusResponse = await client.PostAsync(
            "/api/v1/ui/actions/focus",
            new StringContent("{\"elementId\":\"ActionButton\"}", Encoding.UTF8, "application/json"));
        focusResponse.EnsureSuccessStatusCode();
        using var focusDoc = JsonDocument.Parse(await focusResponse.Content.ReadAsStreamAsync());
        Assert.True(focusDoc.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains(focusDoc.RootElement.GetProperty("simulationMode").GetString(), new[] { "native", "semantic" });
    }

    [Fact]
    public async Task BatchActions_SucceedsForTapAndFill()
    {
        var runtimeIdentifier = GetRuntimeIdentifier();
        var port = GetFreePort();
        await using var host = await StartMewUIAgentHostAsync(port, runtimeIdentifier);

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

        using var batchResponse = await client.PostAsync(
            "/api/v1/ui/actions/batch",
            new StringContent(body, Encoding.UTF8, "application/json"));
        batchResponse.EnsureSuccessStatusCode();
        using var batchDoc = JsonDocument.Parse(await batchResponse.Content.ReadAsStreamAsync());
        Assert.True(batchDoc.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(2, batchDoc.RootElement.GetProperty("results").GetArrayLength());
    }

    [Fact]
    public async Task Theme_GetAndSet_ReturnsThemePayload()
    {
        var runtimeIdentifier = GetRuntimeIdentifier();
        var port = GetFreePort();
        await using var host = await StartMewUIAgentHostAsync(port, runtimeIdentifier);

        using var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}") };
        await PollAgentStatusAsync(client, TimeSpan.FromSeconds(20));

        using var getResponse = await client.GetAsync("/api/v1/device/app/theme");
        getResponse.EnsureSuccessStatusCode();
        using var getDoc = JsonDocument.Parse(await getResponse.Content.ReadAsStreamAsync());
        Assert.True(getDoc.RootElement.TryGetProperty("supportedThemes", out _));

        using var setResponse = await client.PutAsync(
            "/api/v1/device/app/theme",
            new StringContent("{\"theme\":\"light\"}", Encoding.UTF8, "application/json"));
        setResponse.EnsureSuccessStatusCode();
        using var setDoc = JsonDocument.Parse(await setResponse.Content.ReadAsStreamAsync());
        Assert.Equal("light", setDoc.RootElement.GetProperty("userAppTheme").GetString());
        Assert.Equal("light", setDoc.RootElement.GetProperty("theme").GetString());
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

    private static int GetFreePort()
    {
        using var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        return ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static async Task<DisposableProcessHost> StartMewUIAgentHostAsync(int port, string runtimeIdentifier)
    {
        var repoRoot = FindRepositoryRoot(Directory.GetCurrentDirectory());
        var hostProjectPath = Path.GetFullPath(Path.Combine(repoRoot, "src", "DevFlow", "MewUIDevFlowTestApp", "MewUIDevFlowTestApp.csproj"));
        if (!File.Exists(hostProjectPath))
            throw new InvalidOperationException($"Unable to locate MewUI host project at {hostProjectPath}");

        var hostProjectDirectory = Path.GetDirectoryName(hostProjectPath)!;
        if (!RunCommand("dotnet", $"build \"{hostProjectPath}\" -c Debug -r {runtimeIdentifier}", hostProjectDirectory, out var buildOutput, out var buildError))
            throw new InvalidOperationException($"Failed to build MewUI host project:\n{buildError}\n{buildOutput}");

        var outputPath = Path.Combine(hostProjectDirectory, "bin", "Debug", "net10.0", runtimeIdentifier);
        var exePath = runtimeIdentifier.StartsWith("win", StringComparison.OrdinalIgnoreCase)
            ? Path.Combine(outputPath, "MewUIDevFlowTestApp.exe")
            : Path.Combine(outputPath, "MewUIDevFlowTestApp.dll");

        if (!File.Exists(exePath))
            throw new InvalidOperationException($"Unable to locate MewUI host executable at {exePath}");

        var process = StartHiddenProcess(exePath, hostProjectDirectory, port);
        if (process == null || process.HasExited)
            throw new InvalidOperationException("Failed to start the external MewUI host process.");

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
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        if (string.Equals(Path.GetExtension(exePath), ".exe", StringComparison.OrdinalIgnoreCase))
        {
            startInfo.FileName = exePath;
        }
        else
        {
            startInfo.FileName = "dotnet";
            startInfo.Arguments = $"\"{exePath}\"";
        }

        startInfo.Environment["DEVFLOW_AGENT_PORT"] = port.ToString();

        var process = Process.Start(startInfo);
        if (process != null)
        {
            process.OutputDataReceived += (_, e) => { if (e.Data != null) Console.WriteLine("[MewUI host] " + e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data != null) Console.Error.WriteLine("[MewUI host] " + e.Data); };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }

        return process;
    }

    private static string GetRuntimeIdentifier()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "win-arm64" : "win-x64";

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) || RuntimeInformation.OSDescription.Contains("Darwin", StringComparison.OrdinalIgnoreCase))
            return RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "osx-arm64" : "osx-x64";

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.OSDescription.Contains("Linux", StringComparison.OrdinalIgnoreCase))
            return RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "linux-arm64" : "linux-x64";

        var uname = GetUname();
        if (uname.Equals("Darwin", StringComparison.OrdinalIgnoreCase))
            return RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "osx-arm64" : "osx-x64";
        if (uname.Equals("Linux", StringComparison.OrdinalIgnoreCase))
            return RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "linux-arm64" : "linux-x64";

        throw new PlatformNotSupportedException($"Unsupported OS for MewUI tests: {RuntimeInformation.OSDescription}");
    }

    private static bool IsPng(byte[] bytes)
    {
        var pngHeader = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 };
        if (bytes.Length < pngHeader.Length)
            return false;

        for (var i = 0; i < pngHeader.Length; i++)
        {
            if (bytes[i] != pngHeader[i])
                return false;
        }

        return true;
    }

    private static string GetUname()
    {
        try
        {
            using var process = new Process();
            process.StartInfo.FileName = "/usr/bin/uname";
            process.StartInfo.Arguments = "-s";
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;
            process.Start();
            var output = process.StandardOutput.ReadLine();
            process.WaitForExit();
            return output ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
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
