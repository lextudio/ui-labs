using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace LeXtudio.Uno.Cli
{
    public static class Program
    {
        public static int Main(string[] args)
        {
            var tokens = new Queue<string>(args ?? Array.Empty<string>());
            var options = ParseGlobalOptions(tokens);

            if (options.ShowHelp || tokens.Count == 0)
            {
                ShowHelp();
                return 0;
            }

            var command = tokens.Dequeue().ToLowerInvariant();
            return RunCommand(command, tokens, options);
        }

        private static OutputOptions ParseGlobalOptions(Queue<string> tokens)
        {
            var options = new OutputOptions();
            var preserved = new List<string>();

            while (tokens.Count > 0)
            {
                var token = tokens.Peek();
                switch (token)
                {
                    case "--json":
                        options = options with { Json = true };
                        tokens.Dequeue();
                        break;
                    case "--verbose":
                    case "-v":
                        options = options with { Verbose = true };
                        tokens.Dequeue();
                        break;
                    case "--dry-run":
                        options = options with { DryRun = true };
                        tokens.Dequeue();
                        break;
                    case "--ci":
                        options = options with { Ci = true };
                        tokens.Dequeue();
                        break;
                    case "--help":
                    case "-h":
                        options = options with { ShowHelp = true };
                        tokens.Dequeue();
                        break;
                    default:
                        preserved.Add(tokens.Dequeue());
                        break;
                }
            }

            while (preserved.Count > 0)
            {
                tokens.Enqueue(preserved[0]);
                preserved.RemoveAt(0);
            }

            return options;
        }

        private static int RunCommand(string command, Queue<string> tokens, OutputOptions options)
        {
            return command switch
            {
                "doctor" => CommandHandlers.RunDoctor(options),
                "version" => CommandHandlers.RunVersion(options),
                "new" => CommandHandlers.RunNew(tokens, options),
                "build" => CommandHandlers.RunBuild(tokens, options),
                "run" => CommandHandlers.RunRun(tokens, options),
                "publish" => CommandHandlers.RunPublish(tokens, options),
                "package" => CommandHandlers.RunPackage(tokens, options),
                "diagnostics" => CommandHandlers.RunDiagnostics(tokens, options),
                "env" => CommandHandlers.RunEnv(tokens, options),
                "devflow" => CommandHandlers.RunDevFlow(tokens, options),
                "commands" => RunCommandsSchema(),
                "batch" => RunBatch(),
                "help" => ShowHelpAndReturn(),
                _ => UnknownCommand(command)
            };
        }

        private static int ShowHelpAndReturn()
        {
            ShowHelp();
            return 0;
        }

        private static int RunCommandsSchema()
        {
            var commands = new[]
            {
                new { name = "doctor", description = "Validate the Uno development environment" },
                new { name = "version", description = "Display CLI and environment version information" },
                new { name = "new", description = "Scaffold a new Uno app" },
                new { name = "build", description = "Build an Uno project" },
                new { name = "run", description = "Run an Uno application" },
                new { name = "publish", description = "Publish an Uno application" },
                new { name = "package", description = "Package Uno output artifacts" },
                new { name = "diagnostics", description = "Run Uno diagnostics and validation" },
                new { name = "env", description = "Inspect installed SDKs and tooling" },
                new { name = "devflow", description = "Query a running Uno DevFlow agent and inspect runtime state" },
                new { name = "commands", description = "List available commands in machine-readable form" },
                new { name = "batch", description = "Execute newline-delimited JSON command batches from stdin" },
            };

            Console.WriteLine(JsonSerializer.Serialize(new { commands }, new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }

        private static readonly JsonSerializerOptions BatchJsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        private static int RunBatch()
        {
            string? line;
            while ((line = Console.In.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                Console.Out.WriteLine(JsonSerializer.Serialize(ExecuteBatchLine(line), BatchJsonOptions));
            }

            return 0;
        }

        private static BatchResult ExecuteBatchLine(string line)
        {
            string[] argsArray;
            try
            {
                argsArray = JsonSerializer.Deserialize<string[]>(line) ?? Array.Empty<string>();
            }
            catch (JsonException ex)
            {
                return new BatchResult { Input = line, ExitCode = 1, Output = string.Empty, Error = $"Invalid batch line: {ex.Message}" };
            }

            var tokens = new Queue<string>(argsArray);
            var lineOptions = ParseGlobalOptions(tokens);
            var originalOut = Console.Out;
            var writer = new StringWriter();
            int exitCode;
            try
            {
                Console.SetOut(writer);
                exitCode = tokens.Count == 0
                    ? 1
                    : RunCommand(tokens.Dequeue().ToLowerInvariant(), tokens, lineOptions);
            }
            catch (Exception ex)
            {
                return new BatchResult { Input = line, ExitCode = 1, Output = writer.ToString(), Error = ex.Message };
            }
            finally
            {
                Console.SetOut(originalOut);
            }

            return new BatchResult { Input = line, ExitCode = exitCode, Output = writer.ToString() };
        }

        private sealed record BatchResult
        {
            public string? Input { get; init; }
            public int ExitCode { get; init; }
            public string Output { get; init; } = string.Empty;
            public string? Error { get; init; }
        }

        private static int UnknownCommand(string command)
        {
            Console.Error.WriteLine($"Unknown command: {command}");
            Console.Error.WriteLine("Run 'dotnet unolex --help' for available commands.");
            return 1;
        }

        private static void ShowHelp()
        {
            Console.WriteLine("LeXtudio.Uno.Cli - Uno command line utility");
            Console.WriteLine();
            Console.WriteLine("Usage: dotnet unolex [options] <command> [command-options]");
            Console.WriteLine();
            Console.WriteLine("Options:");
            Console.WriteLine("  --json      Emit structured JSON output");
            Console.WriteLine("  -v, --verbose  Enable verbose logging");
            Console.WriteLine("  --dry-run   Show what would be done without making changes");
            Console.WriteLine("  --ci        Run in CI-friendly non-interactive mode");
            Console.WriteLine("  --help, -h  Show help information");
            Console.WriteLine();
            Console.WriteLine("Commands:");
            Console.WriteLine("  doctor       Validate the Uno development environment");
            Console.WriteLine("  version      Display CLI and environment version information");
            Console.WriteLine("  new          Scaffold a new Uno app");
            Console.WriteLine("  build        Build an Uno project");
            Console.WriteLine("  run          Run an Uno application");
            Console.WriteLine("  publish      Publish an Uno application");
            Console.WriteLine("  package      Package Uno output artifacts");
            Console.WriteLine("  diagnostics  Run Uno diagnostics and validation");
            Console.WriteLine("  env          Inspect installed SDKs and tooling");
            Console.WriteLine("  devflow      Query a running Uno DevFlow agent and inspect runtime state");
            Console.WriteLine("    status     Show DevFlow agent status");
            Console.WriteLine("    screenshot Capture a screenshot from a running DevFlow agent");
            Console.WriteLine("    tap        Send a tap action to a running DevFlow agent");
            Console.WriteLine("  commands     List available commands in machine-readable form");
            Console.WriteLine("  batch        Execute newline-delimited JSON command batches from stdin");
        }
    }
}
