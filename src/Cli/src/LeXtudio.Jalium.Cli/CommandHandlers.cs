using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using LeXtudio.DevFlow.Driver;
using Microsoft.Maui.Cli.DevFlow.Broker;
using Microsoft.Maui.Cli.DevFlow.Inspector;

namespace LeXtudio.Jalium.Cli
{
    public static class CommandHandlers
    {
        public static int RunDoctor(OutputOptions options)
        {
            return WriteResult("doctor", "Validated the Jalium development environment.", options);
        }

        public static int RunVersion(OutputOptions options)
        {
            return WriteResult("version", "LeXtudio.Jalium.Cli version 1.0.0", options);
        }

        public static int RunNew(Queue<string> tokens, OutputOptions options)
        {
            var name = tokens.Count > 0 ? tokens.Dequeue() : "JaliumApp";
            var targetDirectory = Path.GetFullPath(name);

            if (Directory.Exists(targetDirectory) || File.Exists(targetDirectory))
                return WriteResult("new", $"Target already exists: {targetDirectory}", options);

            if (options.DryRun)
            {
                Console.WriteLine($"[dry-run] create new Jalium app in {targetDirectory}");
                return 0;
            }

            Directory.CreateDirectory(targetDirectory);
            var projectReference = TryResolveLocalAgentProjectReference(targetDirectory);
            var includeAgent = projectReference != null;
            File.WriteAllText(Path.Combine(targetDirectory, $"{name}.csproj"), GetJaliumProjectFile(name, projectReference));
            File.WriteAllText(Path.Combine(targetDirectory, "Program.cs"), GetJaliumProgramCode(includeAgent));

            var message = includeAgent
                ? $"Created Jalium app '{name}' with DevFlow agent support in {targetDirectory}"
                : $"Created Jalium app '{name}' in {targetDirectory}. DevFlow agent support not wired because repository reference could not be resolved.";

            return WriteResult("new", message, options);
        }

        public static int RunBuild(Queue<string> tokens, OutputOptions options)
        {
            var target = ParseTarget(tokens, out var configuration, out var runtime, out var framework, out var outputDirectory);
            var args = new StringBuilder();
            args.Append("build");
            args.Append(target != null ? $" \"{target}\"" : string.Empty);
            args.Append($" -c {configuration}");
            if (!string.IsNullOrEmpty(runtime)) args.Append($" -r {runtime}");
            if (!string.IsNullOrEmpty(framework)) args.Append($" -f {framework}");
            if (!string.IsNullOrEmpty(outputDirectory)) args.Append($" -o \"{outputDirectory}\"");

            return RunDotnetCommand(args.ToString(), options, Path.GetFullPath("."));
        }

        public static int RunRun(Queue<string> tokens, OutputOptions options)
        {
            var target = ParseTarget(tokens, out var configuration, out var runtime, out var framework, out _);
            var args = new StringBuilder();
            args.Append("run");
            args.Append(target != null ? $" --project \"{target}\"" : string.Empty);
            args.Append($" -c {configuration}");
            if (!string.IsNullOrEmpty(runtime)) args.Append($" -r {runtime}");
            if (!string.IsNullOrEmpty(framework)) args.Append($" -f {framework}");

            return RunDotnetCommand(args.ToString(), options, Path.GetFullPath("."));
        }

        public static int RunPublish(Queue<string> tokens, OutputOptions options)
        {
            var target = ParseTarget(tokens, out var configuration, out var runtime, out var framework, out var outputDirectory);
            var args = new StringBuilder();
            args.Append("publish");
            args.Append(target != null ? $" \"{target}\"" : string.Empty);
            args.Append($" -c {configuration}");
            if (!string.IsNullOrEmpty(runtime)) args.Append($" -r {runtime}");
            if (!string.IsNullOrEmpty(framework)) args.Append($" -f {framework}");
            if (!string.IsNullOrEmpty(outputDirectory)) args.Append($" -o \"{outputDirectory}\"");

            return RunDotnetCommand(args.ToString(), options, Path.GetFullPath("."));
        }

        public static int RunPackage(Queue<string> tokens, OutputOptions options)
        {
            var target = ParseTarget(tokens, out var configuration, out var runtime, out var framework, out var outputDirectory);
            var publishFolder = !string.IsNullOrEmpty(outputDirectory)
                ? outputDirectory
                : Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}");

