namespace LeXtudio.DevFlow.Agent.Core;

public static class ActionSimulationExecutor
{
    public static ActionSimulationResult? Execute(params Func<ActionSimulationResult?>[] strategies)
    {
        foreach (var strategy in strategies)
        {
            var result = strategy();
            if (result != null)
                return result;
        }

        return null;
    }
}
