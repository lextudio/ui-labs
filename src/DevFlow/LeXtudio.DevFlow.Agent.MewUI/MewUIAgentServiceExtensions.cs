using Aprillz.MewUI;
using LeXtudio.DevFlow.Agent.Core;
using Microsoft.Maui.DevFlow.Agent.Core;

namespace LeXtudio.DevFlow.Agent.MewUI;

public static class MewUIAgentServiceExtensions
{
    public static MewUIAgentService AddMewUIDevFlowAgent(this Application app, AgentOptions? options = null)
    {
        var service = new MewUIAgentService(options);
        service.Start();
        return service;
    }
}
