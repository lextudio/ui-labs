using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

namespace LeXtudio.DevFlow.Driver;

/// <summary>
/// Sends mutating DevFlow requests with the mutation lease the agent requires.
/// </summary>
/// <remarks>
/// <para>
/// Upstream made <c>AgentOptions.RequireMutationLease</c> default to true, so every POST, PUT and DELETE
/// must carry a lease acquired through <c>POST /api/v1/agent/lease</c>. Requests without one are rejected
/// with 409 and <c>reason: "lease"</c>.
/// </para>
/// <para>
/// <c>AgentClient</c> already acquires and renews the lease on its own. This helper exists for the code
/// paths that talk to an agent with a bare <see cref="HttpClient"/> — the CLI command handlers and the
/// hand-written <see cref="AgentClient"/>. Leases are cached per agent so a sequence of mutations claims
/// once and refreshes only as the lease nears expiry.
/// </para>
/// <para>
/// An agent that answers the lease endpoint with 404 predates mutation leases and is treated as allowing
/// every mutation, which keeps a mixed-version setup working.
/// </para>
/// </remarks>
public static class DevFlowMutation
{
    private const string LeaseEndpoint = "/api/v1/agent/lease";
    private const string LeaseHeader = "X-DevFlow-Lease";
    private const string HolderHeader = "X-DevFlow-Holder";
    private const string LabelHeader = "X-DevFlow-Label";

    private static readonly ConcurrentDictionary<string, LeaseState> Leases = new(StringComparer.Ordinal);

    public static string HolderKind { get; set; } = "cli";

    public static string? Label { get; set; }

    /// <summary>Sends a mutating request, acquiring or renewing the lease first when needed.</summary>
    public static async Task<HttpResponseMessage> SendAsync(
        HttpClient http,
        HttpMethod method,
        string requestUri,
        HttpContent? content = null,
        CancellationToken cancellationToken = default)
    {
        var lease = await AcquireAsync(http, requestUri, cancellationToken).ConfigureAwait(false);

        using var request = new HttpRequestMessage(method, requestUri);
        if (content is not null)
        {
            request.Content = content;
        }
        if (lease is not null)
        {
            request.Headers.Remove(LeaseHeader);
            request.Headers.TryAddWithoutValidation(LeaseHeader, lease.LeaseId);
            request.Headers.Remove(HolderHeader);
            request.Headers.TryAddWithoutValidation(HolderHeader, lease.HolderKind);
            if (!string.IsNullOrWhiteSpace(lease.Label))
            {
                request.Headers.Remove(LabelHeader);
                request.Headers.TryAddWithoutValidation(LabelHeader, lease.Label);
            }
        }

        return await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Blocking counterpart of <see cref="SendAsync"/>, for the CLI's synchronous handlers.</summary>
    public static HttpResponseMessage Send(
        HttpClient http,
        HttpMethod method,
        string requestUri,
        HttpContent? content = null)
        => SendAsync(http, method, requestUri, content).ConfigureAwait(false).GetAwaiter().GetResult();

    public static HttpResponseMessage Post(HttpClient http, string requestUri, HttpContent? content = null)
        => Send(http, HttpMethod.Post, requestUri, content);

    public static HttpResponseMessage Delete(HttpClient http, string requestUri)
        => Send(http, HttpMethod.Delete, requestUri);

    /// <summary>Drops any cached lease for an agent, so the next mutation claims a fresh one.</summary>
    public static void Reset(HttpClient http, string requestUri) => Leases.TryRemove(Age(http, requestUri), out _);

    private static async Task<LeaseState?> AcquireAsync(HttpClient http, string requestUri, CancellationToken cancellationToken)
    {
        var key = Age(http, requestUri);

        // The lease id is reused across mutations so the agent sees one continuous holder, but the claim
        // itself is always sent. Caching the claim instead would go stale whenever an agent restarts on
        // a recycled port, which is exactly what the test harnesses do, and the mutation would then be
        // rejected for a lease the new agent has never issued. Upstream's AgentClient likewise claims
        // before every non-GET request.
        var leaseId = Leases.TryGetValue(key, out var cached) ? cached.LeaseId : Guid.NewGuid().ToString("N");
        var payload = new Dictionary<string, object?>
        {
            ["action"] = "claim",
            ["leaseId"] = leaseId,
            ["holderKind"] = HolderKind,
            ["label"] = Label,
            ["force"] = false,
        };

        // Serialize before sending. JsonContent cannot precompute its length for a Dictionary, so
        // PostAsJsonAsync falls back to a chunked body, and the agent's HTTP server does not read chunked
        // request bodies: it fails to parse the payload, drops the connection, and the caller sees
        // "The response ended prematurely" instead of a lease. A StringContent always carries a
        // Content-Length and is understood.
        var json = JsonSerializer.Serialize(payload);

        HttpResponseMessage response;
        try
        {
            response = await http
                .PostAsync(
                    new Uri(Resolve(http, requestUri), LeaseEndpoint),
                    new StringContent(json, Encoding.UTF8, "application/json"),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            // The agent is unreachable; let the caller's request surface the real failure.
            return null;
        }

        using (response)
        {
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                Leases[key] = new LeaseState(leaseId, HolderKind, Label, TimeSpan.MaxValue);
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new DevFlowMutationLeaseException(
                    $"The DevFlow agent rejected the mutation lease request with HTTP {(int)response.StatusCode}.");
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var status = JsonDocument.Parse(body).RootElement;

            if (status.ValueKind == JsonValueKind.Object
                && status.TryGetProperty("youHold", out var youHold)
                && !youHold.GetBoolean())
            {
                var holder = status.TryGetProperty("label", out var label) ? label.GetString() : null;
                var detail = status.TryGetProperty("error", out var error) ? error.GetString() : null;
                throw new DevFlowMutationLeaseException(
                    detail ?? $"Another DevFlow session is driving this app{(holder is null ? string.Empty : $" ({holder})")}.");
            }

            var expiresInMs = status.ValueKind == JsonValueKind.Object
                && status.TryGetProperty("expiresInMs", out var expires)
                ? expires.GetInt64()
                : 10_000;

            var state = new LeaseState(leaseId, HolderKind, Label, TimeSpan.FromMilliseconds(expiresInMs));
            Leases[key] = state;
            return state;
        }
    }

    /// <summary>
    /// Resolves a possibly relative request URI against the client's base address.
    /// </summary>
    private static Uri Resolve(HttpClient http, string requestUri)
        => new Uri(http.BaseAddress ?? throw new InvalidOperationException("The DevFlow client needs a base address."), requestUri);

    /// <summary>Reduces a request URI to a stable per-agent key.</summary>
    private static string Age(HttpClient http, string requestUri)
    {
        var uri = Resolve(http, requestUri);
        return $"{uri.Scheme}://{uri.Host}:{uri.Port}";
    }

    private sealed record LeaseState(string LeaseId, string HolderKind, string? Label, TimeSpan ExpiresInMs);
}

/// <summary>Raised when the DevFlow agent refuses a mutation because the lease is held elsewhere.</summary>
public sealed class DevFlowMutationLeaseException : Exception
{
    public DevFlowMutationLeaseException(string message) : base(message)
    {
    }
}