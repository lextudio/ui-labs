using System.ComponentModel;
using System.Text.Json;
using Microsoft.Maui.DevFlow.Driver;
using ModelContextProtocol.Server;

namespace LeXtudio.DevFlow.Mcp.Tools;

/// <summary>
/// The MCP surface exposed for the LeXtudio desktop agents.
/// </summary>
/// <remarks>
/// Deliberately limited to what every desktop agent implements. Upstream's MAUI CLI exposes 23 tools, but
/// roughly a third of them (storage, sensors, jobs, platform files) map to capabilities the desktop agents
/// answer with <c>not_supported</c>; advertising them would hand an AI agent a menu of calls that cannot
/// work. Each tool below maps to an endpoint the desktop agents actually serve, and the set grows as the
/// agents implement more.
/// </remarks>
[McpServerToolType]
public static class DevFlowTools
{
    /// <summary>
    /// The session every tool call uses. Set once by <see cref="DevFlowMcpServer"/> before serving.
    /// </summary>
    /// <remarks>
    /// A static is deliberate. The SDK's target-factory overloads need the session either as an annotated
    /// parameter or as a resolved service, and an unannotated complex parameter is instead treated as a
    /// tool argument, so every call fails with "missing a value for the required parameter". One MCP
    /// server process serves one agent session, so there is nothing to make per-call stateful here; the
    /// per-call state that does matter, the mutation lease, lives on the session itself.
    /// </remarks>
    internal static DevFlowMcpSession Session { get; set; } = null!;

    [McpServerTool(Name = "devflow_tree"), Description(
        "Inspect the visual tree of the running desktop app. Returns a structured hierarchy with element IDs, " +
        "types, bounds, visibility and text. Use the IDs from this tree as the elementId argument of the " +
        "interaction tools; they are not stable across app restarts, so re-read the tree after one.")]
    public static async Task<string> Tree(
        [Description("Agent HTTP port. Optional when only one agent is registered for this project.")]
        int? agentPort = null,
        [Description("Maximum tree depth to return.")] int depth = 50,
        [Description("Window index for multi-window apps.")] int? window = null)
    {
        using var agent = await Session.GetAgentClientAsync(agentPort);

        // GetTreeSnapshotAsync tolerates both response shapes - the bare array the agents return by
        // default and the { "elements": [...] } envelope - so this keeps working whichever the agent
        // answers with, and it also surfaces the tree revision.
        var snapshot = await agent.GetTreeSnapshotAsync(depth, window);

        return snapshot is null || snapshot.Elements.Count == 0
            ? "The visual tree is empty. Is the agent running and has the app created its main window?"
            : JsonSerializer.Serialize(snapshot.Elements, ToolJson.Options);
    }

    [McpServerTool(Name = "devflow_element"), Description(
        "Fetch one element from the visual tree by ID, including its bounds, text and enabled/visible state. " +
        "Use this to confirm an element exists and is interactive before acting on it.")]
    public static async Task<string> Element(
        [Description("Element ID from the visual tree.")] string elementId,
        [Description("Agent HTTP port. Optional when only one agent is registered for this project.")]
        int? agentPort = null)
    {
        using var agent = await Session.GetAgentClientAsync(agentPort);
        var element = await agent.GetElementAsync(elementId);

        return element is null
            ? $"No element with ID '{elementId}'. Read the tree again: IDs change when the UI is rebuilt."
            : JsonSerializer.Serialize(element, ToolJson.Options);
    }

    [McpServerTool(Name = "devflow_tap"), Description(
        "Click an element by ID, invoking its normal click handler. Waits for the app to settle before " +
        "returning, so a following read of the tree observes the result.")]
    public static async Task<string> Tap(
        [Description("Element ID from the visual tree.")] string elementId,
        [Description("Agent HTTP port. Optional when only one agent is registered for this project.")]
        int? agentPort = null)
    {
        using var agent = await Session.GetAgentClientAsync(agentPort);
        var tapped = await agent.TapAsync(elementId, captureEpoch: null, registryGeneration: null);

        return tapped
            ? $"Tapped '{elementId}'."
            : $"The agent could not tap '{elementId}'. It may be disabled, obscured, or have no click handler.";
    }

