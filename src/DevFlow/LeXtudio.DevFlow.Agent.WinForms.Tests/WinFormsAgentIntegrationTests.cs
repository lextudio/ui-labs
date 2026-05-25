using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Xunit;

namespace LeXtudio.DevFlow.Agent.WinForms.Tests;

#pragma warning disable xUnit1051
public class WinFormsAgentIntegrationTests
{
    [Fact]
    public async Task StatusAndTree_Work()
    {
        var port = GetFreePort();
        await using var host = await StartHostAsync(port);
        using var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}") };

        var status = await PollStatusAsync(client, TimeSpan.FromSeconds(15));
        Assert.True(status.GetProperty("running").GetBoolean());
        Assert.Equal("LeXtudio.DevFlow.Agent", status.GetProperty("name").GetString());
        Assert.Equal("winforms", status.GetProperty("framework").GetString());
        var capabilities = status.GetProperty("capabilities");
        Assert.True(capabilities.GetProperty("screenshots").GetBoolean());
        Assert.True(capabilities.GetProperty("elementScreenshots").GetBoolean());
        Assert.True(capabilities.GetProperty("selectorScreenshots").GetBoolean());
        Assert.True(capabilities.GetProperty("tap").GetBoolean());
        Assert.True(capabilities.GetProperty("scroll").GetBoolean());
        Assert.True(capabilities.GetProperty("structuredErrors").GetBoolean());
        Assert.False(capabilities.GetProperty("appTheme").GetBoolean());
        Assert.True(capabilities.GetProperty("webview").GetBoolean());
        Assert.True(capabilities.GetProperty("webviewCdp").GetBoolean());
        Assert.True(capabilities.GetProperty("multiWindow").GetBoolean());

