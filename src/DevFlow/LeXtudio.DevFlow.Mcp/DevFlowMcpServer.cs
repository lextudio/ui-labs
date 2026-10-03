using System.Reflection;
using LeXtudio.DevFlow.Mcp.Tools;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace LeXtudio.DevFlow.Mcp;

/// <summary>
/// Runs the MCP server over stdio so an AI agent can drive a LeXtudio DevFlow desktop agent.
/// </summary>
/// <remarks>
/// <para>
/// The server is built directly on <see cref="McpServer.Create"/> rather than through a generic host: the
/// only service the tools need is <see cref="DevFlowMcpSession"/>, so a hosting container would add a
/// dependency and a configuration layer without buying anything.
/// </para>
/// <para>
/// stdio carries JSON-RPC, so nothing may write to stdout. There is no console logger here for that
/// reason - logging is discarded rather than redirected, because a library has no business choosing an
/// output stream. A CLI that owns the process can add its own stderr logging.
/// </para>
/// </remarks>
public static class DevFlowMcpServer
{
    /// <summary>Serves MCP on stdio until the client disconnects.</summary>
    /// <param name="defaultAgentPort">
    /// Port to use when the agent cannot be resolved automatically. Leave unset to let the session ask the
    /// broker, which is what an agent driving a locally started app normally wants.
    /// </param>
    /// <param name="agentHost">Host the agent listens on. Defaults to loopback.</param>
    /// <param name="cancellationToken">Stops the server.</param>
    /// <param name="loggerFactory">
    /// Where the SDK reports tool failures. Defaults to discarding them, because a library should not pick an
    /// output stream; pass a stderr logger when hosting the process. Without one, a failing tool surfaces to
    /// the AI agent only as "an error occurred", which hides the actual exception.
    /// </param>
    public static async Task RunAsync(
        int? defaultAgentPort = null,
        string agentHost = "localhost",
        CancellationToken cancellationToken = default,
        ILoggerFactory? loggerFactory = null)
    {
        var logger = loggerFactory ?? NullLoggerFactory.Instance;
        var version = typeof(DevFlowMcpServer).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

        var session = new DevFlowMcpSession
        {
            DefaultAgentPort = defaultAgentPort,
            AgentHost = agentHost,
        };

        var options = new McpServerOptions
        {
            ServerInfo = new Implementation { Name = "lextudio-devflow", Version = version },
            ServerInstructions =
                "Inspect and drive a LeXtudio DevFlow desktop application. Start with devflow_tree to obtain " +
                "element IDs, then use devflow_tap, devflow_fill or devflow_assert on them. " +
                "Element IDs are not stable across app restarts, so re-read the tree after one.",
        };

        // ToolCollection is null on a fresh McpServerOptions and there is no public factory for it, so
        // create the collection here. The constructor takes an optional equality comparer; tool names are
        // already unique by attribute, so the default is fine.
        Tools.DevFlowTools.Session = session;

        var toolCollection = new McpServerPrimitiveCollection<McpServerTool>(null);
        foreach (var tool in CreateTools())
        {
            toolCollection.Add(tool);
        }

        options.ToolCollection = toolCollection;

        var services = new SessionServiceProvider(session);
        var server = McpServer.Create(
            new StdioServerTransport(options, logger),
            options,
            logger,
            services);

        await server.RunAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Builds the tool list from the attributed methods on <see cref="DevFlowTools"/>.
    /// </summary>
    /// <remarks>
    /// Reflection keeps the tool list in one place: adding a method with an attribute is enough, and the
    /// server cannot drift out of sync with the class the way a hand-maintained list would.
    /// </remarks>
    private static List<McpServerTool> CreateTools()
    {
        var tools = new List<McpServerTool>();
        foreach (var method in typeof(Tools.DevFlowTools).GetMethods(BindingFlags.Public | BindingFlags.Static))
        {
            if (method.GetCustomAttribute<McpServerToolAttribute>() is null)
            {
                continue;
            }

            tools.Add(McpServerTool.Create(method, target: null, options: null));
        }

        return tools;
    }

    /// <summary>
    /// Supplies the single service the tools ask for. The SDK resolves each tool's parameters from here.
    /// </summary>
    private sealed class SessionServiceProvider(DevFlowMcpSession session) : IServiceProvider
    {
        public object? GetService(Type serviceType)
            => serviceType == typeof(DevFlowMcpSession) ? session : null;
    }
}
