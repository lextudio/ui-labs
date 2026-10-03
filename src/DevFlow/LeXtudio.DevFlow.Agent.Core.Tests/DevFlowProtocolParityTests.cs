using Microsoft.Maui.DevFlow.Agent.Core;
using Xunit;

namespace LeXtudio.DevFlow.Agent.Core.Tests;

/// <summary>
/// Compares the agent's registered HTTP surface against the DevFlow protocol contract in the pinned
/// <c>maui-labs</c> submodule.
/// </summary>
/// <remarks>
/// These tests exist to make the upstream sync verifiable. The submodule's <c>openapi.yaml</c> moves
/// forward when the pointer is bumped, so the gap this file reports shrinks as endpoints are adopted:
/// remove entries from the allow-lists as they are implemented, and the remaining entries are the
/// remaining work.
/// </remarks>
public class DevFlowProtocolParityTests
{
    /// <summary>
    /// Contract paths not yet served by the desktop agents. Desktop-only additions that are not in
    /// the contract are tracked separately in <see cref="DesktopAdditionsOutsideContract"/>.
    /// </summary>
    /// <summary>
    /// Contract paths no agent serves. The desktop agents inherit upstream's whole route table, so this
    /// shrank to the extension namespace, which only mobile hosts with registered extensions implement.
    /// </summary>
    private static readonly string[] ContractPathsNotYetServed =
    [
        "/api/v1/ext/{namespace}/{path}",
    ];

    /// <summary>
    /// Routes the desktop agents serve that the shared contract does not describe, either because they
    /// are desktop-specific (pointer input) or because they predate the contract's naming.
    /// </summary>
    private static readonly string[] DesktopAdditionsOutsideContract =
    [
"get /api/v1/alert/detect",
        "get /api/v1/ui/query-selector",
        "post /api/v1/alert/dismiss",
        "post /api/v1/ui/actions/click",
        "post /api/v1/ui/actions/drag",
        "post /api/v1/ui/actions/drag-move",
        "post /api/v1/ui/actions/keydown",
        "post /api/v1/ui/actions/keyup",
        "post /api/v1/ui/actions/move",
        "post /api/v1/ui/actions/press",
        "post /api/v1/ui/actions/release",
        "post /api/v1/ui/actions/right-tap",
        "post /api/v1/ui/assert",
        "post /api/v1/webview/cdp",
        // Upstream registers these with different placeholder names than the contract declares
        // ({permission} and {sensor} here, {name} in openapi.yaml). The paths match once the
        // placeholders are normalized, so they are served; only the names differ.
        "get /api/v1/device/permissions/{permission}",
        "post /api/v1/device/sensors/{sensor}/start",
        "post /api/v1/device/sensors/{sensor}/stop",
    ];

    private static IReadOnlySet<string> GetRegisteredOperations()
    {
        // EnableLayoutDiagnostics defaults to false, and the layout diagnostics routes are only registered
        // when it is on. Turn it on so the baseline reflects the whole contract surface rather than the
        // default subset.
        using var service = new StubAgentService([], new AgentOptions
        {
            Port = AgentTestHarness.GetFreePort(),
            EnableLayoutDiagnostics = true,
        });
        return DevFlowProtocolSpec.ToOperations(AgentTestHarness.GetRegisteredRoutes(service));
    }

    /// <summary>
    /// Replaces route placeholder names with a fixed token. Upstream and the contract disagree on some
    /// placeholder names (for example {permission} versus {name}), which does not change which endpoint is
    /// served, so comparisons normalize them instead of reporting a false mismatch.
    /// </summary>
    private static string NormalizePlaceholders(string operation)
    {
        var separator = operation.IndexOf(' ');
        var method = separator < 0 ? operation : operation[..separator];
        var path = separator < 0 ? string.Empty : operation[(separator + 1)..];
        return $"{method} {System.Text.RegularExpressions.Regex.Replace(path, @"\{[^}]*\}", "{}")}";
    }

    private static IReadOnlySet<string> Normalized(IEnumerable<string> operations)
        => operations.Select(NormalizePlaceholders).ToHashSet(StringComparer.Ordinal);

    [Fact]
    public void ContractSpec_IsDiscoverableFromTheSubmodule()
    {
        var operations = DevFlowProtocolSpec.GetDeclaredOperations();

        Assert.True(operations.Count > 50, $"Only {operations.Count} operations were parsed from the protocol contract.");
        Assert.Contains("get /api/v1/ui/tree", operations);
        Assert.Contains("post /api/v1/ui/actions/tap", operations);
    }

    [Fact]
    public void Agent_OnlyServesRoutesDeclaredInTheContractOrAllowListed()
    {
        var declared = Normalized(DevFlowProtocolSpec.GetDeclaredOperations());
        var allowed = Normalized(DesktopAdditionsOutsideContract);

        var unexpected = GetRegisteredOperations()
            .Select(NormalizePlaceholders)
            .Where(op => !declared.Contains(op))
            .Where(op => !allowed.Contains(op))
            .OrderBy(op => op, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            unexpected.Length == 0,
            "These routes are neither in the DevFlow contract nor declared as desktop additions:\n  "
            + string.Join("\n  ", unexpected));
    }

    [Fact]
    public void MissingContractPaths_MatchTheTrackedSyncBacklog()
    {
        var declared = DevFlowProtocolSpec.GetDeclaredOperations()
            .Select(op => op[(op.IndexOf(' ') + 1)..])
            .Select(path => System.Text.RegularExpressions.Regex.Replace(path, @"\{[^}]*\}", "{}"))
            .Distinct(StringComparer.Ordinal);

        var served = GetRegisteredOperations()
            .Select(op => op[(op.IndexOf(' ') + 1)..])
            .Select(path => System.Text.RegularExpressions.Regex.Replace(path, @"\{[^}]*\}", "{}"))
            .ToHashSet(StringComparer.Ordinal);

        var missingPaths = declared
            .Where(path => !served.Contains(path))
            .OrderBy(path => path, StringComparer.Ordinal);

        var tracked = ContractPathsNotYetServed
            .Select(path => System.Text.RegularExpressions.Regex.Replace(path, @"\{[^}]*\}", "{}"))
            .OrderBy(p => p, StringComparer.Ordinal);

        Assert.Equal(tracked, missingPaths);
    }

    [Fact]
    public void DesktopAdditions_AreAllStillRegistered()
    {
        var served = GetRegisteredOperations();

        var missing = DesktopAdditionsOutsideContract
            .Where(op => !served.Contains(op))
            .OrderBy(op => op, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            missing.Length == 0,
            "These tracked desktop routes are no longer served:\n  " + string.Join("\n  ", missing));
    }
}