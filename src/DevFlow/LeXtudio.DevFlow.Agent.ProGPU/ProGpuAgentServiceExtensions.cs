using LeXtudio.DevFlow.Agent.Core;
using Microsoft.Maui.DevFlow.Agent.Core;
using Microsoft.UI.Xaml;

namespace LeXtudio.DevFlow.Agent.ProGPU;

/// <summary>
/// Extension methods for integrating the DevFlow agent into ProGPU.WinUI applications.
/// </summary>
public static class ProGpuAgentServiceExtensions
{
    /// <summary>
    /// Starts the DevFlow agent HTTP server for this ProGPU application.
    /// The agent provides visual tree inspection, UI interaction, and diagnostics
    /// over a REST API on localhost (default port 9223).
    /// </summary>
    /// <param name="app">The ProGPU Application instance.</param>
    /// <param name="options">Optional agent configuration.</param>
    /// <returns>The running agent service instance.</returns>
    public static ProGpuAgentService AddProGpuDevFlowAgent(this Application app, AgentOptions? options = null)
    {
        options ??= new AgentOptions();
        DevFlowAgentPortResolver.ApplyDefaultPort(options);

        var service = new ProGpuAgentService(options);
        service.Start();

        // Log the agent startup
        var logger = app.GetType().Assembly
            .GetTypes()
            .FirstOrDefault(t => t.Name == "Program");
        System.Diagnostics.Debug.WriteLine(
            $"[DevFlow] ProGPU agent started on port {service.Port}. " +
            $"GET http://localhost:{service.Port}/api/v1/agent/status");

        return service;
    }
}
