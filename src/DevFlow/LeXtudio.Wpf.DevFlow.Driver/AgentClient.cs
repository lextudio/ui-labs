using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LeXtudio.Wpf.DevFlow.Driver;

public sealed class AgentClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public AgentClient(string host = "localhost", int port = 5500)
    {
        _baseUrl = $"http://{host}:{port}";
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    }

    public string BaseUrl => _baseUrl;

    public async Task<AgentStatus?> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        return await GetAsync<AgentStatus>("/api/v1/agent/status", cancellationToken).ConfigureAwait(false);
    }

    public async Task<JsonElement?> GetTreeAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _http.GetAsync(new Uri(_baseUrl + "/api/v1/ui/tree"), cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken).ConfigureAwait(false);
        return document.RootElement.Clone();
    }

    public async Task<JsonElement?> GetElementAsync(string elementId, CancellationToken cancellationToken = default)
    {
        var url = new Uri(_baseUrl + $"/api/v1/ui/element?id={Uri.EscapeDataString(elementId)}");
        using var response = await _http.GetAsync(url, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken).ConfigureAwait(false);
        return document.RootElement.Clone();
    }

    public async Task<bool> TapAsync(string elementId, CancellationToken cancellationToken = default)
    {
        var content = JsonContent.Create(new { id = elementId }, options: _jsonOptions);
        using var response = await _http.PostAsync(new Uri(_baseUrl + "/api/v1/ui/tap"), content, cancellationToken).ConfigureAwait(false);
        return response.IsSuccessStatusCode;
    }

    public void Dispose() => _http.Dispose();

    private async Task<T?> GetAsync<T>(string path, CancellationToken cancellationToken)
    {
        var response = await _http.GetAsync(new Uri(_baseUrl + path), cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(_jsonOptions, cancellationToken).ConfigureAwait(false);
    }
}
