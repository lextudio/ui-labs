using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Microsoft.Maui.Cli.DevFlow;

/// <summary>
/// Minimal stand-in for the upstream CLI's source-generated <c>DevFlowCliJsonContext</c>.
/// </summary>
/// <remarks>
/// <para>
/// Only <c>LayoutDiagnosticsPolicy</c> needs this type, and only for a single <c>JsonTypeInfo</c> of its own
/// type. Linking the upstream <c>DevFlowCliJsonContext.cs</c> would drag in the CLI's MCP tools, broker and
/// Android port-forwarding sources.
/// </para>
/// <para>
/// This mirrors what this repository already does elsewhere: the hand-written <c>CliJson</c> helper in
/// <c>LeXtudio.DevFlow.Broker</c> replaces upstream's AOT source-generated JSON context. Reflection-based
/// metadata is acceptable because LeXtudio agents are not published as trimmed or NativeAOT artifacts.
/// </para>
/// </remarks>
internal sealed class DevFlowCliJsonContext
{
    private static readonly JsonSerializerOptions Options = new()
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
    };

    public static DevFlowCliJsonContext Default { get; } = new();

    public JsonTypeInfo<LayoutDiagnosticsPolicy> LayoutDiagnosticsPolicy
        => Info<LayoutDiagnosticsPolicy>();

    public JsonTypeInfo<Microsoft.Maui.Cli.DevFlow.Inspector.InspectorServer.InspectorDiagnosticRequest> InspectorDiagnosticRequest
        => Info<Microsoft.Maui.Cli.DevFlow.Inspector.InspectorServer.InspectorDiagnosticRequest>();

    public JsonTypeInfo<Microsoft.Maui.Cli.DevFlow.Inspector.LayoutDiagnosticsDelta> LayoutDiagnosticsDelta
        => Info<Microsoft.Maui.Cli.DevFlow.Inspector.LayoutDiagnosticsDelta>();

    public JsonTypeInfo<Microsoft.Maui.DevFlow.Agent.Core.LayoutFinding> LayoutFinding
        => Info<Microsoft.Maui.DevFlow.Agent.Core.LayoutFinding>();

    private JsonTypeInfo<T> Info<T>() => (JsonTypeInfo<T>)Options.GetTypeInfo(typeof(T));
}