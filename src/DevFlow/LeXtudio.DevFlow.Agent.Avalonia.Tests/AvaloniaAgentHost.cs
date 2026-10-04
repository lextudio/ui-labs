using System.Diagnostics;
using System.Net.Sockets;
using System.Text.Json;
using Xunit;

namespace LeXtudio.DevFlow.Agent.Avalonia.Tests;

/// <summary>
/// Builds and starts AvaloniaDevFlowTestApp once for a test class and stops it afterwards. The tests
/// talk to the agent inside it over HTTP, like a DevFlow client does.
/// </summary>
public sealed class AvaloniaAgentHost : IAsyncLifetime
{
    private Process? _process;
    private readonly StringWriter _output = new();

    public int Port { get; } = GetFreePort();

    public HttpClient Client { get; private set; } = null!;

    /// <summary>Output of the test app, for failure messages.</summary>
    public string Output
    {
        get
        {
            lock (_output)
                return _output.ToString();
        }
    }

    public async ValueTask InitializeAsync()
    {
        var repoRoot = FindRepositoryRoot(Directory.GetCurrentDirectory());
        var projectPath = Path.Combine(repoRoot, "src", "DevFlow", "AvaloniaDevFlowTestApp", "AvaloniaDevFlowTestApp.csproj");
        if (!File.Exists(projectPath))
            throw new InvalidOperationException($"Unable to locate the Avalonia test app at {projectPath}");

        var projectDirectory = Path.GetDirectoryName(projectPath)!;
        if (!RunCommand("dotnet", $"build \"{projectPath}\" -c Debug", projectDirectory, out var buildOutput, out var buildError))
            throw new InvalidOperationException($"Failed to build the Avalonia test app:\n{buildError}\n{buildOutput}");

        var appPath = Path.Combine(projectDirectory, "bin", "Debug", "net10.0", "AvaloniaDevFlowTestApp.dll");
        if (!File.Exists(appPath))
            throw new InvalidOperationException($"Unable to locate the Avalonia test app at {appPath}");

        var startInfo = new ProcessStartInfo("dotnet", $"\"{appPath}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = projectDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.Environment["DEVFLOW_AGENT_PORT"] = Port.ToString();

        _process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start the Avalonia test app.");
        _process.OutputDataReceived += (_, e) => Append(e.Data);
        _process.ErrorDataReceived += (_, e) => Append(e.Data);
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();

        Client = new HttpClient { BaseAddress = new Uri($"http://localhost:{Port}") };
        await PollAgentStatusAsync(TimeSpan.FromSeconds(60));
    }

    public ValueTask DisposeAsync()
    {
        Client?.Dispose();
        if (_process != null)
        {
            if (!_process.HasExited)
            {
                _process.Kill(true);
                _process.WaitForExit(5000);
            }

            _process.Dispose();
        }

        return ValueTask.CompletedTask;
    }

    private void Append(string? line)
    {
        if (line == null)
            return;

        lock (_output)
            _output.WriteLine(line);
    }

    private async Task PollAgentStatusAsync(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (_process!.HasExited)
                throw new InvalidOperationException($"The Avalonia test app exited with code {_process.ExitCode}:\n{Output}");

            try
            {
                using var response = await Client.GetAsync("/api/v1/agent/status");
                if (response.IsSuccessStatusCode)
                {
                    using var doc = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
                    if (doc.RootElement.TryGetProperty("running", out var running) && running.GetBoolean())
                        return;
                }
            }
            catch (HttpRequestException)
            {
            }
            catch (TaskCanceledException)
            {
            }

            await Task.Delay(250);
        }

        throw new InvalidOperationException($"The agent in the Avalonia test app did not answer in time:\n{Output}");
    }

    private static int GetFreePort()
    {
        using var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        return ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static string FindRepositoryRoot(string startFolder)
    {
        var current = new DirectoryInfo(startFolder);
        while (current != null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "src", "DevFlow")))
                return current.FullName;

            current = current.Parent;
        }

        throw new InvalidOperationException("Unable to locate the repository root containing src/DevFlow.");
    }

    private static bool RunCommand(string command, string arguments, string workingDirectory, out string output, out string error)
    {
        using var process = new Process();
        process.StartInfo.FileName = command;
        process.StartInfo.Arguments = arguments;
        process.StartInfo.WorkingDirectory = workingDirectory;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.CreateNoWindow = true;
        process.StartInfo.UseShellExecute = false;

        var stdout = new StringWriter();
        var stderr = new StringWriter();
        process.OutputDataReceived += (_, e) => { if (e.Data != null) stdout.WriteLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) stderr.WriteLine(e.Data); };

        if (!process.Start())
        {
            output = string.Empty;
            error = $"Failed to start {command}";
            return false;
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        process.WaitForExit();

        output = stdout.ToString();
        error = stderr.ToString();
        return process.ExitCode == 0;
    }
}
