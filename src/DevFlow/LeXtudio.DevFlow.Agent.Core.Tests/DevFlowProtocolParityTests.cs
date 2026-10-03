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
    private static readonly string[] ContractPathsNotYetServed =
    [
        "/api/v1/agent/capabilities",
        "/api/v1/device/app",
        "/api/v1/device/battery",
        "/api/v1/device/connectivity",
        "/api/v1/device/display",
        "/api/v1/device/geolocation",
        "/api/v1/device/info",
        "/api/v1/device/jobs",
        "/api/v1/device/jobs/{identifier}/run",
        "/api/v1/device/permissions",
        "/api/v1/device/permissions/{name}",
        "/api/v1/device/sensors",
        "/api/v1/device/sensors/{name}/start",
        "/api/v1/device/sensors/{name}/stop",
        "/api/v1/ext/{namespace}/{path}",
        "/api/v1/logs",
        "/api/v1/profiler/capabilities",
        "/api/v1/profiler/hotspots",
        "/api/v1/profiler/markers",
        "/api/v1/profiler/sessions",
        "/api/v1/profiler/sessions/{id}",
        "/api/v1/profiler/sessions/{id}/samples",
        "/api/v1/profiler/spans",
        "/api/v1/storage/files",
        "/api/v1/storage/files/{path}",
        "/api/v1/storage/preferences",
        "/api/v1/storage/preferences/{key}",
        "/api/v1/storage/roots",
        "/api/v1/storage/secure",
        "/api/v1/storage/secure/{key}",
        "/api/v1/ui/actions/gesture",
        "/api/v1/ui/actions/navigate",
        "/api/v1/ui/actions/resize",
        "/api/v1/ui/elements/{id}/properties/{name}",
        "/api/v1/webview/dom",
        "/api/v1/webview/dom/query",
        "/api/v1/webview/evaluate",
        "/api/v1/webview/input/click",
        "/api/v1/webview/input/fill",
        "/api/v1/webview/input/text",
        "/api/v1/webview/navigate",
        "/api/v1/webview/source",
    ];

    /// <summary>
    /// Routes the desktop agents serve that the shared contract does not describe, either because they
    /// are desktop-specific (pointer input) or because they predate the contract's naming.
    /// </summary>
    private static readonly string[] DesktopAdditionsOutsideContract =
    [
        "get /api/v1/alert/detect",
        "get /api/v1/invoke/actions",
        "get /api/v1/ui/query-selector",
        "post /api/v1/alert/dismiss",
        "post /api/v1/invoke/actions/{name}",
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
    ];

    private static IReadOnlySet<string> GetRegisteredOperations()
    {
        using var service = new StubAgentService([], new AgentOptions { Port = AgentTestHarness.GetFreePort() });
        return DevFlowProtocolSpec.ToOperations(AgentTestHarness.GetRegisteredRoutes(service));
    }

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
        var declared = DevFlowProtocolSpec.GetDeclaredOperations();
        var allowed = DesktopAdditionsOutsideContract.ToHashSet(StringComparer.Ordinal);

        var unexpected = GetRegisteredOperations()
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
        var declared = DevFlowProtocolSpec.GetDeclaredOperations();
        var served = GetRegisteredOperations();

        var missingPaths = declared
            .Select(op => op[(op.IndexOf(' ') + 1)..])
            .Distinct(StringComparer.Ordinal)
            .Where(path => !served.Any(op => op.EndsWith($" {path}", StringComparison.Ordinal)))
            .OrderBy(path => path, StringComparer.Ordinal);

        var tracked = ContractPathsNotYetServed.ToHashSet(StringComparer.Ordinal);

        Assert.Equal(tracked.OrderBy(p => p, StringComparer.Ordinal), missingPaths);
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