using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace LeXtudio.Wpf.Cli.UnitTests
{
    public class DevFlowTemporaryProjectTests
    {
        [Fact]
        public async Task TemporaryWpfProjectBuildsAndExposesDevFlowAgent()
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "LeXtudio.Wpf.Cli.DevFlowValidate", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);

            try
            {
                const string validationAppName = "WpfDevFlowValidationApp";
                const int validationPort = 5500;

                Assert.True(RunCommand("dotnet", $"new wpf --name {validationAppName} --output \"{tempRoot}\"", tempRoot, out var createOutput, out var createError), $"Failed to create temporary WPF app:\n{createError}\n{createOutput}");

                var projectPath = Path.Combine(tempRoot, $"{validationAppName}.csproj");
                Assert.True(File.Exists(projectPath), $"Expected temporary project file not found: {projectPath}");

                var repoRoot = FindRepositoryRoot(Directory.GetCurrentDirectory());
                var agentProjectPath = Path.GetFullPath(Path.Combine(repoRoot, "src", "DevFlow", "LeXtudio.DevFlow.Agent.WPF", "LeXtudio.DevFlow.Agent.WPF.csproj"));
                Assert.True(File.Exists(agentProjectPath), $"Unable to find WPF DevFlow agent project at {agentProjectPath}");

                InjectProjectReference(projectPath, agentProjectPath);
                File.WriteAllText(Path.Combine(tempRoot, "App.xaml"), GetAppXamlContents(), Encoding.UTF8);
                File.WriteAllText(Path.Combine(tempRoot, "App.xaml.cs"), GetAppCsContents(validationPort), Encoding.UTF8);

                Assert.True(RunCommand("dotnet", $"build \"{projectPath}\" -c Debug", tempRoot, out var buildOutput, out var buildError), $"Temporary project build failed:\n{buildError}\n{buildOutput}");

                var targetFramework = GetTargetFrameworkFromProject(projectPath);
                var exePath = Path.Combine(tempRoot, "bin", "Debug", targetFramework, $"{validationAppName}.exe");
                Assert.True(File.Exists(exePath), $"Expected executable not found after build: {exePath}");

                using var process = StartHiddenProcess(exePath, tempRoot);
                Assert.NotNull(process);

                try
                {
                    var validated = await WaitForAgentAsync(validationPort, TimeSpan.FromSeconds(30));
                    Assert.True(validated, "The temporary WPF app did not expose a valid DevFlow agent.");
                }
                finally
                {
                    if (!process.HasExited)
                    {
                        process.Kill(true);
                        process.WaitForExit(5000);
                    }
                }
            }
            finally
            {
                if (Directory.Exists(tempRoot))
                {
                    try
                    {
                        Directory.Delete(tempRoot, true);
                    }
                    catch
                    {
                    }
                }
            }
        }

        private static string FindRepositoryRoot(string startFolder)
        {
            var current = new DirectoryInfo(startFolder);
            while (current != null)
            {
                if (Directory.Exists(Path.Combine(current.FullName, "src", "DevFlow")))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }

            throw new InvalidOperationException("Unable to locate the repository root containing src/DevFlow.");
        }

        private static void InjectProjectReference(string csprojPath, string agentProjectPath)
        {
            var csprojText = File.ReadAllText(csprojPath);
            if (csprojText.Contains("<ProjectReference Include=\""))
            {
                return;
            }

            var insertMarker = "</PropertyGroup>";
            var insertIndex = csprojText.IndexOf(insertMarker, StringComparison.Ordinal);
            if (insertIndex < 0)
            {
                throw new InvalidOperationException("Unable to inject project reference into temporary project file.");
            }

            var referenceBlock = $"\r\n  <ItemGroup>\r\n    <ProjectReference Include=\"{agentProjectPath}\" />\r\n  </ItemGroup>\r\n";
            csprojText = csprojText.Insert(insertIndex + insertMarker.Length, referenceBlock);
            File.WriteAllText(csprojPath, csprojText, Encoding.UTF8);
        }

        private static string GetTargetFrameworkFromProject(string csprojPath)
        {
            var csprojText = File.ReadAllText(csprojPath);
            var marker = "<TargetFramework>";
            var startIndex = csprojText.IndexOf(marker, StringComparison.Ordinal);
            if (startIndex < 0)
            {
                throw new InvalidOperationException("Could not determine TargetFramework from temporary project file.");
            }

            startIndex += marker.Length;
            var endIndex = csprojText.IndexOf("</TargetFramework>", startIndex, StringComparison.Ordinal);
            return csprojText[startIndex..endIndex].Trim();
        }

        private static Process StartHiddenProcess(string exePath, string workingDirectory)
        {
            return Process.Start(new ProcessStartInfo(exePath)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = workingDirectory
            })!;
        }

        private static async Task<bool> WaitForAgentAsync(int port, TimeSpan timeout)
        {
            using var client = new HttpClient();
            var deadline = DateTime.UtcNow + timeout;
            var statusUri = new Uri($"http://localhost:{port}/api/v1/agent/status");
            var treeUri = new Uri($"http://localhost:{port}/api/v1/ui/tree");

            while (DateTime.UtcNow < deadline)
            {
                try
                {
                    using var statusResponse = await client.GetAsync(statusUri);
                    if (!statusResponse.IsSuccessStatusCode)
                    {
                        await Task.Delay(500);
                        continue;
                    }

                    using var statusDoc = JsonDocument.Parse(await statusResponse.Content.ReadAsStreamAsync());
                    if (!statusDoc.RootElement.TryGetProperty("running", out var running) || !running.GetBoolean())
                    {
                        await Task.Delay(500);
                        continue;
                    }

                    using var treeResponse = await client.GetAsync(treeUri);
                    if (!treeResponse.IsSuccessStatusCode)
                    {
                        await Task.Delay(500);
                        continue;
                    }

                    using var treeDoc = JsonDocument.Parse(await treeResponse.Content.ReadAsStreamAsync());
                    if (treeDoc.RootElement.TryGetProperty("elements", out var elements) && elements.GetArrayLength() > 0)
                    {
                        return true;
                    }
                }
                catch
                {
                    await Task.Delay(500);
                }
            }

            return false;
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

            var stdout = new StringBuilder();
            var stderr = new StringBuilder();
            process.OutputDataReceived += (_, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };

            if (!process.Start())
            {
                output = string.Empty;
                error = "Failed to start process.";
                return false;
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            process.WaitForExit();

            output = stdout.ToString();
            error = stderr.ToString();
            return process.ExitCode == 0;
        }

        private static string GetAppXamlContents()
        {
            return "<Application x:Class=\"WpfDevFlowValidationApp.App\"\r\n             xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"\r\n             xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">\r\n</Application>\r\n";
        }

        private static string GetAppCsContents(int port)
        {
            return $"using System.Windows;\r\nusing LeXtudio.DevFlow.Agent.Wpf;\r\nusing Microsoft.Maui.DevFlow.Agent.Core;\r\n\r\nnamespace WpfDevFlowValidationApp;\r\n\r\npublic partial class App : Application\r\n{{\r\n    private const int ValidationPort = {port};\r\n    private WpfAgentService? _devFlowService;\r\n\r\n    public App()\r\n    {{\r\n        Startup += OnStartup;\r\n    }}\r\n\r\n    private void OnStartup(object? sender, StartupEventArgs e)\r\n    {{\r\n        _devFlowService = this.AddWpfDevFlowAgent(new AgentOptions {{ Port = ValidationPort }});\r\n        var hiddenWindow = new MainWindow\r\n        {{\r\n            Visibility = Visibility.Hidden,\r\n            ShowInTaskbar = false\r\n        }};\r\n        hiddenWindow.Show();\r\n    }}\r\n}}\r\n";
        }
    }
}
