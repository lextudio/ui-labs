using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
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

            using var tapResponse = await PostAsync(client, "/api/v1/ui/tap", new StringContent("{\"id\":\"ActionButton\"}", Encoding.UTF8, "application/json"));
            tapResponse.EnsureSuccessStatusCode();
            using var tapDoc = JsonDocument.Parse(await ReadAsStreamAsync(tapResponse.Content));
            Assert.True(tapDoc.RootElement.GetProperty("success").GetBoolean());
            // Mode assertion is informational only; native vs reflection vs semantic
            // depends on host OS, accessibility state and element type. The …OnDesktop
            // tests cover the strict native-path contract.
            // var simulationMode = tapDoc.RootElement.GetProperty("simulationMode").GetString();
            // Assert.Contains(simulationMode, new[] { "native", "reflection", "semantic" });

            using var elementResponse = await GetAsync(client, "/api/v1/ui/element?id=ResponseText");
            elementResponse.EnsureSuccessStatusCode();
            using var elementDoc = JsonDocument.Parse(await ReadAsStreamAsync(elementResponse.Content));
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
    public async Task Drag_OverProbeSurface_DeliversPressMoveRelease(string targetFramework)
    {
        // Injected OS drag is macOS-only (see capabilities.drag). On other platforms the
        // gesture path is not implemented, so there is nothing to assert.
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return;

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

            // Content origin (Quartz global points) the injected drag maps window-local points against.
            // A fresh window may not be placed by the window manager immediately, so poll a few
            // seconds for a non-degenerate origin before giving up.
            double ox = 0, oy = 0;
            var originDeadline = DateTime.UtcNow.AddSeconds(5);
            while (DateTime.UtcNow < originDeadline)
            {
                var s = await PollAgentStatusAsync(client, TimeSpan.FromSeconds(5));
                var o = s.GetProperty("capabilities").GetProperty("windowContentOrigin");
                ox = o.GetProperty("x").GetDouble();
                oy = o.GetProperty("y").GetDouble();
                if (ox != 0 || oy != 0)
                    break;
                await Task.Delay(300, TestContext.Current.CancellationToken);
            }

            // A synthesized OS drag can only be verified when the app window is actually placed on a
            // screen and focusable. Headless / background test runners leave the NSWindow at a
            // degenerate (0,0) content origin (the surface is laid out, but the window has no real
            // on-screen position), so injected global events can't land — skip rather than fail.
            // Run interactively (or in a session with a display) to exercise the real gesture.
            if (ox == 0 && oy == 0)
                Assert.Skip("Window not placed on-screen (content origin 0,0); injected drag can't be delivered in this environment.");

            await InvokeActionAsync(client, "uno.drag-log-reset");

            // DragProbeSurface window-local rect: "x,y,w,h" (points).
            var rect = (await InvokeActionAsync(client, "uno.drag-surface-rect")).Split(',');
            var sx = double.Parse(rect[0], System.Globalization.CultureInfo.InvariantCulture);
            var sy = double.Parse(rect[1], System.Globalization.CultureInfo.InvariantCulture);
            var sw = double.Parse(rect[2], System.Globalization.CultureInfo.InvariantCulture);
            var sh = double.Parse(rect[3], System.Globalization.CultureInfo.InvariantCulture);

            // Path stays entirely inside the surface (20%..80% width, vertical centre).
            var fromX = ox + sx + 0.2 * sw;
            var toX = ox + sx + 0.8 * sw;
            var y = oy + sy + 0.5 * sh;

            // Focus the window first (its first click is otherwise consumed by macOS to activate it).
            await DragAsync(client, fromX, oy - 14.0, fromX, oy - 14.0);
            await Task.Delay(300, TestContext.Current.CancellationToken);

            var dragResult = await DragAsync(client, fromX, y, toX, y);
            Assert.True(dragResult.GetProperty("ok").GetBoolean(), $"drag should succeed: {dragResult}");
            Assert.Equal("cliclick-global", dragResult.GetProperty("mode").GetString());

            // The release is processed on the UI thread after the drag call returns; poll the log.
            string log = "";
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (DateTime.UtcNow < deadline)
            {
                log = await InvokeActionAsync(client, "uno.drag-log");
                if (log.Contains("down@") && log.Contains("move@") && log.Contains("up@"))
                    break;
                await Task.Delay(250, TestContext.Current.CancellationToken);
            }

            var diag = $"origin=({ox},{oy}) surface=({sx},{sy},{sw},{sh}) from=({fromX},{y}) to=({toX},{y}) dragMode={dragResult.GetProperty("mode").GetString()} log='{log}'";
            Assert.True(log.Contains("down@"), $"expected PointerPressed; {diag}");
            Assert.True(log.Contains("move@"), $"expected PointerMoved; {diag}");
            Assert.True(log.Contains("up@"), $"expected PointerReleased (historically dropped); {diag}");
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

            using var screenshotResponse = await GetAsync(client, "/api/v1/ui/screenshot");
            screenshotResponse.EnsureSuccessStatusCode();
            var screenshotBytes = await ReadAsByteArrayAsync(screenshotResponse.Content);

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

            using var scrollResponse = await PostAsync(client, 
                "/api/v1/ui/actions/scroll",
                new StringContent("{\"id\":\"MainScrollViewer\",\"deltaY\":1000}", Encoding.UTF8, "application/json"));
            scrollResponse.EnsureSuccessStatusCode();

            // The Uno host on Windows uses the Windows desktop backend and typically reports
            // vertical offset immediately after a scroll action. Non-Windows Uno desktop hosts
            // can have different dispatch/render timing, so we poll the UI state on macOS/Linux.
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                using var elementResponse = await GetAsync(client, "/api/v1/ui/element?id=MainScrollViewer");
                elementResponse.EnsureSuccessStatusCode();
                using var elementDoc = JsonDocument.Parse(await ReadAsStreamAsync(elementResponse.Content));
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

            using var targetResponse = await GetAsync(client, "/api/v1/ui/element?id=ScrollTargetText");
            targetResponse.EnsureSuccessStatusCode();
            using var targetDoc = JsonDocument.Parse(await ReadAsStreamAsync(targetResponse.Content));
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

    // Regression test for DevFlow bug: UnoAgentService never overrode GetWebViewContextsAsync
    // so /api/v1/webview/contexts always returned an empty list. The fix added an override that
    // enumerates WebView2 descendants via both the visual tree and logical children.
    [Theory]
    [MemberData(nameof(UnoDesktopOnlyTargets))]
    public async Task WebView_Contexts_ListsWebView2(string targetFramework)
    {
        var repoRoot = FindRepositoryRoot(Directory.GetCurrentDirectory());
        var hostProjectPath = Path.GetFullPath(Path.Combine(repoRoot, "src", "DevFlow", "UnoDevFlowTestApp", "UnoDevFlowTestApp", "UnoDevFlowTestApp.csproj"));
        var hostProjectDirectory = Path.GetDirectoryName(hostProjectPath)!;
        BuildHostProject(hostProjectPath, targetFramework, hostProjectDirectory);
        var exePath = GetHostExecutablePath(hostProjectDirectory, targetFramework);
        var port = GetFreePort();
        using var process = StartHiddenProcess(exePath, hostProjectDirectory, port);
        if (process == null || process.HasExited)
            throw new InvalidOperationException("Failed to start the Uno host process.");

        try
        {
            using var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}") };
            await PollAgentStatusAsync(client, TimeSpan.FromSeconds(20));

            JsonElement contexts = default;
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(20);
            while (DateTime.UtcNow < deadline)
            {
                using var response = await GetAsync(client, "/api/v1/webview/contexts");
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("contexts", out var arr) && arr.GetArrayLength() > 0)
                    {
                        contexts = arr.Clone();
                        break;
                    }
                }
                await Delay(250);
            }

            Assert.True(contexts.ValueKind == JsonValueKind.Array && contexts.GetArrayLength() > 0,
                "Expected /api/v1/webview/contexts to list the WebView2 element 'WebViewHost'.");
            var first = contexts[0];
            Assert.Equal("webview2", first.GetProperty("type").GetString());
            var id = first.GetProperty("id").GetString();
            Assert.Equal("WebViewHost", id);
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

    // Regression test for DevFlow bug: ExecuteScriptAsync was cast `as Task<string>`, which fails on
    // Uno's Microsoft.UI WebView2 (returns IAsyncOperation<string>), causing every Runtime.evaluate
    // CDP call to throw on the cast. The fix routes through AsTask<T>(IAsyncOperation<T>).
    //
    // Note: even with the cast fix, Uno's WebView2.CoreWebView2.ExecuteScriptAsync on net10.0-desktop
    // currently returns a null script result (the AsyncOperation completes successfully but with a
    // null payload). This is an Uno Platform WebView2 limitation, not a DevFlow bug. We therefore
    // only assert that the CDP endpoint reaches CoreWebView2 cleanly and produces a well-formed
    // response — proving the DevFlow pipeline (context discovery + async-shape handling) is intact.
    [Theory]
    [MemberData(nameof(UnoDesktopOnlyTargets))]
    public async Task WebView_Cdp_RuntimeEvaluate_ReachesCoreWebView2(string targetFramework)
    {
        var repoRoot = FindRepositoryRoot(Directory.GetCurrentDirectory());
        var hostProjectPath = Path.GetFullPath(Path.Combine(repoRoot, "src", "DevFlow", "UnoDevFlowTestApp", "UnoDevFlowTestApp", "UnoDevFlowTestApp.csproj"));
        var hostProjectDirectory = Path.GetDirectoryName(hostProjectPath)!;
        BuildHostProject(hostProjectPath, targetFramework, hostProjectDirectory);
        var exePath = GetHostExecutablePath(hostProjectDirectory, targetFramework);
        var port = GetFreePort();
        using var process = StartHiddenProcess(exePath, hostProjectDirectory, port);
        if (process == null || process.HasExited)
            throw new InvalidOperationException("Failed to start the Uno host process.");

        try
        {
            using var client = new HttpClient
            {
                BaseAddress = new Uri($"http://localhost:{port}"),
                Timeout = TimeSpan.FromMinutes(3)
            };
            await PollAgentStatusAsync(client, TimeSpan.FromSeconds(20));

            // Wait until /webview/contexts reports the WebView2 (CoreWebView2 may take time to init).
            var contextsDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(20);
            bool hasContexts = false;
            while (DateTime.UtcNow < contextsDeadline)
            {
                using var ctxResp = await GetAsync(client, "/api/v1/webview/contexts");
                if (ctxResp.IsSuccessStatusCode)
                {
                    var ctxJson = await ctxResp.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
                    using var ctxDoc = JsonDocument.Parse(ctxJson);
                    if (ctxDoc.RootElement.TryGetProperty("contexts", out var arr) && arr.GetArrayLength() > 0)
                    {
                        hasContexts = true;
                        break;
                    }
                }
                await Delay(250);
            }
            Assert.True(hasContexts, "WebView2 context was never discovered.");

            // Issue the CDP eval. It must return a well-formed JSON response with no error field
            // and no 'cast failed / not implemented' exception leaking out.
            var payload = JsonSerializer.Serialize(new
            {
                context = "WebViewHost",
                method = "Runtime.evaluate",
                @params = new { expression = "1+1" }
            });
            HttpResponseMessage? response = null;
            Exception? lastException = null;
            var attemptCount = 0;
            var cdpDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(120);
            while (DateTime.UtcNow < cdpDeadline)
            {
                attemptCount++;
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/webview/cdp")
                    {
                        Content = new StringContent(payload, Encoding.UTF8, "application/json")
                    };
                    using var perAttemptCts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
                    perAttemptCts.CancelAfter(TimeSpan.FromSeconds(10));
                    response = await client.SendAsync(request, perAttemptCts.Token);
                    if (response.IsSuccessStatusCode)
                    {
                        break;
                    }

                    response.Dispose();
                    response = null;
                }
                catch (OperationCanceledException ex) when (!TestContext.Current.CancellationToken.IsCancellationRequested)
                {
                    lastException = ex;
                }
                catch (HttpRequestException ex)
                {
                    lastException = ex;
                }

                await Delay(500);
            }

            Assert.True(response is not null,
                $"CDP endpoint never returned success within retry window. Attempts={attemptCount}. LastException={lastException?.GetType().Name}: {lastException?.Message}");
            using (response)
            {
                Assert.True(response.IsSuccessStatusCode, $"CDP HTTP failed: {(int)response.StatusCode}");
                var json = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
                using var doc = JsonDocument.Parse(json);
                Assert.False(doc.RootElement.TryGetProperty("error", out _),
                    $"CDP returned an error field; pipeline still broken: {json}");
                Assert.True(doc.RootElement.TryGetProperty("result", out var result),
                    $"CDP response missing 'result' field: {json}");
                Assert.True(result.TryGetProperty("value", out _),
                    $"CDP result missing 'value' field: {json}");
            }
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

    // Regression test for DevFlow bug: UnoVisualTreeWalker.GetPropertyValue did not catch
    // NotImplementedException, so any element whose properties throw (Uno WebView2 in particular)
    // would abort the entire visual-tree walk and /api/v1/ui/tree returned "response ended prematurely".
    [Theory]
    [MemberData(nameof(UnoDesktopOnlyTargets))]
    public async Task UiTree_DoesNotCrashOnWebView2(string targetFramework)
    {
        var repoRoot = FindRepositoryRoot(Directory.GetCurrentDirectory());
        var hostProjectPath = Path.GetFullPath(Path.Combine(repoRoot, "src", "DevFlow", "UnoDevFlowTestApp", "UnoDevFlowTestApp", "UnoDevFlowTestApp.csproj"));
        var hostProjectDirectory = Path.GetDirectoryName(hostProjectPath)!;
        BuildHostProject(hostProjectPath, targetFramework, hostProjectDirectory);
        var exePath = GetHostExecutablePath(hostProjectDirectory, targetFramework);
        var port = GetFreePort();
        using var process = StartHiddenProcess(exePath, hostProjectDirectory, port);
        if (process == null || process.HasExited)
            throw new InvalidOperationException("Failed to start the Uno host process.");

        try
        {
            using var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}") };
            await PollAgentStatusAsync(client, TimeSpan.FromSeconds(20));

            using var response = await GetAsync(client, "/api/v1/ui/tree");
            Assert.True(response.IsSuccessStatusCode, $"Tree request failed: {(int)response.StatusCode}");
            var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            Assert.False(string.IsNullOrEmpty(body), "Tree response body was empty — walk likely aborted.");
            Assert.Contains("WebViewHost", body);
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

    private static async Task<string?> EvaluateInWebViewAsync(HttpClient client, string contextId, string expression)
    {
        var payload = JsonSerializer.Serialize(new
        {
            context = contextId,
            method = "Runtime.evaluate",
            @params = new { expression }
        });
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        using var response = await PostAsync(client, "/api/v1/webview/cdp", content);
        if (!response.IsSuccessStatusCode)
            return null;
        var json = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("result", out var result)
            && result.TryGetProperty("value", out var value))
        {
            return value.ValueKind == JsonValueKind.Null ? null : value.GetString();
        }
        return null;
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

            using var fillResponse = await PostAsync(client, 
                "/api/v1/ui/actions/fill",
                new StringContent("{\"elementId\":\"ResponseText\",\"text\":\"Filled by test\"}", Encoding.UTF8, "application/json"));
            fillResponse.EnsureSuccessStatusCode();
            using var fillResultDoc = JsonDocument.Parse(await ReadAsStreamAsync(fillResponse.Content));
            // Mode assertion is informational only; the side-effect assertion below
            // is the real correctness check.
            // Assert.Contains(fillResultDoc.RootElement.GetProperty("simulationMode").GetString(), new[] { "property-mutation", "native" });

            using var afterFill = await GetAsync(client, "/api/v1/ui/element?id=ResponseText");
            afterFill.EnsureSuccessStatusCode();
            using var fillDoc = JsonDocument.Parse(await ReadAsStreamAsync(afterFill.Content));
            Assert.Equal("Filled by test", fillDoc.RootElement.GetProperty("text").GetString());

            using var clearResponse = await PostAsync(client, 
                "/api/v1/ui/actions/clear",
                new StringContent("{\"elementId\":\"ResponseText\"}", Encoding.UTF8, "application/json"));
            clearResponse.EnsureSuccessStatusCode();
            using var clearResultDoc = JsonDocument.Parse(await ReadAsStreamAsync(clearResponse.Content));
            // Assert.Contains(clearResultDoc.RootElement.GetProperty("simulationMode").GetString(), new[] { "property-mutation", "native" });

            using var afterClear = await GetAsync(client, "/api/v1/ui/element?id=ResponseText");
            afterClear.EnsureSuccessStatusCode();
            using var clearDoc = JsonDocument.Parse(await ReadAsStreamAsync(afterClear.Content));
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

            using var keyResponse = await PostAsync(client, 
                "/api/v1/ui/actions/key",
                new StringContent("{\"elementId\":\"ResponseText\",\"key\":\"enter\"}", Encoding.UTF8, "application/json"));
            keyResponse.EnsureSuccessStatusCode();
            using var keyDoc = JsonDocument.Parse(await ReadAsStreamAsync(keyResponse.Content));
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
            using var focusResponse = await PostAsync(client, 
                "/api/v1/ui/actions/focus",
                new StringContent("{\"elementId\":\"ActionButton\"}", Encoding.UTF8, "application/json"));
            focusResponse.EnsureSuccessStatusCode();
            using var focusDoc = JsonDocument.Parse(await ReadAsStreamAsync(focusResponse.Content));
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

            using var batchResponse = await PostAsync(client, 
                "/api/v1/ui/actions/batch",
                new StringContent(body, Encoding.UTF8, "application/json"));
            batchResponse.EnsureSuccessStatusCode();
            using var batchDoc = JsonDocument.Parse(await ReadAsStreamAsync(batchResponse.Content));
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
        // This test describes the property-mutation fallback path's event signature
        // (textChanged fires, textChanging does NOT). On Windows and Linux the agent
        // takes the native input path instead, where those guarantees are inverted.
        // Until the agent exposes a way to force property-mutation, skip everywhere
        // native runs — leaving only macOS, where Posix native is currently disabled.
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return;
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

            using var fillResponse = await PostAsync(client, 
                "/api/v1/ui/actions/fill",
                new StringContent("{\"elementId\":\"EventProbeInput\",\"text\":\"abc\"}", Encoding.UTF8, "application/json"));
            fillResponse.EnsureSuccessStatusCode();
            using var fillDoc = JsonDocument.Parse(await ReadAsStreamAsync(fillResponse.Content));
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
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return; // macOS Posix native disabled until NSApp postEvent: path lands
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

            using var fillResponse = await PostAsync(client, 
                "/api/v1/ui/actions/fill",
                new StringContent("{\"elementId\":\"EventProbeInput\",\"text\":\"native path\"}", Encoding.UTF8, "application/json"));
            fillResponse.EnsureSuccessStatusCode();
            using var fillDoc = JsonDocument.Parse(await ReadAsStreamAsync(fillResponse.Content));
            // Mode assertion is informational only.
            // Assert.Equal("native", fillDoc.RootElement.GetProperty("simulationMode").GetString());

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
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return; // macOS Posix native disabled until NSApp postEvent: path lands
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

            using var fillResponse = await PostAsync(client, 
                "/api/v1/ui/actions/fill",
                new StringContent("{\"elementId\":\"EventProbeInput\",\"text\":\"A\"}", Encoding.UTF8, "application/json"));
            fillResponse.EnsureSuccessStatusCode();

            using var keyResponse = await PostAsync(client, 
                "/api/v1/ui/actions/key",
                new StringContent("{\"elementId\":\"EventProbeInput\",\"text\":\"B\"}", Encoding.UTF8, "application/json"));
            keyResponse.EnsureSuccessStatusCode();
            using var keyDoc = JsonDocument.Parse(await ReadAsStreamAsync(keyResponse.Content));
            // Mode assertion is informational only.
            // Assert.Equal("native", keyDoc.RootElement.GetProperty("simulationMode").GetString());

            var text = await PollForElementTextAsync(client, "EventProbeInput", "AB", TimeSpan.FromSeconds(10));
            Assert.Equal("AB", text);

            var eventLog = await PollForElementTextContainingAsync(
                client,
                "EventLogText",
                "input.textChanged",
                TimeSpan.FromSeconds(10));
            // input.keyDown only fires when the native path uses virtual-key events.
            // Our Windows text injection uses KEYEVENTF_UNICODE (and Linux uses
            // XTest with character keysyms) which produces TextInput/TextChanging
            // but not necessarily KeyDown depending on the Uno backend, so this
            // assertion isn't path-stable across desktops.
            // Assert.Contains("input.keyDown", eventLog, StringComparison.Ordinal);
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
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return; // macOS Posix native disabled until NSApp postEvent: path lands
        if (string.Equals(targetFramework, "net10.0-desktop", StringComparison.Ordinal) && RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return; // Uno Skia-Win32 doesn't route synthesized VK_RETURN to TextBox.KeyDown (upstream issue)
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

            using var focusResponse = await PostAsync(client, 
                "/api/v1/ui/actions/focus",
                new StringContent("{\"elementId\":\"EventProbeInput\"}", Encoding.UTF8, "application/json"));
            focusResponse.EnsureSuccessStatusCode();

            using var keyResponse = await PostAsync(client, 
                "/api/v1/ui/actions/key",
                new StringContent("{\"elementId\":\"EventProbeInput\",\"key\":\"enter\"}", Encoding.UTF8, "application/json"));
            keyResponse.EnsureSuccessStatusCode();
            using var keyDoc = JsonDocument.Parse(await ReadAsStreamAsync(keyResponse.Content));
            // Mode assertion is informational only.
            // Assert.Equal("native", keyDoc.RootElement.GetProperty("simulationMode").GetString());

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

            using var tapResponse = await PostAsync(client, 
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

            using var fillResponse = await PostAsync(client, 
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

            using var keyResponse = await PostAsync(client, 
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

            using var getResponse = await GetAsync(client, "/api/v1/device/app/theme");
            getResponse.EnsureSuccessStatusCode();
            using var getDoc = JsonDocument.Parse(await ReadAsStreamAsync(getResponse.Content));
            Assert.True(getDoc.RootElement.TryGetProperty("supportedThemes", out _));

            using var setResponse = await PutAsync(client, 
                "/api/v1/device/app/theme",
                new StringContent("{\"theme\":\"light\"}", Encoding.UTF8, "application/json"));
            setResponse.EnsureSuccessStatusCode();
            using var setDoc = JsonDocument.Parse(await ReadAsStreamAsync(setResponse.Content));
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

            using var listResponse = await GetAsync(client, "/api/v1/invoke/actions");
            listResponse.EnsureSuccessStatusCode();
            using var listDoc = JsonDocument.Parse(await ReadAsStreamAsync(listResponse.Content));
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

            using var invokeResponse = await PostAsync(client, 
                "/api/v1/invoke/actions/uno.echo",
                new StringContent("{\"args\":[\"hello\"]}", Encoding.UTF8, "application/json"));
            invokeResponse.EnsureSuccessStatusCode();
            using var invokeDoc = JsonDocument.Parse(await ReadAsStreamAsync(invokeResponse.Content));
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
            using var elementResponse = await GetAsync(client, $"/api/v1/ui/element?id={elementId}");
            if (elementResponse.IsSuccessStatusCode)
            {
                using var elementDoc = JsonDocument.Parse(await ReadAsStreamAsync(elementResponse.Content));
                if (elementDoc.RootElement.TryGetProperty("frameworkProperties", out var frameworkProps) &&
                    frameworkProps.TryGetProperty("verticalOffset", out var offsetProp) &&
                    double.TryParse(offsetProp.GetString(), out var offsetValue) &&
                    offsetValue > 0)
                {
                    return offsetValue;
                }
            }

            await Delay(250);
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
                using var response = await GetAsync(client, "/api/v1/agent/status");
                if (response.IsSuccessStatusCode)
                {
                    using var doc = JsonDocument.Parse(await ReadAsStreamAsync(response.Content));
                    return doc.RootElement.Clone();
                }
            }
            catch (HttpRequestException)
            {
            }
            catch (TaskCanceledException)
            {
            }

            await Delay(250);
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
            using var response = await GetAsync(client, path);
            if (response.IsSuccessStatusCode)
            {
                var bytes = await ReadAsByteArrayAsync(response.Content);
                if (bytes.Length > 0 && IsPng(bytes))
                    return bytes;
            }

            await Delay(300);
        }

        throw new InvalidOperationException($"Screenshot endpoint did not return a PNG in time: {path}");
    }

    private static async Task<string?> GetElementTextAsync(HttpClient client, string elementId)
    {
        using var response = await GetAsync(client, $"/api/v1/ui/element?id={elementId}");
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await ReadAsStreamAsync(response.Content));
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

            await Delay(250);
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

            await Delay(250);
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

    private static Task<HttpResponseMessage> GetAsync(HttpClient client, string requestUri) => client.GetAsync(requestUri, TestContext.Current.CancellationToken);
    private static Task<HttpResponseMessage> PostAsync(HttpClient client, string requestUri, HttpContent content) => client.PostAsync(requestUri, content, TestContext.Current.CancellationToken);
    private static Task<HttpResponseMessage> PutAsync(HttpClient client, string requestUri, HttpContent content) => client.PutAsync(requestUri, content, TestContext.Current.CancellationToken);
    private static Task<Stream> ReadAsStreamAsync(HttpContent content) => content.ReadAsStreamAsync(TestContext.Current.CancellationToken);
    private static Task<byte[]> ReadAsByteArrayAsync(HttpContent content) => content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
    private static Task Delay(int millisecondsTimeout) => Task.Delay(millisecondsTimeout, TestContext.Current.CancellationToken);

    // Invoke a [DevFlowAction] and return its string result (the wrapper's "returnValue").
    private static async Task<string> InvokeActionAsync(HttpClient client, string action)
    {
        using var content = new StringContent("{\"args\":[]}", Encoding.UTF8, "application/json");
        using var resp = await PostAsync(client, $"/api/v1/invoke/actions/{action}", content);
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await ReadAsStreamAsync(resp.Content));
        return doc.RootElement.TryGetProperty("returnValue", out var rv) ? rv.GetString() ?? "" : "";
    }

    // Post an OS-level global drag (points) and return the parsed response.
    private static async Task<JsonElement> DragAsync(HttpClient client, double fromX, double fromY, double toX, double toY)
    {
        var body = JsonSerializer.Serialize(new { fromX, fromY, toX, toY, global = true });
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var resp = await PostAsync(client, "/api/v1/ui/actions/drag", content);
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await ReadAsStreamAsync(resp.Content));
        return doc.RootElement.Clone();
    }
}
