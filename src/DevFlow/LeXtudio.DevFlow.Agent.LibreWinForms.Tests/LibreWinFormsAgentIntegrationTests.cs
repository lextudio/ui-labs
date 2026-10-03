using System.Text.Json;
using Xunit;

namespace LeXtudio.DevFlow.Agent.LibreWinForms.Tests;

/// <summary>
/// End-to-end coverage for <c>LeXtudio.DevFlow.Agent.LibreWinForms</c> running inside a real LibreWinForms
/// app on the current machine.
/// </summary>
/// <remarks>
/// These assertions are the ones that cannot be inherited from the WinForms agent for free: they prove
/// that the parts of the WinForms agent which rely on the app model and rendering stack actually work on
/// LibreWinForms — form enumeration, <c>PerformClick</c>-based tapping, text mutation, and
/// <c>DrawToBitmap</c> screenshots.
/// </remarks>
public class LibreWinFormsAgentIntegrationTests
{
    [Fact]
    public async Task AgentStatus_ReportsRunning()
    {
        if (SkipWhenUnsupported()) return;
        await using var host = await LibreWinFormsHost.StartAsync(TestContext.Current.CancellationToken);
        using var client = host.CreateClient();

        var status = await DevFlowAssert.GetJsonAsync(client, "/api/v1/agent/status", TestContext.Current.CancellationToken);

        Assert.True(status.GetProperty("running").GetBoolean());
        Assert.Equal("LeXtudio.DevFlow.Agent", status.GetProperty("name").GetString());
        Assert.Equal("winforms", status.GetProperty("framework").GetString());
    }

    [Fact]
    public async Task UiTree_ContainsTheFormAndItsControls()
    {
        if (SkipWhenUnsupported()) return;
        await using var host = await StartHostWithElementAsync("MainForm");
        using var client = host.CreateClient();

        var tree = await DevFlowAssert.GetJsonAsync(client, "/api/v1/ui/tree", TestContext.Current.CancellationToken);

        var ids = tree.GetProperty("elements").EnumerateArray()
            .SelectMany(Flatten)
            .Select(element => element.GetProperty("id").GetString())
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("MainForm", ids);
        Assert.Contains("ActionButton", ids);
        Assert.Contains("InputBox", ids);
        Assert.Contains("ResponseLabel", ids);
        Assert.Contains("MainScrollPanel", ids);
    }

    [Fact]
    public async Task GetElement_ReturnsButtonMetadata()
    {
        if (SkipWhenUnsupported()) return;
        await using var host = await StartHostWithElementAsync("ActionButton");
        using var client = host.CreateClient();

        var element = await DevFlowAssert.GetJsonAsync(client, "/api/v1/ui/elements/ActionButton", TestContext.Current.CancellationToken);

        Assert.Equal("ActionButton", element.GetProperty("id").GetString());
        Assert.Equal("Button", element.GetProperty("type").GetString());
        Assert.Equal("Tap Me", element.GetProperty("text").GetString());
    }

