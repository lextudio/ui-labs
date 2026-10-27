using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace LeXtudio.DevFlow.Agent.LibreWpf.LinuxInput.Tests;

/// <summary>
/// Real XTest input against a real LibreWPF window. The agent injects into the X server the app is
/// connected to, so these need a reachable DISPLAY (X11, Xvfb or XWayland); they skip everywhere else.
/// Left button only: the Linux path of press/drag-move/release rejects the other buttons.
/// </summary>
public class LibreWpfLinuxInputTests
{
    private static readonly bool HasXDisplay =
        OperatingSystem.IsLinux() && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY"));

    [Fact]
    public async Task Click_ReachesTheLibreWpfButton()
    {
        Assert.SkipUnless(HasXDisplay, "XTest needs a reachable X display (X11, Xvfb or XWayland).");

        var port = GetFreePort();
        await using var host = StartTestApp(port);
        using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
        await PollStatusAsync(client);

        var (cx, cy) = await GetButtonCenterAsync(client);
        var click = await PostAsync(client, "/api/v1/ui/actions/click", new { x = cx, y = cy, global = false });
        Assert.True(click.GetProperty("ok").GetBoolean(), click.ToString());

        await WaitForClickedAsync(client, null);
    }

    [Fact]
    public async Task PressDragMoveRelease_HoldsTheButtonAcrossRequests()
    {
        Assert.SkipUnless(HasXDisplay, "XTest needs a reachable X display (X11, Xvfb or XWayland).");

        var port = GetFreePort();
        await using var host = StartTestApp(port);
        using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
        await PollStatusAsync(client);

        // The native click response echoes the screen point it resolved the window point to, which is
        // the only reliable way to get screen coordinates without guessing the window origin.
        var (cx, cy) = await GetButtonCenterAsync(client);
        var first = await PostAsync(client, "/api/v1/ui/actions/click", new { x = cx, y = cy, global = false });
        Assert.True(first.GetProperty("ok").GetBoolean(), first.ToString());
        var screenX = first.GetProperty("x").GetDouble();
        var screenY = first.GetProperty("y").GetDouble();
        var before = await WaitForClickedAsync(client, null);

        // The text is a time of day with second resolution; leave room for it to change.
        await Task.Delay(1500);

        var press = await PostAsync(client, "/api/v1/ui/actions/press", new { x = screenX, y = screenY });
        Assert.True(press.GetProperty("ok").GetBoolean(), press.ToString());
        Assert.Equal("xtest", press.GetProperty("mode").GetString());

        var move = await PostAsync(client, "/api/v1/ui/actions/drag-move", new { x = screenX + 2, y = screenY + 1 });
        Assert.True(move.GetProperty("ok").GetBoolean(), move.ToString());
        Assert.Equal("xtest", move.GetProperty("mode").GetString());

        var release = await PostAsync(client, "/api/v1/ui/actions/release", new { x = screenX, y = screenY });
        Assert.True(release.GetProperty("ok").GetBoolean(), release.ToString());
        Assert.Equal("xtest", release.GetProperty("mode").GetString());

        // A held press, a move and a release over the button is a click to the app: only a button the
        // X server really kept down across the three requests can produce it.
        await WaitForClickedAsync(client, before);
    }

    private static async Task<(double X, double Y)> GetButtonCenterAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/api/v1/ui/elements/ActionButton");
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var bounds = doc.RootElement.GetProperty("bounds");
        return (
            bounds.GetProperty("x").GetDouble() + (bounds.GetProperty("width").GetDouble() / 2d),
            bounds.GetProperty("y").GetDouble() + (bounds.GetProperty("height").GetDouble() / 2d));
    }

    private static async Task<string> WaitForClickedAsync(HttpClient client, string previous)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        var text = string.Empty;
        while (DateTime.UtcNow < deadline)
        {
            using var response = await client.GetAsync("/api/v1/ui/elements/ResponseText");
            if (response.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                text = doc.RootElement.GetProperty("text").GetString() ?? string.Empty;
                if (text.StartsWith("Button clicked at", StringComparison.Ordinal) && text != previous)
                    return text;
            }

            await Task.Delay(200);
        }

        throw new Xunit.Sdk.XunitException($"The button never reported a (new) click; ResponseText is '{text}'.");
    }

    private static async Task<JsonElement> PostAsync(HttpClient client, string path, object body)
    {
        using var response = await LeXtudio.DevFlow.Driver.DevFlowMutation.SendAsync(
            client,
            HttpMethod.Post,
            path,
            new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
            TestContext.Current.CancellationToken);
        var raw = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{path} returned {(int)response.StatusCode}: {raw}");
        using var doc = JsonDocument.Parse(raw);
        return doc.RootElement.Clone();
    }

    private static async Task PollStatusAsync(HttpClient client)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(60);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var response = await client.GetAsync("/api/v1/agent/status");
                if (response.IsSuccessStatusCode)
                    return;
            }
            catch (HttpRequestException) { }
            catch (TaskCanceledException) { }

            await Task.Delay(250);
        }

        throw new InvalidOperationException("The LibreWPF test app's agent did not become reachable.");
    }

    private static TestApp StartTestApp(int port)
    {
        var projectDir = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "DevFlow", "LibreWpfDevFlowTestApp"));
        var csproj = Directory.GetFiles(projectDir, "*.csproj").Single();

        // The workflow builds the app first; --no-build keeps the first request from waiting on a compile.
        var psi = new ProcessStartInfo("dotnet", $"run --no-build --project \"{csproj}\" -c Debug")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.Environment["DEVFLOW_AGENT_PORT"] = port.ToString();
        var process = Process.Start(psi) ?? throw new InvalidOperationException("Could not start the test app.");
        // Drain the pipes so a chatty app cannot block on a full buffer.
        process.OutputDataReceived += (_, _) => { };
        process.ErrorDataReceived += (_, _) => { };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return new TestApp(process);
    }

    private static int GetFreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try { return ((IPEndPoint)listener.LocalEndpoint).Port; }
        finally { listener.Stop(); }
    }

    private sealed class TestApp : IAsyncDisposable
    {
        private readonly Process _process;
        public TestApp(Process process) => _process = process;

        public ValueTask DisposeAsync()
        {
            if (!_process.HasExited)
                _process.Kill(entireProcessTree: true);
            _process.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
