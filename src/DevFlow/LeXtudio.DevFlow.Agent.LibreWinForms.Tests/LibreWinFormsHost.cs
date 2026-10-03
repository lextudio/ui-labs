using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Xunit;

namespace LeXtudio.DevFlow.Agent.LibreWinForms.Tests;

/// <summary>
/// Builds and launches <c>LibreWinFormsDevFlowTestApp</c>, then exposes the DevFlow HTTP surface for
/// assertions.
/// </summary>
/// <remarks>
/// <para>
/// The host app targets <c>net11.0</c> because <c>LibreWinForms.Sdk</c> 0.1.0-preview.57 and later
/// require it, while the agent library under test stays on <c>net10.0</c>. These tests therefore run on
/// <c>net10.0</c> but need a .NET 11 SDK on the machine to build the host; when none is found the tests
/// skip rather than fail.
/// </para>
/// <para>
/// Unlike the WPF and WinForms agent tests, this suite is not Windows-only: LibreWinForms renders through
/// ProGPU/Silk.NET, so the same assertions run on macOS and Linux.
/// </para>
/// </remarks>
internal sealed class LibreWinFormsHost : IAsyncDisposable
{
    private const string AppProjectName = "LibreWinFormsDevFlowTestApp";
    private const string AppAssemblyName = "LibreWinFormsDevFlowTestApp";
    private const string HostTargetFramework = "net11.0";

    private static readonly object BuildGate = new();
    private static string? _buildFailure;
    private static string? _skipReason;
    private static string? _dotnetHost;

    private readonly Process _process;
    private readonly System.Text.StringBuilder _output = new();

    private LibreWinFormsHost(Process process, int port)
    {
        _process = process;
        Port = port;
    }

    public int Port { get; }

    public string CapturedOutput => _output.ToString();

    /// <summary>
    /// Non-null when this machine cannot run the suite. Resolved once and cached, so tests can check it
    /// before starting a host process.
    /// </summary>
    public static string? SkipReason
    {
        get
        {
            EnsureHostResolved();
            return _skipReason;
        }
    }

    /// <summary>
    /// The dotnet host that carries a .NET 11 SDK. The host app targets net11.0, so every build and
    /// launch must go through this host rather than whatever <c>dotnet</c> happens to be first on PATH.
    /// </summary>
    private static string DotnetHost => _dotnetHost ?? throw new InvalidOperationException(_skipReason ?? "No .NET 11 SDK.");

    public static async Task<LibreWinFormsHost> StartAsync(CancellationToken cancellationToken)
    {
        var skipReason = SkipReason;
        if (skipReason is not null)
        {
            throw new InvalidOperationException(skipReason);
        }

        var repoRoot = FindRepositoryRoot(AppContext.BaseDirectory);
        var projectPath = Path.Combine(repoRoot, "src", "DevFlow", AppProjectName, $"{AppProjectName}.csproj");
        EnsureBuilt(projectPath, Path.GetDirectoryName(projectPath)!);

        var appDirectory = Path.Combine(
            Path.GetDirectoryName(projectPath)!, "bin", "Debug", HostTargetFramework);

        var appAssembly = Path.Combine(appDirectory, $"{AppAssemblyName}.dll");
        AssertFileExists(appAssembly);

        var port = GetFreePort();

        // Launch through the resolved dotnet host rather than the native apphost: the app targets net11.0,
        // and on a machine with several dotnet installations the apphost can resolve a different
        // installation than the one that built it. Going through the host removes that ambiguity.
        var startInfo = new ProcessStartInfo(DotnetHost, $"exec \"{appAssembly}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = appDirectory,
        };
        startInfo.Environment["DEVFLOW_AGENT_PORT"] = port.ToString();

        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start the LibreWinForms host app.");

        var host = new LibreWinFormsHost(process, port);
        host.BeginDrainingOutput();

        try
        {
            using var client = CreateClient(port);
            await WaitForAgentAsync(client, TimeSpan.FromSeconds(60), cancellationToken);
        }
        catch (Exception ex)
        {
            var detail = $"Host output:\n{host.CapturedOutput}";
            await host.DisposeAsync();

            // The app is windowed and cannot start where Silk.NET finds no usable platform, which is the
            // case on a headless Linux runner even with Xvfb and the GL libraries present. That is an
            // environment limitation rather than a defect, so report it as a skip instead of a failure.
            if (host.CapturedOutput.Contains("Couldn't find a suitable window platform", StringComparison.Ordinal))
            {
                Assert.Skip("The LibreWinForms app needs a window platform that Silk.NET could not use on this machine.");
            }

            throw new InvalidOperationException($"{ex.Message}\n{detail}", ex);
        }

        return host;
    }

    /// <summary>
    /// Drains the host's stdout/stderr continuously. Without this the pipes fill and the app blocks.
    /// </summary>
    private void BeginDrainingOutput()
    {
        _ = Task.Run(() =>
        {
            try
            {
                _process.OutputDataReceived += (_, e) => AppendLine(e.Data);
                _process.ErrorDataReceived += (_, e) => AppendLine(e.Data);
                _process.BeginOutputReadLine();
                _process.BeginErrorReadLine();
            }
            catch (InvalidOperationException)
            {
                // The process exited before the handlers attached.
            }
        });
    }

    private void AppendLine(string? line)
    {
        if (line is null)
        {
            return;
        }

        lock (_output)
        {
            _output.AppendLine(line);
        }
    }

    public HttpClient CreateClient() => CreateClient(Port);

