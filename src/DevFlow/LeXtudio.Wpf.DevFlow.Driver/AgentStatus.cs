using System.Text.Json.Serialization;

namespace LeXtudio.Wpf.DevFlow.Driver;

public sealed class AgentStatus
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("framework")]
    public string? Framework { get; set; }

    [JsonPropertyName("version")]
    public string? Version { get; set; }

    [JsonPropertyName("running")]
    public bool Running { get; set; }

    [JsonPropertyName("port")]
    public int Port { get; set; }

    [JsonPropertyName("application")]
    public string? Application { get; set; }
}
