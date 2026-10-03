using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Maui.DevFlow.Driver;

namespace Microsoft.Maui.Cli.DevFlow.Inspector;

/// <summary>
/// Result shape for the inspector's alert endpoints. Mirrors the upstream record so the browser UI and
/// <c>InspectorServer</c> serialize the same payload.
/// </summary>
internal sealed record InspectorAlertResult(
    bool Ok,
    bool Supported,
    AlertInfo? Alert = null,
    string? Error = null,
    bool Dismissed = false);

/// <summary>
/// Inspector-side alert detection and dismissal for LeXtudio agents.
/// </summary>
/// <remarks>
/// <para>
/// Upstream's <c>InspectorAlertController</c> drives the target through
/// <c>Microsoft.Maui.DevFlow.Driver</c> app drivers so it can reach a device over ADB or a remote
/// simulator. That requires the Android/iOS/macOS CLI infrastructure (<c>AndroidAppDriver</c>,
/// <c>MacCatalystAppDriver</c>, <c>Xamarin.Android.Tools</c>, the <c>Flows</c> namespace), none of which a
/// desktop agent needs: every LeXtudio agent runs in-process on the same machine as the browser.
/// </para>
/// <para>
/// So this implementation proxies to the agent's own <c>/api/v1/alert/detect</c> and
/// <c>/api/v1/alert/dismiss</c> endpoints, which is what actually backs the agent-side alert detection
/// (<c>WindowsAlertDetector</c>). The inspector's alert buttons therefore stay functional instead of
/// degrading to a stub.
/// </para>
/// </remarks>
internal sealed class InspectorAlertController
{
    private static readonly JsonSerializerOptions CamelCase = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly string _agentHost;
    private readonly int _agentPort;

    public InspectorAlertController(
        string agentHost,
        int agentPort,
        string? appName,
        string? platform)
    {
        _agentHost = agentHost;
        _agentPort = agentPort;
    }

    public async Task<InspectorAlertResult> DetectAsync()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            using var response = await http
                .GetAsync($"http://{_agentHost}:{_agentPort}/api/v1/alert/detect")
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return new InspectorAlertResult(false, false, Error: $"Agent returned {(int)response.StatusCode}.");
            }

            var payload = await response.Content
                .ReadFromJsonAsync<JsonElement>(CamelCase)
                .ConfigureAwait(false);

            if (payload is not { ValueKind: JsonValueKind.Object } json)
            {
                return new InspectorAlertResult(false, false, Error: "Agent returned an unreadable alert payload.");
            }

            var present = json.TryGetProperty("present", out var presentValue) && presentValue.GetBoolean();
            if (!present)
            {
                return new InspectorAlertResult(true, true, Alert: new AlertInfo(null, Array.Empty<AlertButton>()));
            }

            var message = json.TryGetProperty("message", out var messageValue)
                ? messageValue.GetString()
                : null;

            // The desktop agents report button labels only, while upstream's AlertButton carries click
            // coordinates used by its device drivers. Dismissal here goes through the agent's label-based
            // /alert/dismiss endpoint, so coordinates stay at zero.
            var buttons = json.TryGetProperty("buttons", out var buttonsValue) && buttonsValue.ValueKind == JsonValueKind.Array
                ? buttonsValue.EnumerateArray()
                    .Select(button => button.ValueKind == JsonValueKind.String
                        ? button.GetString()
                        : button.TryGetProperty("text", out var text) ? text.GetString() : null)
                    .Where(label => !string.IsNullOrWhiteSpace(label))
                    .Select(label => new AlertButton(label!, 0, 0, 0, 0))
                    .ToList()
                : new List<AlertButton>();

            return new InspectorAlertResult(true, true, new AlertInfo(message, buttons));
        }
        catch (Exception ex)
        {
            return new InspectorAlertResult(false, false, Error: ex.Message);
        }
    }

    public async Task<InspectorAlertResult> DismissAsync(string? buttonLabel)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };

            // Serialize before sending rather than using PostAsJsonAsync. JsonContent cannot always
            // precompute its length, so PostAsJsonAsync may fall back to a chunked body, which the agent's
            // HTTP server does not read: it fails to parse the payload and drops the connection without a
            // response. A StringContent always carries a Content-Length. The request is also routed through
            // DevFlowMutation because dismissing an alert is a mutation and the agent requires a lease.
            var body = JsonSerializer.Serialize(new { buttonLabel }, CamelCase);
            using var response = await LeXtudio.DevFlow.Driver.DevFlowMutation.SendAsync(
                    http,
                    HttpMethod.Post,
                    $"http://{_agentHost}:{_agentPort}/api/v1/alert/dismiss",
                    new StringContent(body, Encoding.UTF8, "application/json"))
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return new InspectorAlertResult(false, false, Error: $"Agent returned {(int)response.StatusCode}.");
            }

            return new InspectorAlertResult(true, true, Dismissed: true);
        }
        catch (Exception ex)
        {
            return new InspectorAlertResult(false, false, Error: ex.Message);
        }
    }
}