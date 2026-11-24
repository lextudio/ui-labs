using System.Net;
using System.Text;
using Microsoft.Maui.DevFlow.Agent.Core;
using Xunit;

namespace LeXtudio.DevFlow.Agent.Core.Tests;

/// <summary>
/// The mutation admission gate of the upstream <c>AgentHttpServer</c>: mutating routes run one at a time and a second one
/// that waits too long gets a retryable 503. <c>SerializeMutations</c> (a fork patch carried for OpenDevelop's test child) turns the
/// gate off for a host that also disabled leases, so a request can supply the input a pending modal command waits for.
/// </summary>
public class AgentHttpServerMutationTests
{
    private static async Task<(HttpStatusCode Second, int Calls, string Body)> RunTwoMutationsAsync(bool serialize)
    {
        using var server = new AgentHttpServer(0)
        {
            MutationAdmissionTimeout = TimeSpan.FromMilliseconds(250),
            SerializeMutations = serialize,
        };
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        server.MutationLeaseValidator = _ => Task.FromResult(new MutationLeaseStatus { Ok = true, Allowed = true, YouHold = true });
        server.MapPost("/api/v1/ui/mutate", async _ =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                firstEntered.TrySetResult();
                await releaseFirst.Task;
            }
            return HttpResponse.Json(new { ok = true });
        });
        server.Start();
        try
        {
            using var client = new HttpClient();
            var url = $"http://localhost:{server.Port}/api/v1/ui/mutate";
            var first = client.PostAsync(url, new StringContent("{}", Encoding.UTF8, "application/json"));
            await firstEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

            using var second = await client.PostAsync(url, new StringContent("{}", Encoding.UTF8, "application/json"));
            var body = await second.Content.ReadAsStringAsync();
            var secondCalls = Volatile.Read(ref calls);

            releaseFirst.TrySetResult();
            Assert.True((await first).IsSuccessStatusCode);
            return (second.StatusCode, secondCalls, body);
        }
        finally
        {
            releaseFirst.TrySetResult();
            await server.StopAsync();
        }
    }

    [Fact]
    public async Task ByDefault_ASecondMutationWaitsBehindThePendingOne_AndGetsARetryableBusy()
    {
        var (second, calls, body) = await RunTwoMutationsAsync(serialize: true);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, second);
        Assert.Contains("\"reason\":\"ui-mutation-busy\"", body);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task WithSerializeMutationsOff_ASecondMutationRunsWhileTheFirstIsPending()
    {
        var (second, calls, _) = await RunTwoMutationsAsync(serialize: false);

        Assert.Equal(HttpStatusCode.OK, second);
        Assert.Equal(2, calls);
    }

    private static bool ServerSerializes(AgentOptions options)
    {
        using var service = new StubAgentService([], options);
        for (var type = typeof(DevFlowAgentServiceBase); type is not null; type = type.BaseType)
        {
            if (type.GetField("_server", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic) is { } field)
            {
                return ((AgentHttpServer)field.GetValue(service)!).SerializeMutations;
            }
        }
        throw new InvalidOperationException("The agent service has no _server field.");
    }

    [Fact]
    public void TheOption_ReachesTheServer_AndNeitherLeaseOptionChangesIt()
    {
        // The plumbing in DevFlowAgentService: AgentOptions.SerializeMutations -> AgentHttpServer.SerializeMutations. Turning the lease off alone
        // (Agent.AotSmoke, OpenDevelop before the opt-out) must keep serialization on.
        Assert.True(ServerSerializes(new AgentOptions { Port = AgentTestHarness.GetFreePort() }));
        Assert.True(ServerSerializes(new AgentOptions { Port = AgentTestHarness.GetFreePort(), RequireMutationLease = false }));
        Assert.False(ServerSerializes(new AgentOptions { Port = AgentTestHarness.GetFreePort(), RequireMutationLease = false, SerializeMutations = false }));
    }

    [Fact]
    public void AgentOptions_SerializeMutations_DefaultsToTrue()
    {
        Assert.True(new AgentOptions().SerializeMutations);
    }
}
