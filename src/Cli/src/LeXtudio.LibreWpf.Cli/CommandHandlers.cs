using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using LeXtudio.DevFlow.Driver;

namespace LeXtudio.LibreWpf.Cli
{
    public static class CommandHandlers
    {
        public static int RunDoctor(OutputOptions options)
        {
            return WriteResult("doctor", "Validated the LibreWPF development environment.", options);
        }

        public static int RunVersion(OutputOptions options)
        {
            return WriteResult("version", "LeXtudio.LibreWpf.Cli version 1.0.0", options);
        }

        public static int RunNew(Queue<string> tokens, OutputOptions options)
        {
            var name = tokens.Count > 0 ? tokens.Dequeue() : "LibreWpfApp";
            var targetDirectory = Path.GetFullPath(name);

            if (Directory.Exists(targetDirectory) || File.Exists(targetDirectory))
                return WriteResult("new", $"Target already exists: {targetDirectory}", options);

            if (options.DryRun)
            {
                Console.WriteLine($"[dry-run] create new LibreWPF app in {targetDirectory}");
                return 0;
            }

            Directory.CreateDirectory(targetDirectory);
            File.WriteAllText(Path.Combine(targetDirectory, $"{name}.csproj"), GetLibreWpfProjectFile(name));
            File.WriteAllText(Path.Combine(targetDirectory, "Program.cs"), GetLibreWpfProgramCode(name));

            return WriteResult("new", $"Created LibreWPF app '{name}' in {targetDirectory}", options);
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
            var target = ParseTarget(tokens, out var configuration, out var runtime, out var framework, out var outputDirectory);
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
            Console.WriteLine($"OS: {RuntimeInformation.OSDescription}");
            Console.WriteLine($"Process architecture: {RuntimeInformation.ProcessArchitecture}");
            return RunDotnetCommand("--info", options, Path.GetFullPath("."));
        }

        public static int RunDevFlow(Queue<string> tokens, OutputOptions options)
        {
            if (tokens.Count == 0)
            {
                return WriteResult("devflow", "Usage: dotnet librewpf devflow <status|screenshot|tap|webview> [options]", options);
            }

            var subcommand = tokens.Dequeue().ToLowerInvariant();
            return subcommand switch
            {
                "status" => RunDevFlowStatus(tokens, options),
                "screenshot" => RunDevFlowScreenshot(tokens, options),
                "tap" => RunDevFlowTap(tokens, options),
                "webview" => RunDevFlowWebView(tokens, options),
                "help" => WriteResult("devflow", "Usage: dotnet librewpf devflow <status|screenshot|tap|webview> [options]", options),
                "--help" => WriteResult("devflow", "Usage: dotnet librewpf devflow <status|screenshot|tap|webview> [options]", options),
                "-h" => WriteResult("devflow", "Usage: dotnet librewpf devflow <status|screenshot|tap|webview> [options]", options),
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
                return WriteResult("devflow", $"Unable to contact LibreWPF DevFlow agent at {host}:{port}: {ex.Message}", options);
            }

            if (status == null)
                return WriteResult("devflow", "Unable to retrieve agent status.", options);

            if (options.Json)
            {
                Console.WriteLine(JsonSerializer.Serialize(status, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }));
                return 0;
            }

            Console.WriteLine("LibreWPF DevFlow agent status:");
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
            outputFile ??= "librewpf-devflow-screenshot.png";

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

        private static int RunDevFlowWebView(Queue<string> tokens, OutputOptions options)
        {
            if (tokens.Count == 0)
                return WriteResult("devflow", "Usage: dotnet librewpf devflow webview <contexts|screenshot> [options]", options);

            var sub = tokens.Dequeue().ToLowerInvariant();
            return sub switch
            {
                "contexts" => RunDevFlowWebViewContexts(tokens, options),
                "screenshot" => RunDevFlowWebViewScreenshot(tokens, options),
                "cdp" => RunDevFlowWebViewCdp(tokens, options),
                _ => WriteResult("devflow", "Usage: dotnet librewpf devflow webview <contexts|screenshot> [options]", options)
            };
        }

        private static int RunDevFlowWebViewContexts(Queue<string> tokens, OutputOptions options)
        {
            ParseDevFlowOptions(tokens, out var host, out var port, out _);
            using var client = new AgentClient(host, port);
            try
            {
                var jsonElement = client.GetWebViewContextsAsync().GetAwaiter().GetResult();
                var json = JsonSerializer.Serialize(jsonElement, new JsonSerializerOptions { WriteIndented = true });
                if (options.Json)
                {
                    Console.WriteLine(json);
                    return 0;
                }

                Console.WriteLine("WebView contexts:");
                Console.WriteLine(json);
                return 0;
            }
            catch (Exception ex)
            {
                return WriteResult("devflow", $"Failed to get webview contexts: {ex.Message}", options);
            }
        }