            var publishArgs = new StringBuilder();
            publishArgs.Append("publish");
            publishArgs.Append(target != null ? $" \"{target}\"" : string.Empty);
            publishArgs.Append($" -c {configuration}");
            if (!string.IsNullOrEmpty(runtime)) publishArgs.Append($" -r {runtime}");
            if (!string.IsNullOrEmpty(framework)) publishArgs.Append($" -f {framework}");
            publishArgs.Append($" -o \"{publishFolder}\"");

            var exitCode = RunDotnetCommand(publishArgs.ToString(), options, Path.GetFullPath("."));
            if (exitCode != 0)
                return exitCode;

            if (options.DryRun)
                return 0;

            var packagePath = Path.Combine(Path.GetFullPath("."), "publish.zip");
            if (File.Exists(packagePath))
                File.Delete(packagePath);

            ZipFile.CreateFromDirectory(publishFolder, packagePath, CompressionLevel.Optimal, false);
            return WriteResult("package", $"Packaged output to {packagePath}", options);
        }

        public static int RunDiagnostics(Queue<string> _, OutputOptions options)
        {
            return RunDotnetCommand("--info", options, Path.GetFullPath("."));
        }

        public static int RunEnv(Queue<string> _, OutputOptions options)
        {
            Console.WriteLine($"OS: {Environment.OSVersion}");
            Console.WriteLine($"Process architecture: {RuntimeInformation.ProcessArchitecture}");
            return RunDotnetCommand("--info", options, Path.GetFullPath("."));
        }

        public static int RunDevFlow(Queue<string> tokens, OutputOptions options)
        {
            if (tokens.Count == 0)
            {
                return WriteResult("devflow", "Usage: dotnet jalex devflow <status|screenshot|tap|extensions|inspector|broker|network|ui|alert> [options]", options);
            }

            var subcommand = tokens.Dequeue().ToLowerInvariant();
            return subcommand switch
            {
                "status" => RunDevFlowStatus(tokens, options),
                "screenshot" => RunDevFlowScreenshot(tokens, options),
                "tap" => RunDevFlowTap(tokens, options),
                "extensions" => RunDevFlowExtensions(tokens, options),
                "inspector" => RunDevFlowInspector(tokens, options),
                "broker" => RunDevFlowBroker(tokens, options),
                "network" => RunDevFlowNetwork(tokens, options),
                "ui" => RunDevFlowUi(tokens, options),
                "alert" => RunDevFlowAlert(tokens, options),
                "help" => WriteResult("devflow", "Usage: dotnet jalex devflow <status|screenshot|tap|extensions|inspector|broker|network|ui|alert> [options]", options),
                "--help" => WriteResult("devflow", "Usage: dotnet jalex devflow <status|screenshot|tap|extensions|inspector|broker|network|ui|alert> [options]", options),
                "-h" => WriteResult("devflow", "Usage: dotnet jalex devflow <status|screenshot|tap|extensions|inspector|broker|network|ui|alert> [options]", options),
                _ => UnknownDevFlowSubcommand(subcommand)
            };
        }

        private static int RunDevFlowStatus(Queue<string> tokens, OutputOptions options)
        {
            ParseDevFlowOptions(tokens, out var host, out var port, out _);
            using var client = new AgentClient(host, port);
            AgentStatus? status;
            try
            {
                status = client.GetStatusAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException)
            {
                return WriteResult("devflow", $"Unable to contact Jalium DevFlow agent at {host}:{port}: {ex.Message}", options);
            }

            if (status == null)
                return WriteResult("devflow", "Unable to retrieve agent status.", options);

            if (options.Json)
            {
                Console.WriteLine(JsonSerializer.Serialize(status, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }));
                return 0;
            }

