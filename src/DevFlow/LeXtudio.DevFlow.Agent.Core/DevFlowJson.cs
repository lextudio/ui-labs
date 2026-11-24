using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Maui.DevFlow.Agent.Core;

namespace LeXtudio.DevFlow.Agent.Core;

/// <summary>
/// Builds JSON responses for host applications with deep or self-referencing UI trees.
/// </summary>
/// <remarks>
/// <para>
/// Upstream's <c>HttpResponse.Json</c> serializes through <c>AgentJson</c>, which is tuned for trimmed and
/// NativeAOT hosts: it resolves type metadata from generated contexts and relies on the System.Text.Json
/// defaults for everything else. Those defaults cap object nesting at 64 levels and reject cycles, so a real
/// docking layout fails <c>GET /api/v1/ui/tree</c> with "a possible object cycle was detected".
/// </para>
/// <para>
/// This restores the LeXtudio behavior that the linked upstream sources no longer carry by passing our options
/// (depth 256, cycles ignored) to <c>AgentJson.Serialize</c>. Metadata still comes from the generated contexts,
/// so a host with JSON reflection disabled (NativeAOT) can serialize what those contexts and the built-in
/// dictionary/primitive contracts cover; anonymous objects need reflection and are only available where it is on.
/// </para>
/// <para>
/// Measured effect versus the System.Text.Json defaults: a UI tree nests 127 levels instead of 32, and a
/// self-referencing tree serializes instead of throwing.
/// </para>
/// </remarks>
internal static class DevFlowJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        MaxDepth = 256,
        ReferenceHandler = ReferenceHandler.IgnoreCycles,
    };

    internal static HttpResponse Json(object data) => new()
    {
        Body = AgentJson.Serialize(data, Options),
    };

    internal static HttpResponse Json(object data, int statusCode) => new()
    {
        StatusCode = statusCode,
        Body = AgentJson.Serialize(data, Options),
    };
}