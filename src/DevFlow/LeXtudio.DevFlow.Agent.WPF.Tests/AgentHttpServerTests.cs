using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Maui.DevFlow.Agent.Core;
using Xunit;

namespace LeXtudio.DevFlow.Agent.WPF.Tests;

public class AgentHttpServerTests
{
    [Fact]
    public async Task Start_WhenRequestedPortIsTaken_BindsFallbackPort()
    {
        using var occupiedPort = new TcpListener(IPAddress.Loopback, 0);
        occupiedPort.Start();
        var requestedPort = ((IPEndPoint)occupiedPort.LocalEndpoint).Port;

        using var server = new AgentHttpServer(requestedPort);
        server.MapGet("/api/v1/agent/status", _ => Task.FromResult(HttpResponse.Json(new
        {
            running = true,
            port = server.Port
        })));

        server.Start();
        try
        {
            Assert.True(server.IsRunning);
            Assert.NotEqual(requestedPort, server.Port);

            using var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{server.Port}") };
            using var response = await client.GetAsync("/api/v1/agent/status", TestContext.Current.CancellationToken);
            response.EnsureSuccessStatusCode();

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken));
            Assert.True(doc.RootElement.GetProperty("running").GetBoolean());
            Assert.Equal(server.Port, doc.RootElement.GetProperty("port").GetInt32());
        }
        finally
        {
            await server.StopAsync();
        }
    }
}