    [McpServerTool(Name = "devflow_fill"), Description(
        "Replace the text of an editable element (text box, entry) and raise its change notification, as a " +
        "user typing would.")]
    public static async Task<string> Fill(
        [Description("Element ID of the editable control.")] string elementId,
        [Description("Text to set.")] string text,
        [Description("Agent HTTP port. Optional when only one agent is registered for this project.")]
        int? agentPort = null)
    {
        using var agent = await Session.GetAgentClientAsync(agentPort);
        var filled = await agent.FillAsync(elementId, text, captureEpoch: null, registryGeneration: null);

        return filled
            ? $"Filled '{elementId}'."
            : $"The agent could not fill '{elementId}'. It may not be an editable control.";
    }

    [McpServerTool(Name = "devflow_clear"), Description(
        "Clear the text of an editable element, leaving it empty.")]
    public static async Task<string> Clear(
        [Description("Element ID of the editable control.")] string elementId,
        [Description("Agent HTTP port. Optional when only one agent is registered for this project.")]
        int? agentPort = null)
    {
        using var agent = await Session.GetAgentClientAsync(agentPort);
        var cleared = await agent.ClearAsync(elementId, captureEpoch: null, registryGeneration: null);

        return cleared
            ? $"Cleared '{elementId}'."
            : $"The agent could not clear '{elementId}'. It may not be an editable control.";
    }

    [McpServerTool(Name = "devflow_screenshot"), Description(
        "Capture a PNG screenshot, either the whole window or a single element. Returns base64-encoded PNG " +
        "data. Prefer devflow_tree and devflow_element for assertions: an image cannot be compared " +
        "programmatically, so it is for inspection, not verification.")]
    public static async Task<string> Screenshot(
        [Description("Element ID to capture. Omit for the whole window.")] string? elementId = null,
        [Description("Window index for multi-window apps.")] int? window = null,
        [Description("Agent HTTP port. Optional when only one agent is registered for this project.")]
        int? agentPort = null)
    {
        using var agent = await Session.GetAgentClientAsync(agentPort);
        var png = await agent.ScreenshotAsync(window, elementId);

        return png is null or { Length: 0 }
            ? "The agent returned no image. The window may be minimized or not yet rendered."
            : $"PNG ({png.Length} bytes), base64:\n{Convert.ToBase64String(png)}";
    }

    [McpServerTool(Name = "devflow_assert"), Description(
        "Assert that an element property equals an expected value, returning PASS or FAIL with the actual " +
        "value. Prefer this over reading the tree and judging by eye: the comparison happens in the agent " +
        "and reports both sides, so a failure says what it really was. Get devflow_element to discover the " +
        "property names an agent supports.")]
    public static async Task<string> Assert(
        [Description("Property to check, for example Text, IsVisible or IsEnabled.")]
        string propertyName,
        [Description("Value the property is expected to have.")] string expectedValue,
        [Description("Element ID from the visual tree. Provide either this or automationId.")]
        string? elementId = null,
        [Description("AutomationId of the element to resolve.")] string? automationId = null,
        [Description("Agent HTTP port. Optional when only one agent is registered for this project.")]
        int? agentPort = null)
    {
        using var agent = await Session.GetAgentClientAsync(agentPort);

        var resolvedId = elementId;
        if (resolvedId is null && automationId is not null)
        {
            var matches = await agent.QueryAsync(automationId: automationId);
            if (matches.Count == 0)
            {
                return $"FAIL: no element has AutomationId '{automationId}'.";
            }

            resolvedId = matches[0].Id;
        }

        if (resolvedId is null)
        {
            return "FAIL: provide either elementId or automationId.";
        }

        var actual = await agent.GetPropertyAsync(resolvedId, propertyName);
        if (actual is null)
        {
            return $"FAIL: '{resolvedId}' has no property '{propertyName}'. Read the element for the names it supports.";
        }

        var passed = string.Equals(actual, expectedValue, StringComparison.Ordinal);

        return passed
            ? $"PASS: {resolvedId}.{propertyName} == \"{expectedValue}\""
            : $"FAIL: {resolvedId}.{propertyName} is \"{actual}\", expected \"{expectedValue}\"";
    }

    private static class ToolJson
    {
        /// <summary>
        /// camelCase to match the DevFlow HTTP payloads, and a depth high enough for a real tree: the
        /// default of 64 throws on a deep UI, which is exactly the case an agent is asked to inspect.
        /// </summary>
        internal static readonly JsonSerializerOptions Options = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            MaxDepth = 256,
            WriteIndented = false,
        };
    }
}
