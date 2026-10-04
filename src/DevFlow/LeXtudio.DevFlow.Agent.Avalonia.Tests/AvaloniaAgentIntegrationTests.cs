using System.Text;
using System.Text.Json;
using LeXtudio.DevFlow.Agent.Core;
using Xunit;

namespace LeXtudio.DevFlow.Agent.Avalonia.Tests;

/// <summary>
/// Drives AvaloniaDevFlowTestApp through the agent's HTTP API. The app is shared by the tests of this
/// class, so every test sets up the state it checks instead of relying on the app's initial state.
/// </summary>
public class AvaloniaAgentIntegrationTests : IClassFixture<AvaloniaAgentHost>
{
    private readonly AvaloniaAgentHost _host;

    public AvaloniaAgentIntegrationTests(AvaloniaAgentHost host)
    {
        _host = host;
    }

    private HttpClient Client => _host.Client;

    [Fact]
    public async Task AgentStatus_ReportsAvalonia()
    {
        using var doc = await GetJsonAsync("/api/v1/agent/status");
        var status = doc.RootElement;

        Assert.True(status.GetProperty("running").GetBoolean());
        Assert.Equal("LeXtudio.DevFlow.Agent", status.GetProperty("name").GetString());
        Assert.Equal("avalonia", status.GetProperty("framework").GetString());
        Assert.Equal("AvaloniaDevFlowTestApp", status.GetProperty("application").GetString());

        var capabilities = status.GetProperty("capabilities");
        Assert.True(capabilities.GetProperty("appTheme").GetBoolean());
        Assert.True(capabilities.GetProperty("drag").GetBoolean());
        Assert.True(capabilities.GetProperty("multiWindow").GetBoolean());
    }

    [Fact]
    public async Task Tree_ContainsWindowAndNamedElements()
    {
        using var doc = await GetJsonAsync("/api/v1/ui/tree");
        var roots = Elements(doc.RootElement).ToList();

        var mainWindow = Assert.Single(roots, r => r.GetProperty("text").GetString() == "Avalonia DevFlow Test");
        Assert.Equal("MainWindow", mainWindow.GetProperty("type").GetString());
        Assert.Equal("avalonia", mainWindow.GetProperty("framework").GetString());

        var button = Flatten(mainWindow).Single(e => e.GetProperty("id").GetString() == "ActionButton");
        Assert.Equal("Button", button.GetProperty("type").GetString());
        Assert.Equal("Press me", button.GetProperty("text").GetString());
        var bounds = button.GetProperty("bounds");
        Assert.True(bounds.GetProperty("width").GetDouble() > 0);
        Assert.True(bounds.GetProperty("height").GetDouble() > 0);
    }

    [Fact]
    public async Task QueryElements_FiltersByType()
    {
        using var doc = await GetJsonAsync("/api/v1/ui/elements?type=Button");
        var ids = Elements(doc.RootElement).Select(e => e.GetProperty("id").GetString()).ToList();

        Assert.Contains("ActionButton", ids);
        Assert.Contains("OpenWindowButton", ids);
        Assert.All(Elements(doc.RootElement), e => Assert.Equal("Button", e.GetProperty("type").GetString()));
    }

