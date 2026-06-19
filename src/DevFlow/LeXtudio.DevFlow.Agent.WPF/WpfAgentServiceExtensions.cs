using System.Windows;
using Microsoft.Maui.DevFlow.Agent.Core;
using LeXtudio.DevFlow.Agent.Core;

namespace LeXtudio.DevFlow.Agent.WPF;

public static class WpfAgentServiceExtensions
{
    public static WpfAgentService AddWpfDevFlowAgent(this Application app, AgentOptions? options = null)
    {
        options ??= new AgentOptions();
        DevFlowAgentPortResolver.ApplyDefaultPort(options);

        var service = new WpfAgentService(options);

        // Start the HTTP listener on a thread-pool thread so the caller (which may
        // be on the WPF UI thread, e.g. inside App() constructor or OnStartup) is
        // never blocked while the socket is being bound.
        _ = System.Threading.Tasks.Task.Run(service.Start);

        app.Exit += async (_, _) =>
        {
            await service.StopAsync().ConfigureAwait(false);
        };

        return service;
    }
}
