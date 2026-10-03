using Microsoft.Maui.Cli.DevFlow.Broker;
using Microsoft.Maui.DevFlow.Driver;

namespace LeXtudio.DevFlow.Mcp;

/// <summary>
/// Resolves which DevFlow agent an MCP tool call should talk to, and hands out clients for it.
/// </summary>
/// <remarks>
/// <para>
/// One session serves the whole MCP connection, so the lease id is created once and reused. That matters
/// because upstream defaults <c>AgentOptions.RequireMutationLease</c> to true: every tap, fill and clear
/// is rejected with 409 unless it carries a lease, and a fresh id per call would have each call evict the
/// previous one.
/// </para>
/// <para>
/// Port resolution is deliberately forgiving, because an AI agent should not need to be told where the app
/// is listening: an explicitly configured port wins, then a broker lookup for the current project, then a
/// single registered agent. Failing that the error names the ways to fix it rather than leaving the caller
/// guessing.
/// </para>
/// </remarks>
public sealed class DevFlowMcpSession
{
    private readonly string _leaseId = Guid.NewGuid().ToString("N");
    private int? _defaultAgentPort;

    /// <summary>Host the agent listens on. Defaults to loopback, which is what the agents bind.</summary>
    public string AgentHost { get; set; } = "localhost";

    /// <summary>Port to use when a tool call does not name one.</summary>
    public int? DefaultAgentPort
    {
        get => _defaultAgentPort;
        set => _defaultAgentPort = value;
    }

    /// <summary>Creates an agent client owned by the caller, which must dispose it.</summary>
    public async Task<AgentClient> GetAgentClientAsync(int? agentPort = null)
    {
        var port = agentPort ?? DefaultAgentPort ?? await ResolveAgentPortAsync().ConfigureAwait(false);
        return new AgentClient(AgentHost, port)
        {
            MutationLeaseId = _leaseId,
            MutationLeaseHolderKind = "mcp",
            MutationLeaseLabel = "MCP client",
        };
    }

    private async Task<int> ResolveAgentPortAsync()
    {
        var agent = await BrokerClient.ResolveAgentForProjectAsync().ConfigureAwait(false);
        if (agent is not null)
        {
            DefaultAgentPort = agent.Port;
            return agent.Port;
        }

        var brokerPort = await BrokerClient.EnsureBrokerRunningAsync().ConfigureAwait(false);
        var agents = brokerPort is null ? null : await BrokerClient.ListAgentsAsync(brokerPort.Value).ConfigureAwait(false);
        if (agents is { Length: 1 })
        {
            DefaultAgentPort = agents[0].Port;
            return agents[0].Port;
        }

        var count = agents?.Length ?? 0;
        throw new InvalidOperationException(
            count == 0
                ? "No DevFlow agent is registered, so there is nothing to drive. Start the app with a DevFlow agent, " +
                  "or pass an explicit port to the server."
                : $"{count} DevFlow agents are registered and none could be matched to this project, so the target is " +
                  "ambiguous. Pass an explicit port to the server.");
    }
}