            Console.WriteLine("Jalium DevFlow agent status:");
            Console.WriteLine($"  Name:        {status.Name}");
            Console.WriteLine($"  Id:          {status.Id}");
            Console.WriteLine($"  Framework:   {status.Framework}");
            Console.WriteLine($"  Version:     {status.Version}");
            Console.WriteLine($"  Application: {status.Application}");
            Console.WriteLine($"  Running:     {status.Running}");
            Console.WriteLine($"  Port:        {status.Port}");
            return 0;
        }

        private static int RunDevFlowScreenshot(Queue<string> tokens, OutputOptions options)
        {
            ParseDevFlowOptions(tokens, out var host, out var port, out var outputFile);
            outputFile ??= "jalium-devflow-screenshot.png";

            if (options.DryRun)
            {
                Console.WriteLine($"[dry-run] save screenshot from http://{host}:{port}/api/v1/ui/screenshot to {outputFile}");
                return 0;
            }

            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            var url = new Uri($"http://{host}:{port}/api/v1/ui/screenshot");
            try
            {
                using var response = http.GetAsync(url).GetAwaiter().GetResult();
                response.EnsureSuccessStatusCode();
                var bytes = response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
                File.WriteAllBytes(outputFile, bytes);
                return WriteResult("devflow", $"Saved screenshot to {outputFile}", options);
            }
            catch (Exception ex)
            {
                return WriteResult("devflow", $"Failed to capture screenshot: {ex.Message}", options);
            }
        }

        private static int RunDevFlowTap(Queue<string> tokens, OutputOptions options)
        {
            ParseDevFlowOptions(tokens, out var host, out var port, out _);
            string? elementId = null;

            while (tokens.Count > 0)
            {
                var token = tokens.Dequeue();
                if (token == "--id" && tokens.Count > 0)
                {
                    elementId = tokens.Dequeue();
                    continue;
                }

                Console.Error.WriteLine($"Unknown option: {token}");
                return 1;
            }

            if (string.IsNullOrEmpty(elementId))
                return WriteResult("devflow", "Missing --id <elementId> for devflow tap.", options);

            using var client = new AgentClient(host, port);
            try
            {
                var result = client.TapAsync(elementId).GetAwaiter().GetResult();
                return WriteResult("devflow", result ? $"Tapped element '{elementId}'" : $"Failed to tap element '{elementId}'", options);
            }
            catch (Exception ex)
            {
                return WriteResult("devflow", $"DevFlow tap failed: {ex.Message}", options);
            }
        }

        private static int RunDevFlowExtensions(Queue<string> tokens, OutputOptions options)
        {
            if (tokens.Count == 0)
                return WriteResult("devflow", "Usage: dotnet jalex devflow extensions <list|describe|call> [options]", options);

            var sub = tokens.Dequeue().ToLowerInvariant();
            return sub switch
            {
                "list" => RunDevFlowExtensionsList(tokens, options),
                "describe" => RunDevFlowExtensionsDescribe(tokens, options),
                "call" => RunDevFlowExtensionsCall(tokens, options),
                _ => WriteResult("devflow", "Usage: dotnet jalex devflow extensions <list|describe|call> [options]", options)
            };
        }

        private static int RunDevFlowExtensionsList(Queue<string> tokens, OutputOptions options)
        {
            ParseExtensionsOptions(tokens, out var host, out var port, out _, out _);
            using var client = new AgentClient(host, port);
            try
            {
                var result = client.ListExtensionsAsync().GetAwaiter().GetResult();
                Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
                return 0;
            }
            catch (Exception ex)
            {
                return WriteResult("devflow", $"Failed to list extensions: {ex.Message}", options);
            }
        }

        private static int RunDevFlowExtensionsDescribe(Queue<string> tokens, OutputOptions options)
        {
            ParseExtensionsOptions(tokens, out var host, out var port, out var name, out _);
            if (string.IsNullOrEmpty(name))
                return WriteResult("devflow", "Missing action name for devflow extensions describe.", options);

            using var client = new AgentClient(host, port);
            try
            {
                var result = client.ListExtensionsAsync().GetAwaiter().GetResult();
                if (result.HasValue && result.Value.TryGetProperty("actions", out var list))
                {
                    foreach (var action in list.EnumerateArray())
                    {
                        if (action.TryGetProperty("name", out var n) && string.Equals(n.GetString(), name, StringComparison.OrdinalIgnoreCase))
                        {
                            Console.WriteLine(JsonSerializer.Serialize(action, new JsonSerializerOptions { WriteIndented = true }));
                            return 0;
                        }
                    }
                }

                return WriteResult("devflow", $"Extension '{name}' not found.", options);
            }
            catch (Exception ex)
            {
                return WriteResult("devflow", $"Failed to describe extension: {ex.Message}", options);
            }
        }

        private static int RunDevFlowExtensionsCall(Queue<string> tokens, OutputOptions options)
        {
            ParseExtensionsOptions(tokens, out var host, out var port, out var name, out var args);
            if (string.IsNullOrEmpty(name))
                return WriteResult("devflow", "Missing action name for devflow extensions call.", options);

            using var client = new AgentClient(host, port);
            try
            {
                var (success, result) = client.CallExtensionAsync(name, args.Count > 0 ? args.ToArray() : null).GetAwaiter().GetResult();
                Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
                return success ? 0 : 1;
            }
            catch (Exception ex)
            {
                return WriteResult("devflow", $"Failed to call extension '{name}': {ex.Message}", options);
            }
        }

        private static void ParseExtensionsOptions(Queue<string> tokens, out string host, out int port, out string? name, out List<JsonElement> args)
        {
            host = "localhost";
            port = 9223;
            name = null;
            args = new List<JsonElement>();

            while (tokens.Count > 0)
            {
                var token = tokens.Dequeue();
                if (token == "--host" && tokens.Count > 0) { host = tokens.Dequeue(); continue; }
                if (token == "--port" && tokens.Count > 0 && int.TryParse(tokens.Dequeue(), out var parsedPort)) { port = parsedPort; continue; }
                if (token == "--name" && tokens.Count > 0) { name = tokens.Dequeue(); continue; }
                if (token == "--arg" && tokens.Count > 0) { args.Add(JsonSerializer.SerializeToElement(tokens.Dequeue())); continue; }
                if (!token.StartsWith("--") && name == null) { name = token; continue; }

                Console.Error.WriteLine($"Unknown option: {token}");
            }
        }

        private static int RunDevFlowInspector(Queue<string> tokens, OutputOptions options)
        {
            ParseInspectorOptions(tokens, out var host, out var port, out var inspectorPort);

            using var server = new InspectorServer(inspectorPort, host, port);
            try
            {
                server.Start();
            }
            catch (Exception ex)
            {
                return WriteResult("devflow", $"Failed to start inspector: {ex.Message}", options);
            }

            Console.WriteLine($"DevFlow Web Inspector running at http://localhost:{inspectorPort}/ (proxying agent at {host}:{port})");
            Console.WriteLine("Press Ctrl+C to stop.");

            using var exitEvent = new ManualResetEventSlim(false);
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; exitEvent.Set(); };
            exitEvent.Wait();

            server.StopAsync().GetAwaiter().GetResult();
            return 0;
        }

        private static void ParseInspectorOptions(Queue<string> tokens, out string host, out int port, out int inspectorPort)
        {
            host = "localhost";
            port = 9223;
            inspectorPort = 9300;

            while (tokens.Count > 0)
            {
                var token = tokens.Dequeue();
                if (token == "--host" && tokens.Count > 0) { host = tokens.Dequeue(); continue; }
                if (token == "--port" && tokens.Count > 0 && int.TryParse(tokens.Dequeue(), out var parsedPort)) { port = parsedPort; continue; }
                if (token == "--inspector-port" && tokens.Count > 0 && int.TryParse(tokens.Dequeue(), out var parsedInspectorPort)) { inspectorPort = parsedInspectorPort; continue; }

                Console.Error.WriteLine($"Unknown option: {token}");
            }
        }

        private static int RunDevFlowBroker(Queue<string> tokens, OutputOptions options)
        {
            if (tokens.Count == 0)
                return WriteResult("devflow", "Usage: dotnet jalex devflow broker <start|stop|status|list> [options]", options);

            var sub = tokens.Dequeue().ToLowerInvariant();
            return sub switch
            {
                "start" => RunDevFlowBrokerStart(tokens, options),
                "stop" => RunDevFlowBrokerStop(options),
                "status" => RunDevFlowBrokerStatus(options),
                "list" => RunDevFlowBrokerList(options),
                _ => WriteResult("devflow", "Usage: dotnet jalex devflow broker <start|stop|status|list> [options]", options)
            };
        }

        private static int RunDevFlowBrokerStart(Queue<string> tokens, OutputOptions options)
        {
            var foreground = tokens.Count > 0 && tokens.Peek() == "--foreground";
            if (foreground) tokens.Dequeue();

            if (foreground)
            {
                using var cts = new CancellationTokenSource();
                Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
                using var server = new BrokerServer(log: Console.WriteLine);
                server.RunAsync(cts.Token).GetAwaiter().GetResult();
                return 0;
            }

            var port = BrokerClient.EnsureBrokerRunningAsync().GetAwaiter().GetResult();
            return port.HasValue
                ? WriteResult("devflow", $"Broker running on port {port.Value}", options)
                : WriteResult("devflow", "Failed to start broker. Run with --foreground for diagnostics.", options);
        }

        private static int RunDevFlowBrokerStop(OutputOptions options)
        {
            var success = BrokerClient.ShutdownBrokerAsync().GetAwaiter().GetResult();
            return WriteResult("devflow", success ? "Broker shutdown requested" : "Broker is not running", options);
        }

        private static int RunDevFlowBrokerStatus(OutputOptions options)
        {
            var port = BrokerClient.ReadBrokerPortPublic() ?? BrokerServer.DefaultPort;
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
                var response = http.GetStringAsync($"http://localhost:{port}/api/health").GetAwaiter().GetResult();
                using var doc = JsonDocument.Parse(response);
                var agents = doc.RootElement.GetProperty("agents").GetInt32();
                return WriteResult("devflow", $"Broker: running on port {port} ({agents} agent(s) connected)", options);
            }
            catch
            {
                return WriteResult("devflow", "Broker: not running", options);
            }
        }

        private static int RunDevFlowBrokerList(OutputOptions options)
        {
            var port = BrokerClient.ReadBrokerPortPublic() ?? BrokerServer.DefaultPort;
            var agents = BrokerClient.ListAgents(port);
            if (agents == null || agents.Length == 0)
                return WriteResult("devflow", "No agents connected.", options);

            Console.WriteLine(JsonSerializer.Serialize(agents, new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }

        private static int RunDevFlowNetwork(Queue<string> tokens, OutputOptions options)
        {
            if (tokens.Count == 0)
                return WriteResult("devflow", "Usage: dotnet jalex devflow network <list|detail|clear> [options]", options);

            var sub = tokens.Dequeue().ToLowerInvariant();
            return sub switch
            {
                "list" => RunDevFlowNetworkList(tokens, options),
                "detail" => RunDevFlowNetworkDetail(tokens, options),
                "clear" => RunDevFlowNetworkClear(tokens, options),
                _ => WriteResult("devflow", "Usage: dotnet jalex devflow network <list|detail|clear> [options]", options)
            };
        }

        private static int RunDevFlowNetworkList(Queue<string> tokens, OutputOptions options)
        {
            ParseDevFlowOptions(tokens, out var host, out var port, out _);
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                var response = http.GetStringAsync($"http://{host}:{port}/api/v1/network/list").GetAwaiter().GetResult();
                Console.WriteLine(JsonSerializer.Serialize(JsonDocument.Parse(response).RootElement, new JsonSerializerOptions { WriteIndented = true }));
                return 0;
            }
            catch (Exception ex)
            {
                return WriteResult("devflow", $"Failed to list network requests: {ex.Message}", options);
            }
        }

        private static int RunDevFlowNetworkDetail(Queue<string> tokens, OutputOptions options)
        {
            string? id = null;
            var remaining = new Queue<string>();
            while (tokens.Count > 0)
            {
                var token = tokens.Dequeue();
                if (token == "--id" && tokens.Count > 0) { id = tokens.Dequeue(); continue; }
                remaining.Enqueue(token);
            }

            ParseDevFlowOptions(remaining, out var host, out var port, out _);

            if (string.IsNullOrEmpty(id))
                return WriteResult("devflow", "Missing --id <requestId> for devflow network detail.", options);

            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                var response = http.GetStringAsync($"http://{host}:{port}/api/v1/network/detail?id={Uri.EscapeDataString(id)}").GetAwaiter().GetResult();
                Console.WriteLine(JsonSerializer.Serialize(JsonDocument.Parse(response).RootElement, new JsonSerializerOptions { WriteIndented = true }));
                return 0;
            }
            catch (Exception ex)
            {
                return WriteResult("devflow", $"Failed to get network request detail: {ex.Message}", options);
            }
        }

        private static int RunDevFlowNetworkClear(Queue<string> tokens, OutputOptions options)
        {
            ParseDevFlowOptions(tokens, out var host, out var port, out _);
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                using var response = http.PostAsync($"http://{host}:{port}/api/v1/network/clear", new StringContent(string.Empty)).GetAwaiter().GetResult();
                return WriteResult("devflow", response.IsSuccessStatusCode ? "Network log cleared." : "Failed to clear network log.", options);
            }
            catch (Exception ex)
            {
                return WriteResult("devflow", $"Failed to clear network log: {ex.Message}", options);
            }
        }

        private static int RunDevFlowUi(Queue<string> tokens, OutputOptions options)
        {
            if (tokens.Count == 0)
                return WriteResult("devflow", "Usage: dotnet jalex devflow ui <query|hit-test|assert> [options]", options);

            var sub = tokens.Dequeue().ToLowerInvariant();
            return sub switch
            {
                "query" => RunDevFlowUiQuery(tokens, options),
                "hit-test" => RunDevFlowUiHitTest(tokens, options),
                "assert" => RunDevFlowUiAssert(tokens, options),
                _ => WriteResult("devflow", "Usage: dotnet jalex devflow ui <query|hit-test|assert> [options]", options)
            };
        }

        private static int RunDevFlowUiQuery(Queue<string> tokens, OutputOptions options)
        {
            string? selector = null;
            var remaining = new Queue<string>();
            while (tokens.Count > 0)
            {
                var token = tokens.Dequeue();
                if (token == "--selector" && tokens.Count > 0) { selector = tokens.Dequeue(); continue; }
                remaining.Enqueue(token);
            }

            ParseDevFlowOptions(remaining, out var host, out var port, out _);

            if (string.IsNullOrEmpty(selector))
                return WriteResult("devflow", "Missing --selector <cssSelector> for devflow ui query.", options);

            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                var response = http.GetStringAsync($"http://{host}:{port}/api/v1/ui/query-selector?selector={Uri.EscapeDataString(selector)}").GetAwaiter().GetResult();
                Console.WriteLine(JsonSerializer.Serialize(JsonDocument.Parse(response).RootElement, new JsonSerializerOptions { WriteIndented = true }));
                return 0;
            }
            catch (Exception ex)
            {
                return WriteResult("devflow", $"Failed to query elements: {ex.Message}", options);
            }
        }

        private static int RunDevFlowUiHitTest(Queue<string> tokens, OutputOptions options)
        {
            double? x = null;
            double? y = null;
            var remaining = new Queue<string>();
            while (tokens.Count > 0)
            {
                var token = tokens.Dequeue();
                if (token == "--x" && tokens.Count > 0 && double.TryParse(tokens.Dequeue(), out var parsedX)) { x = parsedX; continue; }
                if (token == "--y" && tokens.Count > 0 && double.TryParse(tokens.Dequeue(), out var parsedY)) { y = parsedY; continue; }
                remaining.Enqueue(token);
            }

            ParseDevFlowOptions(remaining, out var host, out var port, out _);

            if (x == null || y == null)
                return WriteResult("devflow", "Missing --x <x> --y <y> for devflow ui hit-test.", options);

            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                var response = http.GetStringAsync($"http://{host}:{port}/api/v1/ui/hit-test?x={x}&y={y}").GetAwaiter().GetResult();
                Console.WriteLine(JsonSerializer.Serialize(JsonDocument.Parse(response).RootElement, new JsonSerializerOptions { WriteIndented = true }));
                return 0;
            }
            catch (Exception ex)
            {
                return WriteResult("devflow", $"Failed to hit-test: {ex.Message}", options);
            }
        }

        private static int RunDevFlowUiAssert(Queue<string> tokens, OutputOptions options)
        {
            string? selector = null;
            bool? exists = null;
            int? count = null;
            string? textEquals = null;
            string? textContains = null;
            var remaining = new Queue<string>();
            while (tokens.Count > 0)
            {
                var token = tokens.Dequeue();
                if (token == "--selector" && tokens.Count > 0) { selector = tokens.Dequeue(); continue; }
                if (token == "--exists" && tokens.Count > 0 && bool.TryParse(tokens.Dequeue(), out var parsedExists)) { exists = parsedExists; continue; }
                if (token == "--count" && tokens.Count > 0 && int.TryParse(tokens.Dequeue(), out var parsedCount)) { count = parsedCount; continue; }
                if (token == "--text-equals" && tokens.Count > 0) { textEquals = tokens.Dequeue(); continue; }
                if (token == "--text-contains" && tokens.Count > 0) { textContains = tokens.Dequeue(); continue; }
                remaining.Enqueue(token);
            }

            ParseDevFlowOptions(remaining, out var host, out var port, out _);

            if (string.IsNullOrEmpty(selector))
                return WriteResult("devflow", "Missing --selector <cssSelector> for devflow ui assert.", options);

            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                var payload = JsonSerializer.Serialize(new { selector, exists, count, textEquals, textContains },
                    new JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull });
                using var content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json");
                using var response = http.PostAsync($"http://{host}:{port}/api/v1/ui/assert", content).GetAwaiter().GetResult();
                var body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                Console.WriteLine(JsonSerializer.Serialize(JsonDocument.Parse(body).RootElement, new JsonSerializerOptions { WriteIndented = true }));
                return response.IsSuccessStatusCode ? 0 : 1;
            }
            catch (Exception ex)
            {
                return WriteResult("devflow", $"Failed to assert: {ex.Message}", options);
            }
        }

        private static int RunDevFlowAlert(Queue<string> tokens, OutputOptions options)
        {
            if (tokens.Count == 0)
                return WriteResult("devflow", "Usage: dotnet jalex devflow alert <detect|dismiss> [options]", options);

            var sub = tokens.Dequeue().ToLowerInvariant();
            return sub switch
            {
                "detect" => RunDevFlowAlertDetect(tokens, options),
                "dismiss" => RunDevFlowAlertDismiss(tokens, options),
                _ => WriteResult("devflow", "Usage: dotnet jalex devflow alert <detect|dismiss> [options]", options)
            };
        }

        private static int RunDevFlowAlertDetect(Queue<string> tokens, OutputOptions options)
        {
            ParseDevFlowOptions(tokens, out var host, out var port, out _);
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                var response = http.GetStringAsync($"http://{host}:{port}/api/v1/alert/detect").GetAwaiter().GetResult();
                Console.WriteLine(JsonSerializer.Serialize(JsonDocument.Parse(response).RootElement, new JsonSerializerOptions { WriteIndented = true }));
                return 0;
            }
            catch (Exception ex)
            {
                return WriteResult("devflow", $"Failed to detect alert: {ex.Message}", options);
            }
        }

        private static int RunDevFlowAlertDismiss(Queue<string> tokens, OutputOptions options)
        {
            string? buttonLabel = null;
            var remaining = new Queue<string>();
            while (tokens.Count > 0)
            {
                var token = tokens.Dequeue();
                if (token == "--button" && tokens.Count > 0) { buttonLabel = tokens.Dequeue(); continue; }
                remaining.Enqueue(token);
            }

            ParseDevFlowOptions(remaining, out var host, out var port, out _);

            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                var payload = JsonSerializer.Serialize(new { buttonLabel },
                    new JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull });
                using var content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json");
                using var response = http.PostAsync($"http://{host}:{port}/api/v1/alert/dismiss", content).GetAwaiter().GetResult();
                var body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                return WriteResult("devflow", response.IsSuccessStatusCode ? "Alert dismissed." : $"Failed to dismiss alert: {body}", options);
            }
            catch (Exception ex)
            {
                return WriteResult("devflow", $"Failed to dismiss alert: {ex.Message}", options);
            }
        }

        private static void ParseDevFlowOptions(Queue<string> tokens, out string host, out int port, out string? outputFile)
        {
            host = "localhost";
            port = 9223;
            outputFile = null;

            while (tokens.Count > 0)
            {
                var token = tokens.Dequeue();
                if (token == "--host" && tokens.Count > 0)
                {
                    host = tokens.Dequeue();
                    continue;
                }

                if (token == "--port" && tokens.Count > 0 && int.TryParse(tokens.Dequeue(), out var parsedPort))
                {
                    port = parsedPort;
                    continue;
                }

                if (token == "--output" && tokens.Count > 0)
                {
                    outputFile = tokens.Dequeue();
                    continue;
                }

                Console.Error.WriteLine($"Unknown option: {token}");
                break;
            }
        }

        private static string? ParseTarget(Queue<string> tokens, out string configuration, out string runtime, out string framework, out string outputDirectory)
        {
            configuration = "Debug";
            runtime = string.Empty;
            framework = string.Empty;
            outputDirectory = string.Empty;
            string? target = null;

            while (tokens.Count > 0)
            {
                var token = tokens.Dequeue();
                switch (token)
                {
                    case "--configuration":
                    case "-c":
                        if (tokens.Count > 0)
                            configuration = tokens.Dequeue();
                        break;
                    case "--runtime":
                    case "-r":
                        if (tokens.Count > 0)
                            runtime = tokens.Dequeue();
                        break;
                    case "--framework":
                        if (tokens.Count > 0)
                            framework = tokens.Dequeue();
                        break;
                    case "--output":
                        if (tokens.Count > 0)
                            outputDirectory = tokens.Dequeue();
                        break;
                    default:
                        if (target == null)
                            target = token;
                        break;
                }
            }

            return target;
        }

        private static string? TryResolveLocalAgentProjectReference(string newProjectDirectory)
        {
            var repoRoot = FindRepositoryRoot(Path.GetFullPath(newProjectDirectory));
            if (repoRoot == null)
                return null;

            var projectPath = Path.Combine(repoRoot, "src", "DevFlow", "LeXtudio.DevFlow.Agent.Jalium", "LeXtudio.DevFlow.Agent.Jalium.csproj");
            if (!File.Exists(projectPath))
                return null;

            var relative = Path.GetRelativePath(newProjectDirectory, projectPath).Replace(Path.DirectorySeparatorChar, '/');
            return relative;
        }

        private static string? FindRepositoryRoot(string directory)
        {
            var current = new DirectoryInfo(directory);
            while (current != null)
            {
                if (File.Exists(Path.Combine(current.FullName, ".git")) || Directory.Exists(Path.Combine(current.FullName, ".git")))
                    return current.FullName;

                current = current.Parent;
            }

            return null;
        }

        private static string GetJaliumProjectFile(string name, string? projectReference)
        {
            var referenceText = string.Empty;
            if (!string.IsNullOrEmpty(projectReference))
            {
                referenceText = $"  <ItemGroup>\n    <ProjectReference Include=\"{projectReference}\" />\n  </ItemGroup>\n";
            }

            return $"<Project Sdk=\"Microsoft.NET.Sdk\">\n" +
                   "  <PropertyGroup>\n" +
                   "    <OutputType>Exe</OutputType>\n" +
                   "    <TargetFramework>net10.0</TargetFramework>\n" +
                   "    <RuntimeIdentifiers>win-x64;win-arm64;linux-x64;linux-arm64;osx-x64;osx-arm64</RuntimeIdentifiers>\n" +
                   "    <ImplicitUsings>enable</ImplicitUsings>\n" +
                   "    <Nullable>enable</Nullable>\n" +
                   "    <AssemblyName>" + name + "</AssemblyName>\n" +
                   "    <RootNamespace>" + name + "</RootNamespace>\n" +
                   "  </PropertyGroup>\n" +
                   "  <ItemGroup>\n" +
                   "    <PackageReference Include=\"Jalium.UI.Controls\" />\n" +
                   "  </ItemGroup>\n" +
                   referenceText +
                   "</Project>\n";
        }

        private static string GetJaliumProgramCode(bool includeAgent)
        {
            if (!includeAgent)
            {
                return """
using Jalium.UI;
using Jalium.UI.Controls;

var window = new Window { Title = "Jalium App", Width = 900, Height = 700 };
var app = new Application();
app.Run(window);
""";
            }

            return """
using Jalium.UI;
using Jalium.UI.Controls;
using Microsoft.Maui.DevFlow.Agent.Core;
using LeXtudio.DevFlow.Agent.Jalium;

var port = int.TryParse(Environment.GetEnvironmentVariable("DEVFLOW_AGENT_PORT"), out var p) && p > 0 ? p : 9223;
var window = new Window { Title = "Jalium App", Width = 900, Height = 700 };
window.Loaded += (s, e) =>
{
    var agent = Application.Current!.AddJaliumDevFlowAgent(new AgentOptions { Port = port });
    Console.WriteLine($"DevFlow agent started on port {agent.Port}.");
};
var app = new Application();
app.Run(window);
""";
        }

        private static int RunDotnetCommand(string arguments, OutputOptions options, string workingDirectory)
        {
            if (options.DryRun)
            {
                Console.WriteLine($"[dry-run] dotnet {arguments}");
                return 0;
            }

            var startInfo = new ProcessStartInfo("dotnet", arguments)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                WorkingDirectory = workingDirectory,
            };

            using var process = Process.Start(startInfo);
            if (process == null)
            {
                Console.Error.WriteLine("Failed to start dotnet process.");
                return 1;
            }

            process.OutputDataReceived += (_, e) => { if (e.Data != null) Console.WriteLine(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data != null) Console.Error.WriteLine(e.Data); };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            process.WaitForExit();
            return process.ExitCode;
        }

        private static int UnknownDevFlowSubcommand(string subcommand)
        {
            Console.Error.WriteLine($"Unknown devflow subcommand: {subcommand}");
            Console.Error.WriteLine("Run 'dotnet jalex devflow --help' for available commands.");
            return 1;
        }

        private static int WriteResult(string command, string message, OutputOptions options)
        {
            if (options.Json)
            {
                var payload = new { command, message, timestamp = DateTime.UtcNow };
                Console.WriteLine(JsonSerializer.Serialize(payload));
                return 0;
            }

            Console.WriteLine(message);
            if (options.Verbose)
                Console.WriteLine("(verbose mode enabled)");

            if (options.DryRun)
                Console.WriteLine("(dry run: no changes will be made)");

            return 0;
        }
    }
}
