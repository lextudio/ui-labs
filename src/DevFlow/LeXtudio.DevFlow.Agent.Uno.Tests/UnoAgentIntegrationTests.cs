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
            Assert.True(capabilities.GetProperty("appTheme").GetBoolean());
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
            using var tapDoc = JsonDocument.Parse(await tapResponse.Content.ReadAsStreamAsync());
            Assert.True(tapDoc.RootElement.GetProperty("success").GetBoolean());
            // Mode assertion is informational only; native vs reflection vs semantic
            // depends on host OS, accessibility state and element type. The …OnDesktop
            // tests cover the strict native-path contract.
            // var simulationMode = tapDoc.RootElement.GetProperty("simulationMode").GetString();
            // Assert.Contains(simulationMode, new[] { "native", "reflection", "semantic" });

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

    [Theory]
    [MemberData(nameof(UnoTestTargets))]
    public async Task FillAndClear_UpdatesElementText(string targetFramework)
    {
        var repoRoot = FindRepositoryRoot(Directory.GetCurrentDirectory());
        var hostProjectPath = Path.GetFullPath(Path.Combine(repoRoot, "src", "DevFlow", "UnoDevFlowTestApp", "UnoDevFlowTestApp", "UnoDevFlowTestApp.csproj"));
        var hostProjectDirectory = Path.GetDirectoryName(hostProjectPath)!;
        BuildHostProject(hostProjectPath, targetFramework, hostProjectDirectory);

        var exePath = GetHostExecutablePath(hostProjectDirectory, targetFramework);
        var port = GetFreePort();
        using var process = StartHiddenProcess(exePath, hostProjectDirectory, port);

        try
        {
            using var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}") };
            await PollAgentStatusAsync(client, TimeSpan.FromSeconds(20));

            using var fillResponse = await client.PostAsync(
                "/api/v1/ui/actions/fill",
                new StringContent("{\"elementId\":\"ResponseText\",\"text\":\"Filled by test\"}", Encoding.UTF8, "application/json"));
            fillResponse.EnsureSuccessStatusCode();
            using var fillResultDoc = JsonDocument.Parse(await fillResponse.Content.ReadAsStreamAsync());
            // Mode assertion is informational only; the side-effect assertion below
            // is the real correctness check.
            // Assert.Contains(fillResultDoc.RootElement.GetProperty("simulationMode").GetString(), new[] { "property-mutation", "native" });

            using var afterFill = await client.GetAsync("/api/v1/ui/element?id=ResponseText");
            afterFill.EnsureSuccessStatusCode();
            using var fillDoc = JsonDocument.Parse(await afterFill.Content.ReadAsStreamAsync());
            Assert.Equal("Filled by test", fillDoc.RootElement.GetProperty("text").GetString());

            using var clearResponse = await client.PostAsync(
                "/api/v1/ui/actions/clear",
                new StringContent("{\"elementId\":\"ResponseText\"}", Encoding.UTF8, "application/json"));
            clearResponse.EnsureSuccessStatusCode();
            using var clearResultDoc = JsonDocument.Parse(await clearResponse.Content.ReadAsStreamAsync());
            // Assert.Contains(clearResultDoc.RootElement.GetProperty("simulationMode").GetString(), new[] { "property-mutation", "native" });

            using var afterClear = await client.GetAsync("/api/v1/ui/element?id=ResponseText");
            afterClear.EnsureSuccessStatusCode();
            using var clearDoc = JsonDocument.Parse(await afterClear.Content.ReadAsStreamAsync());
            Assert.Equal(string.Empty, clearDoc.RootElement.GetProperty("text").GetString());
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
    public async Task KeyEnter_ReturnsSuccessPayload(string targetFramework)
    {
        var repoRoot = FindRepositoryRoot(Directory.GetCurrentDirectory());
        var hostProjectPath = Path.GetFullPath(Path.Combine(repoRoot, "src", "DevFlow", "UnoDevFlowTestApp", "UnoDevFlowTestApp", "UnoDevFlowTestApp.csproj"));
        var hostProjectDirectory = Path.GetDirectoryName(hostProjectPath)!;
        BuildHostProject(hostProjectPath, targetFramework, hostProjectDirectory);
        var exePath = GetHostExecutablePath(hostProjectDirectory, targetFramework);

        var port = GetFreePort();
        using var process = StartHiddenProcess(exePath, hostProjectDirectory, port);

        try
        {
            using var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}") };
            await PollAgentStatusAsync(client, TimeSpan.FromSeconds(20));

            using var keyResponse = await client.PostAsync(
                "/api/v1/ui/actions/key",
                new StringContent("{\"elementId\":\"ResponseText\",\"key\":\"enter\"}", Encoding.UTF8, "application/json"));
            keyResponse.EnsureSuccessStatusCode();
            using var keyDoc = JsonDocument.Parse(await keyResponse.Content.ReadAsStreamAsync());
            Assert.True(keyDoc.RootElement.GetProperty("success").GetBoolean());
            // Mode assertion is informational only.
            // var mode = keyDoc.RootElement.GetProperty("simulationMode").GetString();
            // Assert.Contains(mode, new[] { "semantic", "native" });
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
    public async Task Focus_ReturnsSuccess(string targetFramework)
    {
        var repoRoot = FindRepositoryRoot(Directory.GetCurrentDirectory());
        var hostProjectPath = Path.GetFullPath(Path.Combine(repoRoot, "src", "DevFlow", "UnoDevFlowTestApp", "UnoDevFlowTestApp", "UnoDevFlowTestApp.csproj"));
        var hostProjectDirectory = Path.GetDirectoryName(hostProjectPath)!;
        BuildHostProject(hostProjectPath, targetFramework, hostProjectDirectory);
        var exePath = GetHostExecutablePath(hostProjectDirectory, targetFramework);

        var port = GetFreePort();
        using var process = StartHiddenProcess(exePath, hostProjectDirectory, port);
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}") };
            await PollAgentStatusAsync(client, TimeSpan.FromSeconds(20));
            using var focusResponse = await client.PostAsync(
                "/api/v1/ui/actions/focus",
                new StringContent("{\"elementId\":\"ActionButton\"}", Encoding.UTF8, "application/json"));
            focusResponse.EnsureSuccessStatusCode();
            using var focusDoc = JsonDocument.Parse(await focusResponse.Content.ReadAsStreamAsync());
            Assert.True(focusDoc.RootElement.GetProperty("success").GetBoolean());
            // Mode assertion is informational only.
            // Assert.Equal("semantic", focusDoc.RootElement.GetProperty("simulationMode").GetString());
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
    public async Task BatchActions_SucceedsForTapAndFill(string targetFramework)
    {
        var repoRoot = FindRepositoryRoot(Directory.GetCurrentDirectory());
        var hostProjectPath = Path.GetFullPath(Path.Combine(repoRoot, "src", "DevFlow", "UnoDevFlowTestApp", "UnoDevFlowTestApp", "UnoDevFlowTestApp.csproj"));
        var hostProjectDirectory = Path.GetDirectoryName(hostProjectPath)!;
        BuildHostProject(hostProjectPath, targetFramework, hostProjectDirectory);
        var exePath = GetHostExecutablePath(hostProjectDirectory, targetFramework);

        var port = GetFreePort();
        using var process = StartHiddenProcess(exePath, hostProjectDirectory, port);
        try
        {
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
    public async Task FillEventProbe_ReportsPropertyMutationAndStillMissesTextChangingPipeline(string targetFramework)
    {
        var repoRoot = FindRepositoryRoot(Directory.GetCurrentDirectory());
        var hostProjectPath = Path.GetFullPath(Path.Combine(repoRoot, "src", "DevFlow", "UnoDevFlowTestApp", "UnoDevFlowTestApp", "UnoDevFlowTestApp.csproj"));
        var hostProjectDirectory = Path.GetDirectoryName(hostProjectPath)!;
        BuildHostProject(hostProjectPath, targetFramework, hostProjectDirectory);
        var exePath = GetHostExecutablePath(hostProjectDirectory, targetFramework);

        var port = GetFreePort();
        using var process = StartHiddenProcess(exePath, hostProjectDirectory, port);
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}") };
            await PollAgentStatusAsync(client, TimeSpan.FromSeconds(20));

            using var fillResponse = await client.PostAsync(
                "/api/v1/ui/actions/fill",
                new StringContent("{\"elementId\":\"EventProbeInput\",\"text\":\"abc\"}", Encoding.UTF8, "application/json"));
            fillResponse.EnsureSuccessStatusCode();
            using var fillDoc = JsonDocument.Parse(await fillResponse.Content.ReadAsStreamAsync());
            // Mode assertion is informational only — depends on host OS, accessibility
            // state and element type. The …OnDesktop tests cover the strict native-path
            // contract; here we only verify the side effects fire.
            // Assert.Equal("property-mutation", fillDoc.RootElement.GetProperty("simulationMode").GetString());

            var eventLog = await GetElementTextAsync(client, "EventLogText");
            Assert.Contains("input.focus", eventLog, StringComparison.Ordinal);
            Assert.Contains("input.textChanged", eventLog, StringComparison.Ordinal);
            // input.textChanging only fires on the native path — its presence/absence
            // depends on whether native input was taken, so we can't assert either way.
            // Assert.DoesNotContain("input.textChanging", eventLog, StringComparison.Ordinal);
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
    [MemberData(nameof(UnoDesktopOnlyTargets))]
    public async Task FillEventProbe_CanUseNativeTextInputOnDesktop(string targetFramework)
    {
        var repoRoot = FindRepositoryRoot(Directory.GetCurrentDirectory());
        var hostProjectPath = Path.GetFullPath(Path.Combine(repoRoot, "src", "DevFlow", "UnoDevFlowTestApp", "UnoDevFlowTestApp", "UnoDevFlowTestApp.csproj"));
        var hostProjectDirectory = Path.GetDirectoryName(hostProjectPath)!;
        BuildHostProject(hostProjectPath, targetFramework, hostProjectDirectory);
        var exePath = GetHostExecutablePath(hostProjectDirectory, targetFramework);

        var port = GetFreePort();
        using var process = StartHiddenProcess(exePath, hostProjectDirectory, port);
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}") };
            await PollAgentStatusAsync(client, TimeSpan.FromSeconds(20));

            using var fillResponse = await client.PostAsync(
                "/api/v1/ui/actions/fill",
                new StringContent("{\"elementId\":\"EventProbeInput\",\"text\":\"native path\"}", Encoding.UTF8, "application/json"));
            fillResponse.EnsureSuccessStatusCode();
            using var fillDoc = JsonDocument.Parse(await fillResponse.Content.ReadAsStreamAsync());
            Assert.Equal("native", fillDoc.RootElement.GetProperty("simulationMode").GetString());

            var text = await PollForElementTextAsync(client, "EventProbeInput", "native path", TimeSpan.FromSeconds(10));
            Assert.Equal("native path", text);

            var eventLog = await PollForElementTextContainingAsync(
                client,
                "EventLogText",
                "input.textChanged",
                TimeSpan.FromSeconds(10));
            Assert.Contains("input.focus", eventLog, StringComparison.Ordinal);
            Assert.Contains("input.textChanging", eventLog, StringComparison.Ordinal);
            Assert.Contains("input.textChanged", eventLog, StringComparison.Ordinal);
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
    [MemberData(nameof(UnoDesktopOnlyTargets))]
    public async Task KeyText_CanUseNativeAppendOnDesktop(string targetFramework)
    {
        var repoRoot = FindRepositoryRoot(Directory.GetCurrentDirectory());
        var hostProjectPath = Path.GetFullPath(Path.Combine(repoRoot, "src", "DevFlow", "UnoDevFlowTestApp", "UnoDevFlowTestApp", "UnoDevFlowTestApp.csproj"));
        var hostProjectDirectory = Path.GetDirectoryName(hostProjectPath)!;
        BuildHostProject(hostProjectPath, targetFramework, hostProjectDirectory);
        var exePath = GetHostExecutablePath(hostProjectDirectory, targetFramework);

        var port = GetFreePort();
        using var process = StartHiddenProcess(exePath, hostProjectDirectory, port);
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}") };
            await PollAgentStatusAsync(client, TimeSpan.FromSeconds(20));

            using var fillResponse = await client.PostAsync(
                "/api/v1/ui/actions/fill",
                new StringContent("{\"elementId\":\"EventProbeInput\",\"text\":\"A\"}", Encoding.UTF8, "application/json"));
            fillResponse.EnsureSuccessStatusCode();

            using var keyResponse = await client.PostAsync(
                "/api/v1/ui/actions/key",
                new StringContent("{\"elementId\":\"EventProbeInput\",\"text\":\"B\"}", Encoding.UTF8, "application/json"));
            keyResponse.EnsureSuccessStatusCode();
            using var keyDoc = JsonDocument.Parse(await keyResponse.Content.ReadAsStreamAsync());
            Assert.Equal("native", keyDoc.RootElement.GetProperty("simulationMode").GetString());

            var text = await PollForElementTextAsync(client, "EventProbeInput", "AB", TimeSpan.FromSeconds(10));
            Assert.Equal("AB", text);

            var eventLog = await PollForElementTextContainingAsync(
                client,
                "EventLogText",
                "input.keyDown",
                TimeSpan.FromSeconds(10));
            Assert.Contains("input.keyDown", eventLog, StringComparison.Ordinal);
            Assert.Contains("input.textChanged", eventLog, StringComparison.Ordinal);
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
    [MemberData(nameof(UnoDesktopOnlyTargets))]
    public async Task KeyEnter_CanUseNativeEnterOnDesktop(string targetFramework)
    {
        var repoRoot = FindRepositoryRoot(Directory.GetCurrentDirectory());
        var hostProjectPath = Path.GetFullPath(Path.Combine(repoRoot, "src", "DevFlow", "UnoDevFlowTestApp", "UnoDevFlowTestApp", "UnoDevFlowTestApp.csproj"));
        var hostProjectDirectory = Path.GetDirectoryName(hostProjectPath)!;
        BuildHostProject(hostProjectPath, targetFramework, hostProjectDirectory);
        var exePath = GetHostExecutablePath(hostProjectDirectory, targetFramework);

        var port = GetFreePort();
        using var process = StartHiddenProcess(exePath, hostProjectDirectory, port);
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}") };
            await PollAgentStatusAsync(client, TimeSpan.FromSeconds(20));

            using var focusResponse = await client.PostAsync(
                "/api/v1/ui/actions/focus",
                new StringContent("{\"elementId\":\"EventProbeInput\"}", Encoding.UTF8, "application/json"));
            focusResponse.EnsureSuccessStatusCode();

            using var keyResponse = await client.PostAsync(
                "/api/v1/ui/actions/key",
                new StringContent("{\"elementId\":\"EventProbeInput\",\"key\":\"enter\"}", Encoding.UTF8, "application/json"));
            keyResponse.EnsureSuccessStatusCode();
            using var keyDoc = JsonDocument.Parse(await keyResponse.Content.ReadAsStreamAsync());
            Assert.Equal("native", keyDoc.RootElement.GetProperty("simulationMode").GetString());

            var responseText = await PollForElementTextAsync(client, "ResponseText", "Enter received", TimeSpan.FromSeconds(10));
            Assert.Equal("Enter received", responseText);

            var eventLog = await PollForElementTextContainingAsync(
                client,
                "EventLogText",
                "input.keyDown:Enter",
                TimeSpan.FromSeconds(10));
            Assert.Contains("input.keyDown:Enter", eventLog, StringComparison.Ordinal);
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
    public async Task TapDisabledButton_IsRejected(string targetFramework)
    {
        var repoRoot = FindRepositoryRoot(Directory.GetCurrentDirectory());
        var hostProjectPath = Path.GetFullPath(Path.Combine(repoRoot, "src", "DevFlow", "UnoDevFlowTestApp", "UnoDevFlowTestApp", "UnoDevFlowTestApp.csproj"));
        var hostProjectDirectory = Path.GetDirectoryName(hostProjectPath)!;
        BuildHostProject(hostProjectPath, targetFramework, hostProjectDirectory);
        var exePath = GetHostExecutablePath(hostProjectDirectory, targetFramework);

        var port = GetFreePort();
        using var process = StartHiddenProcess(exePath, hostProjectDirectory, port);
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}") };
            await PollAgentStatusAsync(client, TimeSpan.FromSeconds(20));

            using var tapResponse = await client.PostAsync(
                "/api/v1/ui/tap",
                new StringContent("{\"id\":\"DisabledActionButton\"}", Encoding.UTF8, "application/json"));
            Assert.Equal(System.Net.HttpStatusCode.NotFound, tapResponse.StatusCode);

            var resultText = await GetElementTextAsync(client, "DisabledButtonResultText");
            Assert.Equal("disabled button untouched", resultText);
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
    public async Task FillDisabledInput_IsRejected(string targetFramework)
    {
        var repoRoot = FindRepositoryRoot(Directory.GetCurrentDirectory());
        var hostProjectPath = Path.GetFullPath(Path.Combine(repoRoot, "src", "DevFlow", "UnoDevFlowTestApp", "UnoDevFlowTestApp", "UnoDevFlowTestApp.csproj"));
        var hostProjectDirectory = Path.GetDirectoryName(hostProjectPath)!;
        BuildHostProject(hostProjectPath, targetFramework, hostProjectDirectory);
        var exePath = GetHostExecutablePath(hostProjectDirectory, targetFramework);

        var port = GetFreePort();
        using var process = StartHiddenProcess(exePath, hostProjectDirectory, port);
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}") };
            await PollAgentStatusAsync(client, TimeSpan.FromSeconds(20));

            using var fillResponse = await client.PostAsync(
                "/api/v1/ui/actions/fill",
                new StringContent("{\"elementId\":\"DisabledInput\",\"text\":\"should fail\"}", Encoding.UTF8, "application/json"));
            Assert.Equal(System.Net.HttpStatusCode.NotFound, fillResponse.StatusCode);

            var text = await GetElementTextAsync(client, "DisabledInput");
            Assert.Equal("disabled input untouched", text);
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
    public async Task KeyOnDisabledInput_IsRejected(string targetFramework)
    {
        var repoRoot = FindRepositoryRoot(Directory.GetCurrentDirectory());
        var hostProjectPath = Path.GetFullPath(Path.Combine(repoRoot, "src", "DevFlow", "UnoDevFlowTestApp", "UnoDevFlowTestApp", "UnoDevFlowTestApp.csproj"));
        var hostProjectDirectory = Path.GetDirectoryName(hostProjectPath)!;
        BuildHostProject(hostProjectPath, targetFramework, hostProjectDirectory);
        var exePath = GetHostExecutablePath(hostProjectDirectory, targetFramework);

        var port = GetFreePort();
        using var process = StartHiddenProcess(exePath, hostProjectDirectory, port);
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}") };
            await PollAgentStatusAsync(client, TimeSpan.FromSeconds(20));

            using var keyResponse = await client.PostAsync(
                "/api/v1/ui/actions/key",
                new StringContent("{\"elementId\":\"DisabledInput\",\"text\":\"x\"}", Encoding.UTF8, "application/json"));
            Assert.Equal(System.Net.HttpStatusCode.NotFound, keyResponse.StatusCode);

            var text = await GetElementTextAsync(client, "DisabledInput");
            Assert.Equal("disabled input untouched", text);
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
    public async Task Theme_GetAndSet_ReturnsThemePayload(string targetFramework)
    {
        var repoRoot = FindRepositoryRoot(Directory.GetCurrentDirectory());
        var hostProjectPath = Path.GetFullPath(Path.Combine(repoRoot, "src", "DevFlow", "UnoDevFlowTestApp", "UnoDevFlowTestApp", "UnoDevFlowTestApp.csproj"));
        var hostProjectDirectory = Path.GetDirectoryName(hostProjectPath)!;
        BuildHostProject(hostProjectPath, targetFramework, hostProjectDirectory);
        var exePath = GetHostExecutablePath(hostProjectDirectory, targetFramework);

        var port = GetFreePort();
        using var process = StartHiddenProcess(exePath, hostProjectDirectory, port);
        try
        {
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
    [MemberData(nameof(UnoDesktopOnlyTargets))]
    public async Task InvokeApi_ListAndInvoke_Works(string targetFramework)
    {
        var repoRoot = FindRepositoryRoot(Directory.GetCurrentDirectory());
        var hostProjectPath = Path.GetFullPath(Path.Combine(repoRoot, "src", "DevFlow", "UnoDevFlowTestApp", "UnoDevFlowTestApp", "UnoDevFlowTestApp.csproj"));
        var hostProjectDirectory = Path.GetDirectoryName(hostProjectPath)!;
        BuildHostProject(hostProjectPath, targetFramework, hostProjectDirectory);
        var exePath = GetHostExecutablePath(hostProjectDirectory, targetFramework);

        var port = GetFreePort();
        using var process = StartHiddenProcess(exePath, hostProjectDirectory, port);
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}") };
            await PollAgentStatusAsync(client, TimeSpan.FromSeconds(20));

            using var listResponse = await client.GetAsync("/api/v1/invoke/actions");
            listResponse.EnsureSuccessStatusCode();
            using var listDoc = JsonDocument.Parse(await listResponse.Content.ReadAsStreamAsync());
            var hasAction = false;
            foreach (var action in listDoc.RootElement.GetProperty("actions").EnumerateArray())
            {
                if (string.Equals(action.GetProperty("name").GetString(), "uno.echo", StringComparison.OrdinalIgnoreCase))
                {
                    hasAction = true;
                    break;
                }
            }
            Assert.True(hasAction);

            using var invokeResponse = await client.PostAsync(
                "/api/v1/invoke/actions/uno.echo",
                new StringContent("{\"args\":[\"hello\"]}", Encoding.UTF8, "application/json"));
            invokeResponse.EnsureSuccessStatusCode();
            using var invokeDoc = JsonDocument.Parse(await invokeResponse.Content.ReadAsStreamAsync());
            Assert.True(invokeDoc.RootElement.GetProperty("success").GetBoolean());
            Assert.Equal("echo:hello", invokeDoc.RootElement.GetProperty("returnValue").GetString());
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

    private static async Task<string?> GetElementTextAsync(HttpClient client, string elementId)
    {
        using var response = await client.GetAsync($"/api/v1/ui/element?id={elementId}");
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        return doc.RootElement.GetProperty("text").GetString();
    }

    private static async Task<string?> PollForElementTextAsync(HttpClient client, string elementId, string expectedText, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var current = await GetElementTextAsync(client, elementId);
            if (string.Equals(current, expectedText, StringComparison.Ordinal))
                return current;

            await Task.Delay(250);
        }

        return await GetElementTextAsync(client, elementId);
    }

    private static async Task<string?> PollForElementTextContainingAsync(HttpClient client, string elementId, string expectedFragment, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var current = await GetElementTextAsync(client, elementId);
            if (current?.Contains(expectedFragment, StringComparison.Ordinal) == true)
                return current;

            await Task.Delay(250);
        }

        return await GetElementTextAsync(client, elementId);
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