        private static int RunDevFlowWebViewScreenshot(Queue<string> tokens, OutputOptions options)
        {
            ParseDevFlowOptions(tokens, out var host, out var port, out var outputFile);
            string? context = null;
            while (tokens.Count > 0)
            {
                var token = tokens.Dequeue();
                if (token == "--context" && tokens.Count > 0)
                {
                    context = tokens.Dequeue();
                    continue;
                }
            }

            outputFile ??= "librewpf-devflow-webview-screenshot.png";
            try
            {
                using var client = new AgentClient(host, port);
                var bytes = client.GetWebViewScreenshotAsync(context).GetAwaiter().GetResult();
                if (bytes == null || bytes.Length == 0)
                    return WriteResult("devflow", "Failed to capture webview screenshot: no data returned", options);
                File.WriteAllBytes(outputFile, bytes);
                return WriteResult("devflow", $"Saved webview screenshot to {outputFile}", options);
            }
            catch (Exception ex)
            {
                return WriteResult("devflow", $"Failed to capture webview screenshot: {ex.Message}", options);
            }
        }

        private static int RunDevFlowWebViewCdp(Queue<string> tokens, OutputOptions options)
        {
            ParseDevFlowOptions(tokens, out var host, out var port, out _);
            string? context = null;
            string? method = null;
            string? expression = null;
            while (tokens.Count > 0)
            {
                var token = tokens.Dequeue();
                if (token == "--context" && tokens.Count > 0) context = tokens.Dequeue();
                else if (token == "--method" && tokens.Count > 0) method = tokens.Dequeue();
                else if (token == "--expression" && tokens.Count > 0) expression = tokens.Dequeue();
            }

            method ??= "Runtime.evaluate";
            try
            {
                using var client = new AgentClient(host, port);
                JsonElement? parameters = null;
                if (!string.IsNullOrWhiteSpace(expression))
                {
                    parameters = JsonSerializer.Deserialize<JsonElement>($"{{\"expression\":{JsonSerializer.Serialize(expression)}}}");
                }
                var result = client.SendWebViewCdpCommandAsync(method, parameters, context).GetAwaiter().GetResult();
                Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
                return 0;
            }
            catch (Exception ex)
            {
                return WriteResult("devflow", $"Failed to execute webview CDP command: {ex.Message}", options);
            }
        }

        private static void ParseDevFlowOptions(Queue<string> tokens, out string host, out int port, out string? outputFile)
        {
            host = "localhost";
            port = 5500;
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

        private static string GetLibreWpfProjectFile(string name)
        {
            return $"<Project Sdk=\"LibreWPF.Sdk\">\n" +
                   "  <PropertyGroup>\n" +
                   "    <OutputType>WinExe</OutputType>\n" +
                   "    <TargetFramework>net10.0-windows</TargetFramework>\n" +
                   "    <UseWPF>true</UseWPF>\n" +
                   "    <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>\n" +
                   "    <ImplicitUsings>enable</ImplicitUsings>\n" +
                   "    <Nullable>enable</Nullable>\n" +
                   "    <RootNamespace>" + name + "</RootNamespace>\n" +
                   "  </PropertyGroup>\n" +
                   "</Project>\n";
        }

        private static string GetLibreWpfProgramCode(string name)
        {
            return $"using System;\nusing System.Windows;\nusing System.Windows.Controls;\n\nnamespace {name}\n{{\n    public static class Program\n    {{\n        [STAThread]\n        public static void Main()\n        {{\n            var app = new Application();\n            var window = new Window\n            {{\n                Title = \"{name}\",\n                Width = 800,\n                Height = 450,\n                Content = new Grid\n                {{\n                    Children =\n                    {{\n                        new TextBlock\n                        {{\n                            Text = \"Hello, LibreWPF!\",\n                            HorizontalAlignment = HorizontalAlignment.Center,\n                            VerticalAlignment = VerticalAlignment.Center,\n                            FontSize = 24\n                        }}\n                    }}\n                }}\n            }};\n\n            app.Run(window);\n        }}\n    }}\n}}\n";
        }

        private static int UnknownDevFlowSubcommand(string subcommand)
        {
            Console.Error.WriteLine($"Unknown devflow subcommand: {subcommand}");
            Console.Error.WriteLine("Run 'dotnet librewpf devflow --help' for available commands.");
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
            {
                Console.WriteLine("(verbose mode enabled)");
            }

            if (options.DryRun)
            {
                Console.WriteLine("(dry run: no changes will be made)");
            }

            return 0;
        }
    }
}
