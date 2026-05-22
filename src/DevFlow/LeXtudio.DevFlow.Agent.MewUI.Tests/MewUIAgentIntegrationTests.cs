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
