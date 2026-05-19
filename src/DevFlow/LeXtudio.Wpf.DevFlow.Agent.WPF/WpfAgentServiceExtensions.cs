using System.Windows;
using Microsoft.Maui.DevFlow.Agent.Core;
using LeXtudio.Wpf.DevFlow.Agent.Core;

namespace LeXtudio.Wpf.DevFlow.Agent.WPF;

public static class WpfAgentServiceExtensions
{
    public static WpfAgentService AddWpfDevFlowAgent(this Application app, AgentOptions? options = null)
    {
        var service = new WpfAgentService(options);
        service.Start();

        app.Exit += async (_, _) =>
        {
            await service.StopAsync().ConfigureAwait(false);
        };

        return service;
    }
}
