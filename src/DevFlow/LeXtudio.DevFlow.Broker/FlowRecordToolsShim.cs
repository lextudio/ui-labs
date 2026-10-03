using Microsoft.Maui.Cli.DevFlow.Flows;

namespace Microsoft.Maui.Cli.DevFlow.Flows;

/// <summary>
/// Stand-in for the MCP-annotated <c>FlowRecordTools</c> that <c>BrokerFlowCoordinator</c> calls into.
/// </summary>
/// <remarks>
/// <para>
/// Upstream's <c>FlowRecordTools</c> is a Model Context Protocol tool surface: every method is annotated with
/// <c>[McpServerTool]</c> and the file depends on the <c>ModelContextProtocol</c> package plus the CLI's
/// <c>DevFlow.Mcp</c> helpers. Only two of its methods are reachable from the broker, both static and both
/// MCP-attribute-free: <c>AddStepCore</c> and <c>FinishToMarkdownCore</c>.
/// </para>
/// <para>
/// Pulling the MCP dependency into this package for two internal methods is not worth it, and the flow
/// recording they serve targets device automation rather than the desktop agents this broker discovers. The
/// inspector's own flow recording endpoints do work: they drive <c>FlowRecorder</c> directly and do not go
/// through this type. So the broker reports flow recording as unsupported instead of silently pretending.
/// </para>
/// </remarks>
internal static class FlowRecordTools
{
    private const string Unsupported =
        "Flow recording is not supported by the LeXtudio DevFlow broker. Use the inspector's flow recording endpoints instead.";

    internal static (bool ok, int seq, int stepCount, bool fragile, string? error) AddStepCore(
        FlowRecorder recorder, string action,
        string? automationId, string? text, string? type, int? index, string? id,
        string? value, string? name, double? dx, double? dy, int? itemIndex, string? position,
        string? page, bool navigated, string? assertsJson)
        => (false, -1, recorder.StepCount, false, Unsupported);

    internal static (bool ok, string? markdown, MauiFlow? flow, string? error) FinishToMarkdownCore(FlowRecorder recorder)
        => (false, null, null, Unsupported);
}