    [Fact]
    public async Task TapButton_RunsItsClickHandler()
    {
        await InvokeActionAsync("avalonia.set-status", "before tap");

        using var tap = await PostJsonAsync("/api/v1/ui/actions/tap", "{\"elementId\":\"ActionButton\"}");
        Assert.True(tap.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains(tap.RootElement.GetProperty("simulationMode").GetString(), new[] { "native", "semantic" });

        Assert.Equal("Button pressed.", await GetTextAsync("ResponseText"));
    }

    [Fact]
    public async Task FillAndClear_UpdateTextBox()
    {
        using var fill = await PostJsonAsync("/api/v1/ui/actions/fill", "{\"elementId\":\"InputBox\",\"text\":\"Filled by test\"}");
        Assert.True(fill.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("Filled by test", await GetTextAsync("InputBox"));

        using var clear = await PostJsonAsync("/api/v1/ui/actions/clear", "{\"elementId\":\"InputBox\"}");
        Assert.True(clear.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(string.Empty, await GetTextAsync("InputBox"));
    }

    [Fact]
    public async Task KeyEnter_RaisesKeyDownOnTextBox()
    {
        using var _ = await PostJsonAsync("/api/v1/ui/actions/fill", "{\"elementId\":\"InputBox\",\"text\":\"enter test\"}");

        using var key = await PostJsonAsync("/api/v1/ui/actions/key", "{\"elementId\":\"InputBox\",\"key\":\"Enter\"}");
        Assert.True(key.RootElement.GetProperty("success").GetBoolean());

        Assert.Equal("Enter pressed: enter test", await GetTextAsync("ResponseText"));
    }

    [Fact]
    public async Task Focus_FocusesTextBox()
    {
        using var focus = await PostJsonAsync("/api/v1/ui/actions/focus", "{\"elementId\":\"InputBox\"}");
        Assert.True(focus.RootElement.GetProperty("success").GetBoolean());

        using var element = await GetJsonAsync("/api/v1/ui/elements/InputBox");
        Assert.True(element.RootElement.GetProperty("isFocused").GetBoolean());
    }

    [Fact]
    public async Task BatchActions_RunTapAndFill()
    {
        const string body = """
                            {
                              "actions": [
                                { "action": "tap", "elementId": "ActionButton" },
                                { "action": "fill", "elementId": "InputBox", "text": "Batch updated" }
                              ]
                            }
                            """;

        using var batch = await PostJsonAsync("/api/v1/ui/actions/batch", body);
        Assert.True(batch.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(2, batch.RootElement.GetProperty("results").GetArrayLength());
        Assert.Equal("Batch updated", await GetTextAsync("InputBox"));
    }

    [Fact]
    public async Task Scroll_MovesScrollViewer()
    {
        using var scroll = await PostJsonAsync("/api/v1/ui/actions/scroll", "{\"elementId\":\"ScrollArea\",\"deltaX\":0,\"deltaY\":100}");
        Assert.True(scroll.RootElement.GetProperty("success").GetBoolean());

        using var element = await GetJsonAsync("/api/v1/ui/elements/ScrollArea");
        var offset = double.Parse(element.RootElement.GetProperty("frameworkProperties").GetProperty("verticalOffset").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
        Assert.True(offset > 0, $"vertical offset {offset}");
    }

    [Fact]
    public async Task Screenshots_ArePngs()
    {
        var window = await GetBytesAsync("/api/v1/ui/screenshot");
        Assert.True(IsPng(window));

        var element = await GetBytesAsync("/api/v1/ui/screenshot?id=ActionButton");
        Assert.True(IsPng(element));
        Assert.True(element.Length < window.Length, "an element screenshot should be smaller than the window's");
    }

    [Fact]
    public async Task Theme_GetAndSet()
    {
        using var get = await GetJsonAsync("/api/v1/device/app/theme");
        Assert.True(get.RootElement.TryGetProperty("supportedThemes", out _));

        using var dark = await SendJsonAsync(HttpMethod.Put, "/api/v1/device/app/theme", "{\"theme\":\"dark\"}");
        Assert.Equal("dark", dark.RootElement.GetProperty("userAppTheme").GetString());
        Assert.Equal("dark", dark.RootElement.GetProperty("theme").GetString());

        using var light = await SendJsonAsync(HttpMethod.Put, "/api/v1/device/app/theme", "{\"theme\":\"light\"}");
        Assert.Equal("light", light.RootElement.GetProperty("theme").GetString());
    }

    [Fact]
    public async Task InvokeActions_CallWindowMethods()
    {
        using var list = await GetJsonAsync("/api/v1/invoke/actions");
        var names = list.RootElement.GetProperty("actions").EnumerateArray().Select(a => a.GetProperty("name").GetString()).ToList();
        Assert.Contains("avalonia.echo", names);
        Assert.Contains("avalonia.set-status", names);

        using var echo = await InvokeActionAsync("avalonia.echo", "ping");
        Assert.Equal("ping", echo.RootElement.GetProperty("returnValue").GetString());

        using var _ = await InvokeActionAsync("avalonia.set-status", "set by action");
        Assert.Equal("set by action", await GetTextAsync("ResponseText"));
    }

    [Fact]
    public async Task SecondWindow_IsAnotherRoot_AndBackClosesIt()
    {
        using var tap = await PostJsonAsync("/api/v1/ui/actions/tap", "{\"elementId\":\"OpenWindowButton\"}");
        Assert.True(tap.RootElement.GetProperty("success").GetBoolean());

        await WaitForAsync(async () => (await CountRootsAsync()) == 2, "the second window to open");

        await WaitForAsync(async () =>
        {
            using var back = await SendAsync(HttpMethod.Post, "/api/v1/ui/actions/back", "{}");
            return (await CountRootsAsync()) == 1;
        }, "the second window to close");
    }

    [Fact]
    public async Task NativePressAndRelease_ReachTheControl()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux() && LinuxNativeInput.IsAvailable,
            "native pointer injection without extra permissions needs XTest (Linux with an X display); Windows needs the window in the foreground and macOS needs cliclick and Accessibility permission");

        using var move = await PostJsonAsync("/api/v1/ui/actions/move", "{\"elementId\":\"PointerTarget\"}");
        Assert.True(move.RootElement.GetProperty("ok").GetBoolean());
        var x = move.RootElement.GetProperty("x").GetDouble();
        var y = move.RootElement.GetProperty("y").GetDouble();

        using var press = await PostJsonAsync("/api/v1/ui/actions/press", $"{{\"x\":{x},\"y\":{y}}}");
        Assert.True(press.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("xtest", press.RootElement.GetProperty("mode").GetString());
        await WaitForAsync(async () => (await GetTextAsync("ResponseText"))?.StartsWith("Pointer pressed", StringComparison.Ordinal) == true, "the press to arrive");

        using var dragMove = await PostJsonAsync("/api/v1/ui/actions/drag-move", $"{{\"x\":{x + 20},\"y\":{y}}}");
        Assert.True(dragMove.RootElement.GetProperty("ok").GetBoolean());

        using var release = await PostJsonAsync("/api/v1/ui/actions/release", $"{{\"x\":{x + 20},\"y\":{y}}}");
        Assert.True(release.RootElement.GetProperty("ok").GetBoolean());
        await WaitForAsync(async () => (await GetTextAsync("ResponseText"))?.StartsWith("Pointer released", StringComparison.Ordinal) == true, "the release to arrive");
    }

    // ===== Helpers =====

    private async Task<int> CountRootsAsync()
    {
        using var doc = await GetJsonAsync("/api/v1/ui/tree");
        return Elements(doc.RootElement).Count();
    }

    private async Task<string?> GetTextAsync(string elementId)
    {
        using var doc = await GetJsonAsync($"/api/v1/ui/elements/{elementId}");
        return doc.RootElement.TryGetProperty("text", out var text) ? text.GetString() : null;
    }

    private Task<JsonDocument> InvokeActionAsync(string action, string argument)
        => PostJsonAsync($"/api/v1/invoke/actions/{action}", JsonSerializer.Serialize(new { args = new[] { argument } }));

    private async Task<JsonDocument> GetJsonAsync(string uri)
    {
        using var response = await Client.GetAsync(uri, TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.True(response.IsSuccessStatusCode, $"GET {uri} -> {(int)response.StatusCode}: {body}");
        return JsonDocument.Parse(body);
    }

    private async Task<byte[]> GetBytesAsync(string uri)
    {
        using var response = await Client.GetAsync(uri, TestContext.Current.CancellationToken);
        Assert.True(response.IsSuccessStatusCode, $"GET {uri} -> {(int)response.StatusCode}");
        return await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
    }

    private Task<JsonDocument> PostJsonAsync(string uri, string json) => SendJsonAsync(HttpMethod.Post, uri, json);

    private async Task<JsonDocument> SendJsonAsync(HttpMethod method, string uri, string json)
    {
        using var response = await SendAsync(method, uri, json);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.True(response.IsSuccessStatusCode, $"{method} {uri} -> {(int)response.StatusCode}: {body}\nApp output:\n{_host.Output}");
        return JsonDocument.Parse(body);
    }

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string uri, string json)
        => LeXtudio.DevFlow.Driver.DevFlowMutation.SendAsync(
            Client, method, uri, new StringContent(json, Encoding.UTF8, "application/json"), TestContext.Current.CancellationToken);

    private static async Task WaitForAsync(Func<Task<bool>> condition, string what)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            if (await condition())
                return;

            await Task.Delay(100, TestContext.Current.CancellationToken);
        }

        Assert.Fail($"Timed out waiting for {what}.");
    }

    /// <summary>The element list of a tree or query response, which is either an array or wrapped in an object.</summary>
    private static IEnumerable<JsonElement> Elements(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
            return root.EnumerateArray();

        foreach (var name in new[] { "elements", "tree", "roots" })
        {
            if (root.TryGetProperty(name, out var list) && list.ValueKind == JsonValueKind.Array)
                return list.EnumerateArray();
        }

        return Enumerable.Empty<JsonElement>();
    }

    private static IEnumerable<JsonElement> Flatten(JsonElement element)
    {
        yield return element;
        if (element.TryGetProperty("children", out var children) && children.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in children.EnumerateArray())
            {
                foreach (var descendant in Flatten(child))
                    yield return descendant;
            }
        }
    }

    private static bool IsPng(byte[] bytes)
        => bytes.Length > 8 && bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
}
