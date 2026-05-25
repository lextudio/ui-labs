using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace LeXtudio.WinForms.Cli.UnitTests
{
    public class DevFlowTemporaryProjectTests
    {
        [Fact]
        public async Task TemporaryWinFormsProjectBuildsAndExposesDevFlowAgent()
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "LeXtudio.WinForms.Cli.DevFlowValidate", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);

            try
            {
                const string validationAppName = "WinFormsDevFlowValidationApp";
                const int validationPort = 5500;

                Assert.True(RunCommand("dotnet", $"new winforms --name {validationAppName} --output \"{tempRoot}\"", tempRoot, out var createOutput, out var createError), $"Failed to create temporary WinForms app:\n{createError}\n{createOutput}");

                var projectPath = Path.Combine(tempRoot, $"{validationAppName}.csproj");
                Assert.True(File.Exists(projectPath), $"Expected temporary project file not found: {projectPath}");

                var repoRoot = FindRepositoryRoot(Directory.GetCurrentDirectory());
                var agentProjectPath = Path.GetFullPath(Path.Combine(repoRoot, "src", "DevFlow", "LeXtudio.DevFlow.Agent.WinForms", "LeXtudio.DevFlow.Agent.WinForms.csproj"));
                Assert.True(File.Exists(agentProjectPath), $"Unable to find WinForms DevFlow agent project at {agentProjectPath}");

                InjectProjectReference(projectPath, agentProjectPath);
                File.WriteAllText(Path.Combine(tempRoot, "Program.cs"), GetProgramCsContents(validationPort), Encoding.UTF8);
                File.WriteAllText(Path.Combine(tempRoot, "MainForm.cs"), GetMainFormCsContents(), Encoding.UTF8);

                Assert.True(RunCommand("dotnet", $"build \"{projectPath}\" -c Debug", tempRoot, out var buildOutput, out var buildError), $"Temporary project build failed:\n{buildError}\n{buildOutput}");

                var targetFramework = GetTargetFrameworkFromProject(projectPath);
                var exePath = Path.Combine(tempRoot, "bin", "Debug", targetFramework, $"{validationAppName}.exe");
                Assert.True(File.Exists(exePath), $"Expected executable not found after build: {exePath}");

                using var process = StartHiddenProcess(exePath, tempRoot);
                Assert.NotNull(process);

                try
                {
                    var validated = await WaitForAgentAsync(validationPort, TimeSpan.FromSeconds(30));
                    Assert.True(validated, "The temporary WinForms app did not expose a valid DevFlow agent.");
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

        private static string GetProgramCsContents(int port)
        {
            return $"using System.Windows.Forms;\r\nusing LeXtudio.DevFlow.Agent.WinForms;\r\nusing Microsoft.Maui.DevFlow.Agent.Core;\r\n\r\nnamespace WinFormsDevFlowValidationApp;\r\n\r\ninternal static class Program\r\n{{\r\n    [STAThread]\r\n    private static void Main()\r\n    {{\r\n        ApplicationConfiguration.Initialize();\r\n\r\n        var form = new MainForm();\r\n        var context = new ApplicationContext(form);\r\n        context.AddWinFormsDevFlowAgent(new AgentOptions {{ Port = {port} }});\r\n\r\n        Application.Run(context);\r\n    }}\r\n}}\r\n";
        }

        private static string GetMainFormCsContents()
        {
            return "using System.Drawing;\r\nusing System.Windows.Forms;\r\n\r\nnamespace WinFormsDevFlowValidationApp;\r\n\r\ninternal sealed class MainForm : Form\r\n{\r\n    public MainForm()\r\n    {\r\n        Name = \"MainForm\";\r\n        Text = \"WinForms DevFlow Validation\";\r\n        Size = new Size(360, 220);\r\n        StartPosition = FormStartPosition.Manual;\r\n        Location = new Point(-2000, -2000);\r\n        ShowInTaskbar = false;\r\n\r\n        Controls.Add(new Label\r\n        {\r\n            Name = \"StatusLabel\",\r\n            Text = \"Ready\",\r\n            AutoSize = true,\r\n            Location = new Point(20, 20)\r\n        });\r\n\r\n        Controls.Add(new TextBox\r\n        {\r\n            Name = \"NameTextBox\",\r\n            Text = \"Initial\",\r\n            Location = new Point(20, 55),\r\n            Width = 180\r\n        });\r\n\r\n        Controls.Add(new Button\r\n        {\r\n            Name = \"SubmitButton\",\r\n            Text = \"Submit\",\r\n            Location = new Point(20, 95),\r\n            Width = 100\r\n        });\r\n    }\r\n}\r\n";
        }
    }
}