        using var tree = await client.GetAsync("/api/v1/ui/tree");
        tree.EnsureSuccessStatusCode();
        using var treeDoc = JsonDocument.Parse(await tree.Content.ReadAsStreamAsync());
        Assert.True(treeDoc.RootElement.GetProperty("elements").GetArrayLength() > 0);
    }

    [Fact]
    public async Task ElementLookup_ReturnsInputBox()
    {
        var port = GetFreePort();
        await using var host = await StartHostAsync(port);
        using var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}") };
        await PollStatusAsync(client, TimeSpan.FromSeconds(15));
        await WaitForElementAsync(client, "InputBox", TimeSpan.FromSeconds(10));

        using var response = await client.GetAsync("/api/v1/ui/element?id=InputBox");
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        Assert.Equal("InputBox", doc.RootElement.GetProperty("id").GetString());
        Assert.Equal("TextBox", doc.RootElement.GetProperty("type").GetString());
    }

    [Fact]
    public async Task Query_ReturnsMatchingElements()
    {
        var port = GetFreePort();
        await using var host = await StartHostAsync(port);
        using var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}") };
        await PollStatusAsync(client, TimeSpan.FromSeconds(15));

        using var query = await client.GetAsync("/api/v1/ui/elements?type=TextBox");
        query.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await query.Content.ReadAsStreamAsync());
        var elements = doc.RootElement.EnumerateArray().ToArray();
        Assert.Contains(elements, e => e.GetProperty("id").GetString() == "InputBox");
    }

    [Fact]
    public async Task TapFillClearFocus_Work()
    {
        var port = GetFreePort();
        await using var host = await StartHostAsync(port);
        using var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}") };
        await PollStatusAsync(client, TimeSpan.FromSeconds(15));
        await WaitForElementAsync(client, "ActionButton", TimeSpan.FromSeconds(10));
        await WaitForElementAsync(client, "InputBox", TimeSpan.FromSeconds(10));
        await WaitForElementAsync(client, "ResponseLabel", TimeSpan.FromSeconds(10));

        using var tapResponse = await client.PostAsync("/api/v1/ui/tap", Json("{" + "\"id\":\"ActionButton\"}"));
        tapResponse.EnsureSuccessStatusCode();
        using var tapDoc = JsonDocument.Parse(await tapResponse.Content.ReadAsStreamAsync());
        Assert.True(tapDoc.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains(tapDoc.RootElement.GetProperty("simulationMode").GetString(), new[] { "native", "semantic" });

        using var fillResponse = await client.PostAsync("/api/v1/ui/actions/fill", Json("{" + "\"elementId\":\"InputBox\",\"text\":\"hello\"}"));
        fillResponse.EnsureSuccessStatusCode();
        using var fillDoc = JsonDocument.Parse(await fillResponse.Content.ReadAsStreamAsync());
        Assert.True(fillDoc.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains(fillDoc.RootElement.GetProperty("simulationMode").GetString(), new[] { "native", "property-mutation" });

        using var focusResponse = await client.PostAsync("/api/v1/ui/actions/focus", Json("{" + "\"elementId\":\"InputBox\"}"));
        focusResponse.EnsureSuccessStatusCode();
        using var focusDoc = JsonDocument.Parse(await focusResponse.Content.ReadAsStreamAsync());
        Assert.True(focusDoc.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains(focusDoc.RootElement.GetProperty("simulationMode").GetString(), new[] { "native", "semantic" });

        using var clearResponse = await client.PostAsync("/api/v1/ui/actions/clear", Json("{" + "\"elementId\":\"InputBox\"}"));
        clearResponse.EnsureSuccessStatusCode();
        using var clearDoc = JsonDocument.Parse(await clearResponse.Content.ReadAsStreamAsync());
        Assert.True(clearDoc.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains(clearDoc.RootElement.GetProperty("simulationMode").GetString(), new[] { "native", "property-mutation" });

        using var input = await client.GetAsync("/api/v1/ui/element?id=InputBox");
        input.EnsureSuccessStatusCode();
        using var inputDoc = JsonDocument.Parse(await input.Content.ReadAsStreamAsync());
        Assert.Equal(string.Empty, inputDoc.RootElement.GetProperty("text").GetString());

        using var responseLabel = await client.GetAsync("/api/v1/ui/element?id=ResponseLabel");
        responseLabel.EnsureSuccessStatusCode();
        using var labelDoc = JsonDocument.Parse(await responseLabel.Content.ReadAsStreamAsync());
        Assert.Equal("Button clicked", labelDoc.RootElement.GetProperty("text").GetString());
    }

    [Fact]
    public async Task FillAndClear_UpdatesElementText()
    {
        var port = GetFreePort();
        await using var host = await StartHostAsync(port);
        using var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}") };
        await PollStatusAsync(client, TimeSpan.FromSeconds(15));
        await WaitForElementAsync(client, "InputBox", TimeSpan.FromSeconds(10));

        (await client.PostAsync("/api/v1/ui/actions/fill", Json("{\"elementId\":\"InputBox\",\"text\":\"hello\"}"))).EnsureSuccessStatusCode();
        using var filled = await client.GetAsync("/api/v1/ui/element?id=InputBox");
        filled.EnsureSuccessStatusCode();
        using var filledDoc = JsonDocument.Parse(await filled.Content.ReadAsStreamAsync());
        Assert.Equal("hello", filledDoc.RootElement.GetProperty("text").GetString());

        (await client.PostAsync("/api/v1/ui/actions/clear", Json("{\"elementId\":\"InputBox\"}"))).EnsureSuccessStatusCode();
        using var cleared = await client.GetAsync("/api/v1/ui/element?id=InputBox");
        cleared.EnsureSuccessStatusCode();
        using var clearedDoc = JsonDocument.Parse(await cleared.Content.ReadAsStreamAsync());
        Assert.Equal(string.Empty, clearedDoc.RootElement.GetProperty("text").GetString());
    }

    [Fact]
    public async Task Focus_ReturnsSuccess()
    {
        var port = GetFreePort();
        await using var host = await StartHostAsync(port);
        using var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}") };
        await PollStatusAsync(client, TimeSpan.FromSeconds(15));
        await WaitForElementAsync(client, "InputBox", TimeSpan.FromSeconds(10));

        using var response = await client.PostAsync("/api/v1/ui/actions/focus", Json("{\"elementId\":\"InputBox\"}"));
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        Assert.True(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.True(doc.RootElement.TryGetProperty("simulationMode", out _));
    }

    [Fact]
    public async Task Key_ReturnsSuccess()
    {
        var port = GetFreePort();
        await using var host = await StartHostAsync(port);
        using var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}") };
        await PollStatusAsync(client, TimeSpan.FromSeconds(15));
        await WaitForElementAsync(client, "InputBox", TimeSpan.FromSeconds(10));

        using var response = await client.PostAsync("/api/v1/ui/actions/key", Json("{\"elementId\":\"InputBox\",\"text\":\"A\"}"));
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        Assert.True(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains(doc.RootElement.GetProperty("simulationMode").GetString(), new[] { "native", "property-mutation" });
    }

    [Fact]
    public async Task ScreenshotScrollAndKey_Work()
    {
        var port = GetFreePort();
        await using var host = await StartHostAsync(port);
        using var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}") };
        await PollStatusAsync(client, TimeSpan.FromSeconds(15));
        await WaitForElementAsync(client, "ActionButton", TimeSpan.FromSeconds(10));
        await WaitForElementAsync(client, "InputBox", TimeSpan.FromSeconds(10));
        await WaitForElementAsync(client, "MainScrollPanel", TimeSpan.FromSeconds(10));

        using var fullShot = await client.GetAsync("/api/v1/ui/screenshot");
        fullShot.EnsureSuccessStatusCode();
        var fullBytes = await fullShot.Content.ReadAsByteArrayAsync();
        Assert.True(IsPng(fullBytes));

        using var elementShot = await client.GetAsync("/api/v1/ui/screenshot?id=ActionButton");
        elementShot.EnsureSuccessStatusCode();
        var elementBytes = await elementShot.Content.ReadAsByteArrayAsync();
        Assert.True(IsPng(elementBytes));

        using var selectorShot = await client.GetAsync("/api/v1/ui/screenshot?selector=%23ActionButton");
        selectorShot.EnsureSuccessStatusCode();
        var selectorBytes = await selectorShot.Content.ReadAsByteArrayAsync();
        Assert.True(IsPng(selectorBytes));

        (await client.PostAsync("/api/v1/ui/actions/scroll", Json("{" + "\"id\":\"MainScrollPanel\",\"deltaY\":120}"))).EnsureSuccessStatusCode();

        (await client.PostAsync("/api/v1/ui/actions/key", Json("{" + "\"elementId\":\"InputBox\",\"text\":\"A\"}"))).EnsureSuccessStatusCode();
        (await client.PostAsync("/api/v1/ui/actions/key", Json("{" + "\"elementId\":\"InputBox\",\"key\":\"backspace\"}"))).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task ElementScreenshot_ReturnsValidPng()
    {
        var port = GetFreePort();
        await using var host = await StartHostAsync(port);
        using var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}") };
        await PollStatusAsync(client, TimeSpan.FromSeconds(15));
        await WaitForElementAsync(client, "ActionButton", TimeSpan.FromSeconds(10));

        var bytes = await PollScreenshotAsync(client, "/api/v1/ui/screenshot?id=ActionButton", TimeSpan.FromSeconds(15));
        Assert.NotEmpty(bytes);
        Assert.True(IsPng(bytes));
    }

    [Fact]
    public async Task SelectorScreenshot_ReturnsValidPng()
    {
        var port = GetFreePort();
        await using var host = await StartHostAsync(port);
        using var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}") };
        await PollStatusAsync(client, TimeSpan.FromSeconds(15));
        await WaitForElementAsync(client, "ActionButton", TimeSpan.FromSeconds(10));

        var bytes = await PollScreenshotAsync(client, "/api/v1/ui/screenshot?selector=%23ActionButton", TimeSpan.FromSeconds(15));
        Assert.NotEmpty(bytes);
        Assert.True(IsPng(bytes));
    }

    [Fact]
    public async Task BatchActions_SucceedsForTapAndFill()
    {
        var port = GetFreePort();
        await using var host = await StartHostAsync(port);
        using var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}") };
        await PollStatusAsync(client, TimeSpan.FromSeconds(15));

        const string body = "{\"actions\":[{\"action\":\"tap\",\"elementId\":\"ActionButton\"},{\"action\":\"fill\",\"elementId\":\"InputBox\",\"text\":\"batch\"}]}";
        using var response = await client.PostAsync("/api/v1/ui/actions/batch", Json(body));
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        Assert.True(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(2, doc.RootElement.GetProperty("results").GetArrayLength());
    }

    [Fact]
    public async Task Theme_GetReturnsPayloadAndSetReturnsStructuredError()
    {
        var port = GetFreePort();
        await using var host = await StartHostAsync(port);
        using var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}") };
        await PollStatusAsync(client, TimeSpan.FromSeconds(15));

        using var get = await client.GetAsync("/api/v1/device/app/theme");
        get.EnsureSuccessStatusCode();
        using var getDoc = JsonDocument.Parse(await get.Content.ReadAsStreamAsync());
        Assert.Equal("system", getDoc.RootElement.GetProperty("theme").GetString());

        using var set = await client.PutAsync("/api/v1/device/app/theme", Json("{\"theme\":\"light\"}"));
        Assert.Equal(HttpStatusCode.BadRequest, set.StatusCode);
    }

    [Fact]
    public async Task InvokeApi_ListAndInvoke_Works()
    {
        var port = GetFreePort();
        await using var host = await StartHostAsync(port);
        using var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}") };
        await PollStatusAsync(client, TimeSpan.FromSeconds(15));

        using var list = await client.GetAsync("/api/v1/invoke/actions");
        list.EnsureSuccessStatusCode();
        using var listDoc = JsonDocument.Parse(await list.Content.ReadAsStreamAsync());
        var actions = listDoc.RootElement.GetProperty("actions").EnumerateArray().ToArray();
        Assert.Contains(actions, a => string.Equals(a.GetProperty("name").GetString(), "winforms.echo", StringComparison.OrdinalIgnoreCase));

        using var invoke = await client.PostAsync("/api/v1/invoke/actions/winforms.echo", Json("{\"args\":[\"hello\"]}"));
        invoke.EnsureSuccessStatusCode();
        using var invokeDoc = JsonDocument.Parse(await invoke.Content.ReadAsStreamAsync());
        Assert.True(invokeDoc.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("echo:hello", invokeDoc.RootElement.GetProperty("returnValue").GetString());
    }

    [Fact]
    public async Task WebViewEndpoints_Work()
    {
        var port = GetFreePort();
        await using var host = await StartHostAsync(port);
        using var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}") };
        await PollStatusAsync(client, TimeSpan.FromSeconds(15));
        await WaitForElementAsync(client, "WebViewHost", TimeSpan.FromSeconds(10));

        using var contexts = await client.GetAsync("/api/v1/webview/contexts");
        contexts.EnsureSuccessStatusCode();
        using var contextsDoc = JsonDocument.Parse(await contexts.Content.ReadAsStreamAsync());
        var contextList = contextsDoc.RootElement.GetProperty("contexts").EnumerateArray().ToArray();
        Assert.Contains(contextList, c => c.GetProperty("id").GetString() == "WebViewHost");

        var screenshotBytes = await PollScreenshotAsync(client, "/api/v1/webview/screenshot?context=WebViewHost", TimeSpan.FromSeconds(20));
        Assert.NotEmpty(screenshotBytes);
        Assert.True(IsPng(screenshotBytes));

        using var cdp = await client.PostAsync(
            "/api/v1/webview/cdp",
            Json("{\"context\":\"WebViewHost\",\"method\":\"Runtime.evaluate\",\"params\":{\"expression\":\"document.getElementById('title').textContent\"}}"));
        cdp.EnsureSuccessStatusCode();
        using var cdpDoc = JsonDocument.Parse(await cdp.Content.ReadAsStreamAsync());
        Assert.Contains("DevFlow WinForms WebView Test", cdpDoc.RootElement.GetProperty("result").GetProperty("value").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task QueryAndErrorEnvelope_Work()
    {
        var port = GetFreePort();
        await using var host = await StartHostAsync(port);
        using var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}") };
        await PollStatusAsync(client, TimeSpan.FromSeconds(15));

        using var query = await client.GetAsync("/api/v1/ui/elements?type=TextBox");
        query.EnsureSuccessStatusCode();

        using var bad = await client.GetAsync("/api/v1/ui/element");
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
    }

    private static bool IsPng(byte[] bytes)
    {
        byte[] h = [137, 80, 78, 71, 13, 10, 26, 10];
        return bytes.Length >= h.Length && bytes.Take(h.Length).SequenceEqual(h);
    }

    private static async Task<byte[]> PollScreenshotAsync(HttpClient client, string path, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var response = await client.GetAsync(path);
                if (response.IsSuccessStatusCode)
                {
                    var bytes = await response.Content.ReadAsByteArrayAsync();
                    if (bytes.Length > 0 && IsPng(bytes))
                        return bytes;
                }
            }
            catch
            {
            }

            await Task.Delay(300);
        }

        throw new InvalidOperationException($"Screenshot endpoint did not return a PNG in time: {path}");
    }

    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");
    private static async Task<JsonElement> PollStatusAsync(HttpClient client, TimeSpan timeout)
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
            catch { }
            await Task.Delay(250);
        }

        throw new InvalidOperationException("Agent status endpoint did not become available in time.");
    }

    private static int GetFreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static async Task WaitForElementAsync(HttpClient client, string elementId, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var response = await client.GetAsync($"/api/v1/ui/element?id={Uri.EscapeDataString(elementId)}");
                if (response.IsSuccessStatusCode)
                    return;
            }
            catch { }

            await Task.Delay(200);
        }

        throw new InvalidOperationException($"Element '{elementId}' was not available in time.");
    }

    private static async Task<DisposableProcessHost> StartHostAsync(int port)
    {
        var repoRoot = FindRepositoryRoot(Directory.GetCurrentDirectory());
        var hostProjectPath = Path.GetFullPath(Path.Combine(repoRoot, "src", "DevFlow", "WinFormsDevFlowTestApp", "WinFormsDevFlowTestApp.csproj"));
        var hostProjectDirectory = Path.GetDirectoryName(hostProjectPath)!;

        if (!RunCommand("dotnet", $"build \"{hostProjectPath}\" -c Debug", hostProjectDirectory, out var outp, out var err))
            throw new InvalidOperationException($"Failed to build WinForms host project:\n{err}\n{outp}");

        var exePath = Path.Combine(hostProjectDirectory, "bin", "Debug", "net8.0-windows", "WinFormsDevFlowTestApp.exe");
        var psi = new ProcessStartInfo(exePath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = hostProjectDirectory,
        };
        psi.Environment["DEVFLOW_AGENT_PORT"] = port.ToString();

        var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start host process.");
        await Task.Delay(300);
        return new DisposableProcessHost(process);
    }

    private static string FindRepositoryRoot(string startFolder)
    {
        var current = new DirectoryInfo(startFolder);
        while (current != null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "src", "DevFlow"))) return current.FullName;
            current = current.Parent;
        }
        throw new InvalidOperationException("Unable to locate repository root.");
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
        process.Start();
        output = process.StandardOutput.ReadToEnd();
        error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return process.ExitCode == 0;
    }

    private sealed class DisposableProcessHost(Process process) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            if (!process.HasExited)
            {
                process.Kill(true);
                process.WaitForExit(5000);
            }
            process.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
