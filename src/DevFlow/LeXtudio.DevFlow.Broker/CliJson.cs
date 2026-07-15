using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Microsoft.Maui.Cli.DevFlow;

internal static class CliJson
{
    public static T? Deserialize<T>(string json) where T : class
        => JsonSerializer.Deserialize<T>(json);

    public static string SerializeUntyped(object? value, bool indented = true)
    {
        if (value is null)
            return "null";

        return value switch
        {
            JsonNode node => node.ToJsonString(new JsonSerializerOptions { WriteIndented = indented }),
            JsonElement element => PrettyPrint(element, indented),
            JsonDocument document => PrettyPrint(document.RootElement, indented),
            _ => JsonSerializer.Serialize(value, value.GetType(), new JsonSerializerOptions { WriteIndented = indented }),
        };
    }

    public static JsonElement ParseElement(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    public static string PrettyPrint(string json)
    {
        using var document = JsonDocument.Parse(json);
        return PrettyPrint(document.RootElement, indented: true);
    }

    public static string PrettyPrint(JsonElement element, bool indented = true)
    {
        if (!indented)
            return element.GetRawText();

        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
        element.WriteTo(writer);
        writer.Flush();
        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
