using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace LeXtudio.DevFlow.Agent.Uno.Tests;

public class UnoAgentIntegrationTests
{
    public static IEnumerable<object[]> UnoTestTargets
    {
        get
        {
            yield return new object[] { "net10.0-desktop" };
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                yield return new object[] { "net10.0-windows10.0.19041.0" };
            }
        }
    }

    [Theory]
    [MemberData(nameof(UnoTestTargets))]
    public async Task UnoDevFlowTestApp_AgentStatus_ReturnsRunning(string targetFramework)
    {
        var repoRoot = FindRepositoryRoot(Directory.GetCurrentDirectory());
        var hostProjectPath = Path.GetFullPath(Path.Combine(repoRoot, "src", "DevFlow", "UnoDevFlowTestApp", "UnoDevFlowTestApp", "UnoDevFlowTestApp.csproj"));
        if (!File.Exists(hostProjectPath))
            throw new InvalidOperationException($"Unable to locate Uno host project at {hostProjectPath}");

        var hostProjectDirectory = Path.GetDirectoryName(hostProjectPath)!;
        BuildHostProject(hostProjectPath, targetFramework, hostProjectDirectory);

        var exePath = GetHostExecutablePath(hostProjectDirectory, targetFramework);
        if (!File.Exists(exePath))
            throw new InvalidOperationException($"Unable to locate Uno host executable at {exePath}");

        var port = GetFreePort();
        using var process = StartHiddenProcess(exePath, hostProjectDirectory, port);
        if (process == null || process.HasExited)
            throw new InvalidOperationException("Failed to start the Uno host process.");

        try
        {
            using var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}") };
            var status = await PollAgentStatusAsync(client, TimeSpan.FromSeconds(20));

            Assert.True(status.GetProperty("running").GetBoolean());
            Assert.Equal("LeXtudio.DevFlow.Agent", status.GetProperty("name").GetString());
            Assert.Equal("uno", status.GetProperty("framework").GetString());
            var capabilities = status.GetProperty("capabilities");
            Assert.True(capabilities.GetProperty("screenshots").GetBoolean());
            Assert.True(capabilities.GetProperty("elementScreenshots").GetBoolean());
            Assert.True(capabilities.GetProperty("tap").GetBoolean());
            Assert.True(capabilities.GetProperty("scroll").GetBoolean());
            Assert.False(capabilities.GetProperty("selectorScreenshots").GetBoolean());
            Assert.True(capabilities.GetProperty("structuredErrors").GetBoolean());
            Assert.True(capabilities.GetProperty("webviewCdp").GetBoolean());
            Assert.True(capabilities.GetProperty("multiWindow").GetBoolean());
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(true);
                process.WaitForExit(5000);
            }
        }
    }

    [Theory]
    [MemberData(nameof(UnoTestTargets))]
    public async Task TapButton_UpdatesResponseText(string targetFramework)
    {
        var repoRoot = FindRepositoryRoot(Directory.GetCurrentDirectory());
        var hostProjectPath = Path.GetFullPath(Path.Combine(repoRoot, "src", "DevFlow", "UnoDevFlowTestApp", "UnoDevFlowTestApp", "UnoDevFlowTestApp.csproj"));
        if (!File.Exists(hostProjectPath))
            throw new InvalidOperationException($"Unable to locate Uno host project at {hostProjectPath}");

        var hostProjectDirectory = Path.GetDirectoryName(hostProjectPath)!;
        BuildHostProject(hostProjectPath, targetFramework, hostProjectDirectory);

        var exePath = GetHostExecutablePath(hostProjectDirectory, targetFramework);
        if (!File.Exists(exePath))
            throw new InvalidOperationException($"Unable to locate Uno host executable at {exePath}");

        var port = GetFreePort();
        using var process = StartHiddenProcess(exePath, hostProjectDirectory, port);
        if (process == null || process.HasExited)
            throw new InvalidOperationException("Failed to start the Uno host process.");

        try
        {
            using var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}") };
            await PollAgentStatusAsync(client, TimeSpan.FromSeconds(20));

            using var tapResponse = await client.PostAsync("/api/v1/ui/tap", new StringContent("{\"id\":\"ActionButton\"}", Encoding.UTF8, "application/json"));
            tapResponse.EnsureSuccessStatusCode();

            using var elementResponse = await client.GetAsync("/api/v1/ui/element?id=ResponseText");
            elementResponse.EnsureSuccessStatusCode();
            using var elementDoc = JsonDocument.Parse(await elementResponse.Content.ReadAsStreamAsync());
            var text = elementDoc.RootElement.GetProperty("text").GetString();

            Assert.False(string.IsNullOrWhiteSpace(text));
            Assert.Contains("Button clicked", text, StringComparison.Ordinal);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(true);
                process.WaitForExit(5000);
            }
        }
    }

    [Theory]
    [MemberData(nameof(UnoTestTargets))]
    public async Task Screenshot_ReturnsValidPng(string targetFramework)
    {
        var repoRoot = FindRepositoryRoot(Directory.GetCurrentDirectory());
        var hostProjectPath = Path.GetFullPath(Path.Combine(repoRoot, "src", "DevFlow", "UnoDevFlowTestApp", "UnoDevFlowTestApp", "UnoDevFlowTestApp.csproj"));
        if (!File.Exists(hostProjectPath))
            throw new InvalidOperationException($"Unable to locate Uno host project at {hostProjectPath}");

        var hostProjectDirectory = Path.GetDirectoryName(hostProjectPath)!;
        BuildHostProject(hostProjectPath, targetFramework, hostProjectDirectory);

        var exePath = GetHostExecutablePath(hostProjectDirectory, targetFramework);
        if (!File.Exists(exePath))
            throw new InvalidOperationException($"Unable to locate Uno host executable at {exePath}");

        var port = GetFreePort();
        using var process = StartHiddenProcess(exePath, hostProjectDirectory, port);
        if (process == null || process.HasExited)
            throw new InvalidOperationException("Failed to start the Uno host process.");

        try
        {
            using var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}") };
            await PollAgentStatusAsync(client, TimeSpan.FromSeconds(20));

            using var screenshotResponse = await client.GetAsync("/api/v1/ui/screenshot");
            screenshotResponse.EnsureSuccessStatusCode();
            var screenshotBytes = await screenshotResponse.Content.ReadAsByteArrayAsync();

            Assert.NotEmpty(screenshotBytes);
            Assert.True(IsPng(screenshotBytes));
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(true);
                process.WaitForExit(5000);
            }
        }
    }

    [Theory]
    [MemberData(nameof(UnoTestTargets))]
    public async Task ScrollViewer_UpdatesVerticalOffset(string targetFramework)
    {
        var repoRoot = FindRepositoryRoot(Directory.GetCurrentDirectory());
        var hostProjectPath = Path.GetFullPath(Path.Combine(repoRoot, "src", "DevFlow", "UnoDevFlowTestApp", "UnoDevFlowTestApp", "UnoDevFlowTestApp.csproj"));
        if (!File.Exists(hostProjectPath))
            throw new InvalidOperationException($"Unable to locate Uno host project at {hostProjectPath}");

        var hostProjectDirectory = Path.GetDirectoryName(hostProjectPath)!;
        BuildHostProject(hostProjectPath, targetFramework, hostProjectDirectory);

        var exePath = GetHostExecutablePath(hostProjectDirectory, targetFramework);
        if (!File.Exists(exePath))
            throw new InvalidOperationException($"Unable to locate Uno host executable at {exePath}");

        var port = GetFreePort();
        using var process = StartHiddenProcess(exePath, hostProjectDirectory, port);
        if (process == null || process.HasExited)
            throw new InvalidOperationException("Failed to start the Uno host process.");

        try
        {
            using var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}") };
            await PollAgentStatusAsync(client, TimeSpan.FromSeconds(20));

            using var scrollResponse = await client.PostAsync(
                "/api/v1/ui/actions/scroll",
                new StringContent("{\"id\":\"MainScrollViewer\",\"deltaY\":1000}", Encoding.UTF8, "application/json"));
            scrollResponse.EnsureSuccessStatusCode();

            // The Uno host on Windows uses the Windows desktop backend and typically reports
            // vertical offset immediately after a scroll action. Non-Windows Uno desktop hosts
            // can have different dispatch/render timing, so we poll the UI state on macOS/Linux.
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                using var elementResponse = await client.GetAsync("/api/v1/ui/element?id=MainScrollViewer");
                elementResponse.EnsureSuccessStatusCode();
                using var elementDoc = JsonDocument.Parse(await elementResponse.Content.ReadAsStreamAsync());
                var offset = elementDoc.RootElement
                    .GetProperty("frameworkProperties")
                    .GetProperty("verticalOffset")
                    .GetString();

                Assert.True(double.TryParse(offset, out var offsetValue) && offsetValue > 0,
                    "ScrollViewer verticalOffset did not increase immediately on Windows.");
            }
            else
            {
                var offsetValue = await PollForScrollOffsetAsync(client, "MainScrollViewer", TimeSpan.FromSeconds(10));
                Assert.True(offsetValue > 0, "ScrollViewer verticalOffset did not increase after scrolling.");
            }

            using var targetResponse = await client.GetAsync("/api/v1/ui/element?id=ScrollTargetText");
            targetResponse.EnsureSuccessStatusCode();
            using var targetDoc = JsonDocument.Parse(await targetResponse.Content.ReadAsStreamAsync());
            var text = targetDoc.RootElement.GetProperty("text").GetString();

            Assert.Equal("Scroll target is here!", text);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(true);
                process.WaitForExit(5000);
            }
        }
    }

    public static IEnumerable<object[]> UnoDesktopOnlyTargets
    {
        get
        {
            yield return new object[] { "net10.0-desktop" };
        }
    }

    [Theory]
    [MemberData(nameof(UnoDesktopOnlyTargets))]
    public async Task WebView_ElementScreenshot_ReturnsValidPng(string targetFramework)
    {
        var repoRoot = FindRepositoryRoot(Directory.GetCurrentDirectory());
        var hostProjectPath = Path.GetFullPath(Path.Combine(repoRoot, "src", "DevFlow", "UnoDevFlowTestApp", "UnoDevFlowTestApp", "UnoDevFlowTestApp.csproj"));
        if (!File.Exists(hostProjectPath))
            throw new InvalidOperationException($"Unable to locate Uno host project at {hostProjectPath}");

        var hostProjectDirectory = Path.GetDirectoryName(hostProjectPath)!;
        BuildHostProject(hostProjectPath, targetFramework, hostProjectDirectory);

        var exePath = GetHostExecutablePath(hostProjectDirectory, targetFramework);
        if (!File.Exists(exePath))
            throw new InvalidOperationException($"Unable to locate Uno host executable at {exePath}");

        var port = GetFreePort();
        using var process = StartHiddenProcess(exePath, hostProjectDirectory, port);
        if (process == null || process.HasExited)
            throw new InvalidOperationException("Failed to start the Uno host process.");

        try
        {
            using var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}") };
            await PollAgentStatusAsync(client, TimeSpan.FromSeconds(20));

            var screenshotBytes = await PollScreenshotAsync(client, "/api/v1/ui/screenshot?id=WebViewHost", TimeSpan.FromSeconds(20));
            Assert.NotEmpty(screenshotBytes);
            Assert.True(IsPng(screenshotBytes));
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(true);
                process.WaitForExit(5000);
            }
        }
    }

    [Theory]
    [MemberData(nameof(UnoTestTargets))]
    public async Task ElementScreenshot_ReturnsValidPng(string targetFramework)
    {
        var repoRoot = FindRepositoryRoot(Directory.GetCurrentDirectory());
        var hostProjectPath = Path.GetFullPath(Path.Combine(repoRoot, "src", "DevFlow", "UnoDevFlowTestApp", "UnoDevFlowTestApp", "UnoDevFlowTestApp.csproj"));
        if (!File.Exists(hostProjectPath))
            throw new InvalidOperationException($"Unable to locate Uno host project at {hostProjectPath}");

        var hostProjectDirectory = Path.GetDirectoryName(hostProjectPath)!;
        BuildHostProject(hostProjectPath, targetFramework, hostProjectDirectory);

        var exePath = GetHostExecutablePath(hostProjectDirectory, targetFramework);
        if (!File.Exists(exePath))
            throw new InvalidOperationException($"Unable to locate Uno host executable at {exePath}");

        var port = GetFreePort();
        using var process = StartHiddenProcess(exePath, hostProjectDirectory, port);
        if (process == null || process.HasExited)
            throw new InvalidOperationException("Failed to start the Uno host process.");

        try
        {
            using var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}") };
            await PollAgentStatusAsync(client, TimeSpan.FromSeconds(20));

            var screenshotBytes = await PollScreenshotAsync(client, "/api/v1/ui/screenshot?id=ActionButton", TimeSpan.FromSeconds(20));
            Assert.NotEmpty(screenshotBytes);
            Assert.True(IsPng(screenshotBytes));
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(true);
                process.WaitForExit(5000);
            }
        }
    }

    private static async Task<double> PollForScrollOffsetAsync(HttpClient client, string elementId, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            using var elementResponse = await client.GetAsync($"/api/v1/ui/element?id={elementId}");
            if (elementResponse.IsSuccessStatusCode)
            {
                using var elementDoc = JsonDocument.Parse(await elementResponse.Content.ReadAsStreamAsync());
                if (elementDoc.RootElement.TryGetProperty("frameworkProperties", out var frameworkProps) &&
                    frameworkProps.TryGetProperty("verticalOffset", out var offsetProp) &&
                    double.TryParse(offsetProp.GetString(), out var offsetValue) &&
                    offsetValue > 0)
                {
                    return offsetValue;
                }
            }

            await Task.Delay(250);
        }

        return 0;
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
        using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        return ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
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

    private static async Task<byte[]> PollScreenshotAsync(HttpClient client, string path, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            using var response = await client.GetAsync(path);
            if (response.IsSuccessStatusCode)
            {
                var bytes = await response.Content.ReadAsByteArrayAsync();
                if (bytes.Length > 0 && IsPng(bytes))
                    return bytes;
            }

            await Task.Delay(300);
        }

        throw new InvalidOperationException($"Screenshot endpoint did not return a PNG in time: {path}");
    }

    private static Process? StartHiddenProcess(string exePath, string workingDirectory, int port)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = exePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory,
        };

        if (string.Equals(Path.GetExtension(exePath), ".dll", StringComparison.OrdinalIgnoreCase))
        {
            startInfo.FileName = "dotnet";
            startInfo.Arguments = $"\"{exePath}\"";
        }

        startInfo.Environment["DEVFLOW_AGENT_PORT"] = port.ToString();
        startInfo.Environment["DEVFLOW_HIDE_WINDOW"] = "true";

        return Process.Start(startInfo);
    }

    private static void BuildHostProject(string hostProjectPath, string targetFramework, string workingDirectory)
    {
        var buildArguments = $"build \"{hostProjectPath}\" -c Debug -f {targetFramework}";
        if (!RunCommand("dotnet", buildArguments, workingDirectory, out var buildOutput, out var buildError))
        {
            throw new InvalidOperationException($"Failed to build Uno host project for {targetFramework}:\n{buildError}\n{buildOutput}");
        }
    }

    private static string GetHostExecutablePath(string hostProjectDirectory, string targetFramework)
    {
        var outputDir = Path.Combine(hostProjectDirectory, "bin", "Debug", targetFramework);
        var appName = "UnoDevFlowTestApp";
        var hostExecutable = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? Path.Combine(outputDir, appName + ".exe")
            : Path.Combine(outputDir, appName);

        if (File.Exists(hostExecutable))
            return hostExecutable;

        var dllPath = Path.Combine(outputDir, appName + ".dll");
        return dllPath;
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
}