    private static HttpClient CreateClient(int port)
        => new() { BaseAddress = new Uri($"http://localhost:{port}"), Timeout = TimeSpan.FromSeconds(30) };

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                await _process.WaitForExitAsync();
            }
        }
        catch (InvalidOperationException)
        {
            // The process already exited.
        }
        finally
        {
            _process.Dispose();
        }
    }

    private static void EnsureBuilt(string projectPath, string workingDirectory)
    {
        lock (BuildGate)
        {
            if (_buildFailure is not null)
            {
                throw new InvalidOperationException(_buildFailure);
            }

            var startInfo = new ProcessStartInfo(DotnetHost, $"build \"{projectPath}\" -c Debug")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = workingDirectory,
            };

            using var process = Process.Start(startInfo)!;
            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                _buildFailure = $"Failed to build {AppProjectName} (exit {process.ExitCode}):\n{error}\n{output}";
                throw new InvalidOperationException(_buildFailure);
            }
        }
    }

    /// <summary>
    /// Waits until an element shows up in the agent's tree.
    /// </summary>
    /// <remarks>
    /// The agent starts inside the <c>ApplicationContext</c> constructor, before the main form is shown,
    /// so <c>Application.OpenForms</c> can still be empty when the agent first answers. Interaction tests
    /// must wait for their target element instead of assuming the tree is populated.
    /// </remarks>
    public static async Task WaitForElementAsync(HttpClient client, string elementId, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var response = await client.GetAsync(
                    $"/api/v1/ui/elements/{Uri.EscapeDataString(elementId)}", cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch (HttpRequestException)
            {
            }
            catch (TaskCanceledException)
            {
            }

            await Task.Delay(200, cancellationToken);
        }

        throw new InvalidOperationException($"Element '{elementId}' did not appear in the agent tree in time.");
    }

    private static async Task WaitForAgentAsync(HttpClient client, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var response = await client.GetAsync("/api/v1/agent/status", cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch (HttpRequestException)
            {
            }
            catch (TaskCanceledException)
            {
            }

            await Task.Delay(250, cancellationToken);
        }

        throw new InvalidOperationException("The LibreWinForms agent did not become available in time.");
    }

    private static void EnsureHostResolved()
    {
        if (_skipReason is not null || _dotnetHost is not null)
        {
            return;
        }

        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS() && !OperatingSystem.IsLinux())
        {
            _skipReason = $"LibreWinForms does not support {RuntimeInformation.OSDescription}.";
            return;
        }

        _dotnetHost = FindDotnetHost();
        if (_dotnetHost is null)
        {
            _skipReason = "No dotnet host with a .NET 11 SDK was found; LibreWinFormsDevFlowTestApp targets net11.0.";
        }
    }

    private static string? FindDotnetHost()
    {
        foreach (var candidate in EnumerateDotnetCandidates())
        {
            if (!File.Exists(candidate))
            {
                continue;
            }

            if (HasNet11Sdk(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static IEnumerable<string> EnumerateDotnetCandidates()
    {
        if (Environment.GetEnvironmentVariable("DOTNET_ROOT") is { Length: > 0 } dotnetRoot)
        {
            yield return Path.Combine(dotnetRoot, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
        }

        var path = Environment.GetEnvironmentVariable("PATH");
        if (path is not null)
        {
            foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                yield return Path.Combine(directory, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
            }
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        yield return Path.Combine(home, ".dotnet", "dotnet");
        yield return "/usr/local/share/dotnet/dotnet";
        yield return "/opt/homebrew/bin/dotnet";
        yield return "/usr/share/dotnet/dotnet";
    }

    private static bool HasNet11Sdk(string dotnetPath)
    {
        try
        {
            var startInfo = new ProcessStartInfo(dotnetPath, "--list-sdks")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return false;
            }

            var output = process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();
            process.WaitForExit();

            return process.ExitCode == 0
                && output.Split('\n').Any(line => line.TrimStart().StartsWith("11.", StringComparison.Ordinal));
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static int GetFreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    private static void AssertFileExists(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"The LibreWinForms host app was not produced at '{path}'. Build {AppProjectName} manually to see why.",
                path);
        }
    }

    private static string FindRepositoryRoot(string startFolder)
    {
        var current = new DirectoryInfo(startFolder);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "src", "DevFlow")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Unable to locate the repository root from " + startFolder);
    }
}

/// <summary>
/// Shared helpers for reading DevFlow responses in assertions.
/// </summary>
internal static class DevFlowAssert
{
    /// <summary>
    /// Reads an endpoint whose payload is a bare JSON array, such as <c>/api/v1/ui/tree</c>.
    /// </summary>
    /// <remarks>
    /// The tree endpoint answers with a bare array by default and only wraps it in an object when
    /// <c>envelope=true</c> is passed, which is the shape the shared protocol client expects.
    /// </remarks>
    public static async Task<JsonElement> GetJsonArrayAsync(HttpClient client, string path, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(path, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);
        return document.RootElement.Clone();
    }

    public static async Task<JsonElement> GetJsonAsync(HttpClient client, string path, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(path, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);
        return document.RootElement.Clone();
    }

    public static async Task<JsonElement> PostJsonAsync(HttpClient client, string path, string body, CancellationToken cancellationToken)
    {
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var response = await LeXtudio.DevFlow.Driver.DevFlowMutation.SendAsync(
            client, HttpMethod.Post, path, content, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);
        return document.RootElement.Clone();
    }

    public static async Task<string> GetTextAsync(HttpClient client, string elementId, CancellationToken cancellationToken)
    {
        var element = await GetJsonAsync(client, $"/api/v1/ui/elements/{Uri.EscapeDataString(elementId)}", cancellationToken);
        return element.GetProperty("text").GetString() ?? string.Empty;
    }

    public static bool IsPng(byte[] bytes)
    {
        byte[] header = [137, 80, 78, 71, 13, 10, 26, 10];
        return bytes.Length >= header.Length && bytes.Take(header.Length).SequenceEqual(header);
    }
}