    [Fact]
    public async Task TapButton_InvokesTheClickHandler()
    {
        if (SkipWhenUnsupported()) return;
        await using var host = await StartHostWithElementAsync("ResponseLabel");
        using var client = host.CreateClient();

        Assert.Equal("ready", await DevFlowAssert.GetTextAsync(client, "ResponseLabel", TestContext.Current.CancellationToken));

        var result = await DevFlowAssert.PostJsonAsync(
            client, "/api/v1/ui/actions/tap", """{ "id": "ActionButton" }""", TestContext.Current.CancellationToken);

        Assert.True(result.GetProperty("success").GetBoolean());
        Assert.Equal("Button clicked", await DevFlowAssert.GetTextAsync(client, "ResponseLabel", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task TapButton_AcceptsElementIdAsWellAsId()
    {
        // Upstream's own AgentClient sends "elementId" for every action, while this agent originally only
        // accepted "id". Anything built on that library - the MCP server, for one - therefore failed to tap
        // at all. Both spellings must drive the same handler.
        if (SkipWhenUnsupported()) return;
        await using var host = await StartHostWithElementAsync("ResponseLabel");
        using var client = host.CreateClient();

        var result = await DevFlowAssert.PostJsonAsync(
            client, "/api/v1/ui/actions/tap", """{ "elementId": "ActionButton" }""", TestContext.Current.CancellationToken);

        Assert.True(result.GetProperty("success").GetBoolean());
        Assert.Equal("Button clicked", await DevFlowAssert.GetTextAsync(client, "ResponseLabel", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task FillAndClear_UpdateTheTextBox()
    {
        if (SkipWhenUnsupported()) return;
        await using var host = await StartHostWithElementAsync("InputBox");
        using var client = host.CreateClient();

        var filled = await DevFlowAssert.PostJsonAsync(
            client, "/api/v1/ui/actions/fill", """{ "elementId": "InputBox", "text": "hello" }""", TestContext.Current.CancellationToken);
        Assert.True(filled.GetProperty("success").GetBoolean());
        Assert.Equal("hello", await DevFlowAssert.GetTextAsync(client, "InputBox", TestContext.Current.CancellationToken));

        var cleared = await DevFlowAssert.PostJsonAsync(
            client, "/api/v1/ui/actions/clear", """{ "elementId": "InputBox" }""", TestContext.Current.CancellationToken);
        Assert.True(cleared.GetProperty("success").GetBoolean());
        Assert.Equal(string.Empty, await DevFlowAssert.GetTextAsync(client, "InputBox", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ScrollPanel_AcceptsScrollRequests()
    {
        if (SkipWhenUnsupported()) return;
        await using var host = await StartHostWithElementAsync("MainScrollPanel");
        using var client = host.CreateClient();

        var result = await DevFlowAssert.PostJsonAsync(
            client, "/api/v1/ui/actions/scroll", """{ "id": "MainScrollPanel", "deltaX": 0, "deltaY": 300 }""",
            TestContext.Current.CancellationToken);

        Assert.True(result.GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task Screenshot_ReturnsWindowAndElementPngs()
    {
        if (SkipWhenUnsupported()) return;
        await using var host = await StartHostWithElementAsync("ActionButton");
        using var client = host.CreateClient();

        using var windowResponse = await client.GetAsync("/api/v1/ui/screenshot", TestContext.Current.CancellationToken);
        windowResponse.EnsureSuccessStatusCode();
        Assert.Equal("image/png", windowResponse.Content.Headers.ContentType?.MediaType);

        var windowBytes = await windowResponse.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.True(DevFlowAssert.IsPng(windowBytes), "The window screenshot was not a PNG.");

        using var elementResponse = await client.GetAsync("/api/v1/ui/screenshot?id=ActionButton", TestContext.Current.CancellationToken);
        elementResponse.EnsureSuccessStatusCode();
        var elementBytes = await elementResponse.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.True(DevFlowAssert.IsPng(elementBytes), "The element screenshot was not a PNG.");
    }

    [Fact]
    public async Task InvokeAction_EchoesThroughTheAgent()
    {
        if (SkipWhenUnsupported()) return;
        await using var host = await StartHostWithElementAsync("ActionButton");
        using var client = host.CreateClient();

        var actions = await DevFlowAssert.GetJsonAsync(client, "/api/v1/invoke/actions", TestContext.Current.CancellationToken);
        Assert.Contains(
            actions.GetProperty("actions").EnumerateArray(),
            action => action.GetProperty("name").GetString() == "librewinforms.echo");

        var result = await DevFlowAssert.PostJsonAsync(
            client, "/api/v1/invoke/actions/librewinforms.echo", """{ "args": ["hi"] }""", TestContext.Current.CancellationToken);

        Assert.True(result.GetProperty("success").GetBoolean());
        Assert.Equal("echo:hi", result.GetProperty("returnValue").GetString());
    }

    [Fact]
    public async Task QueryElements_AndAssert_WorkAgainstTheLiveTree()
    {
        if (SkipWhenUnsupported()) return;
        await using var host = await StartHostWithElementAsync("ActionButton");
        using var client = host.CreateClient();

        var buttons = await DevFlowAssert.GetJsonAsync(client, "/api/v1/ui/elements?type=Button", TestContext.Current.CancellationToken);
        Assert.Contains(buttons.EnumerateArray(), element => element.GetProperty("id").GetString() == "ActionButton");

        var assertion = await DevFlowAssert.PostJsonAsync(
            client, "/api/v1/ui/assert", """{ "selector": "#ActionButton", "exists": true, "count": 1 }""",
            TestContext.Current.CancellationToken);

        Assert.True(assertion.GetProperty("success").GetBoolean());
        Assert.Equal(1, assertion.GetProperty("matchCount").GetInt32());
    }

    /// <summary>
    /// Starts a host app and waits until the given element is present. The agent answers before the main
    /// form is shown, so interaction tests cannot assume the control tree is populated.
    /// </summary>
    private static async Task<LibreWinFormsHost> StartHostWithElementAsync(string elementId)
    {
        var host = await LibreWinFormsHost.StartAsync(TestContext.Current.CancellationToken);
        try
        {
            using var client = host.CreateClient();
            await LibreWinFormsHost.WaitForElementAsync(
                client, elementId, TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
            return host;
        }
        catch
        {
            await host.DisposeAsync();
            throw;
        }
    }

    private static IEnumerable<JsonElement> Flatten(JsonElement element)
    {
        yield return element;

        if (!element.TryGetProperty("children", out var children) || children.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var child in children.EnumerateArray())
        {
            foreach (var descendant in Flatten(child))
            {
                yield return descendant;
            }
        }
    }

    private static bool SkipWhenUnsupported()
    {
        var reason = LibreWinFormsHost.SkipReason;
        if (reason is not null)
        {
            Assert.Skip(reason);
        }

        return false;
    }
}