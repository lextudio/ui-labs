using System;
using System.Collections.Generic;
using System.Text.Json;
using LeXtudio.Wpf.DevFlow.Driver;

namespace LeXtudio.Wpf.Cli
{
    public static class CommandHandlers
    {
        public static int RunDoctor(OutputOptions options)
        {
            return WriteResult("doctor", "Validate the WPF development environment.", options);
        }

        public static int RunVersion(OutputOptions options)
        {
            return WriteResult("version", "Display CLI and environment version information.", options);
        }

        public static int RunNew(Queue<string> tokens, OutputOptions options)
        {
            var target = tokens.Count > 0 ? tokens.Dequeue() : "app";
            return WriteResult("new", $"Scaffold a new WPF {target}.", options);
        }

        public static int RunBuild(Queue<string> _, OutputOptions options)
        {
            return WriteResult("build", "Build a WPF project with WPF-aware defaults.", options);
        }

        public static int RunRun(Queue<string> _, OutputOptions options)
        {
            return WriteResult("run", "Run a WPF application.", options);
        }

        public static int RunPublish(Queue<string> _, OutputOptions options)
        {
            return WriteResult("publish", "Publish a WPF application for deployment.", options);
        }

        public static int RunPackage(Queue<string> _, OutputOptions options)
        {
            return WriteResult("package", "Package WPF output artifacts.", options);
        }

        public static int RunDiagnostics(Queue<string> _, OutputOptions options)
        {
            return WriteResult("diagnostics", "Run WPF-specific diagnostics and validation.", options);
        }

        public static int RunEnv(Queue<string> _, OutputOptions options)
        {
            return WriteResult("env", "Inspect installed SDKs and tooling.", options);
        }

        public static int RunDevFlow(Queue<string> tokens, OutputOptions options)
        {
            if (tokens.Count == 0)
            {
                return WriteResult("devflow", "Usage: wpf devflow status [--host <host>] [--port <port>]", options);
            }

            var subcommand = tokens.Dequeue().ToLowerInvariant();
            return subcommand switch
            {
                "status" => RunDevFlowStatus(tokens, options),
                "help" => WriteResult("devflow", "Usage: wpf devflow status [--host <host>] [--port <port>]", options),
                "--help" => WriteResult("devflow", "Usage: wpf devflow status [--host <host>] [--port <port>]", options),
                "-h" => WriteResult("devflow", "Usage: wpf devflow status [--host <host>] [--port <port>]", options),
                _ => UnknownDevFlowSubcommand(subcommand)
            };
        }

        private static int RunDevFlowStatus(Queue<string> tokens, OutputOptions options)
        {
            var host = "localhost";
            var port = 5500;

            while (tokens.Count > 0)
            {
                var token = tokens.Dequeue().ToLowerInvariant();
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

                Console.Error.WriteLine($"Unknown option: {token}");
                return 1;
            }

            using var client = new AgentClient(host, port);
            AgentStatus? status;
            try
            {
                status = client.GetStatusAsync().GetAwaiter().GetResult();
            }
            catch (HttpRequestException ex)
            {
                return WriteResult("devflow", $"Unable to connect to WPF DevFlow agent at {host}:{port}. {ex.Message}", options);
            }
            catch (TaskCanceledException)
            {
                return WriteResult("devflow", $"Timed out while connecting to WPF DevFlow agent at {host}:{port}.", options);
            }

            if (status == null)
            {
                return WriteResult("devflow", "Unable to retrieve agent status. Ensure a WPF DevFlow agent is running on the specified host and port.", options);
            }

            if (options.Json)
            {
                Console.WriteLine(JsonSerializer.Serialize(status, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
                return 0;
            }

            Console.WriteLine("WPF DevFlow agent status:");
            Console.WriteLine($"  Name:        {status.Name}");
            Console.WriteLine($"  Id:          {status.Id}");
            Console.WriteLine($"  Framework:   {status.Framework}");
            Console.WriteLine($"  Version:     {status.Version}");
            Console.WriteLine($"  Application: {status.Application}");
            Console.WriteLine($"  Running:     {status.Running}");
            Console.WriteLine($"  Port:        {status.Port}");

            return 0;
        }

        private static int UnknownDevFlowSubcommand(string subcommand)
        {
            Console.Error.WriteLine($"Unknown devflow subcommand: {subcommand}");
            Console.Error.WriteLine("Run 'wpf devflow --help' for available commands.");
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
