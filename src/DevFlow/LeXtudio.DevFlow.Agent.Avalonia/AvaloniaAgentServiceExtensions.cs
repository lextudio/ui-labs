using Avalonia;
using LeXtudio.DevFlow.Agent.Core;
using Microsoft.Maui.DevFlow.Agent.Core;

namespace LeXtudio.DevFlow.Agent.Avalonia;

public static class AvaloniaAgentServiceExtensions
{
    /// <summary>
    /// Starts a DevFlow agent for this Avalonia application. Call it once the application is
    /// initialized, e.g. from <c>OnFrameworkInitializationCompleted</c>.
    /// </summary>
    public static AvaloniaAgentService AddAvaloniaDevFlowAgent(this Application app, AgentOptions? options = null)
    {
        options ??= new AgentOptions();
        DevFlowAgentPortResolver.ApplyDefaultPort(options);

        var service = new AvaloniaAgentService(options);
        service.Start();
        return service;
    }
}
