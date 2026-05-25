namespace LeXtudio.DevFlow.Agent.Core;

public static class SimulationModes
{
    public const string Native = "native";
    public const string Semantic = "semantic";
    public const string Reflection = "reflection";
    public const string PropertyMutation = "property-mutation";
}

public sealed class ActionSimulationResult
{
    public bool Success { get; init; } = true;
    public string? SimulationMode { get; init; }
    public string? ElementId { get; init; }
    public string? Key { get; init; }
    public string? Text { get; init; }
    public double? DeltaX { get; init; }
    public double? DeltaY { get; init; }
}
