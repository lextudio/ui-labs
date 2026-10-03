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
/// This restores the LeXtudio behavior that the linked upstream sources no longer carry. The cost is that
/// responses produced here use reflection-based serialization, which is fine because these packages are not
/// published as trimmed or NativeAOT artifacts. Endpoints that should stay on the generated-metadata path —
/// and therefore keep working under trimming — should call <c>HttpResponse.Json</c> directly.
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
        Body = JsonSerializer.Serialize(data, Options),
    };

    internal static HttpResponse Json(object data, int statusCode) => new()
    {
        StatusCode = statusCode,
        Body = JsonSerializer.Serialize(data, Options),
    };
}