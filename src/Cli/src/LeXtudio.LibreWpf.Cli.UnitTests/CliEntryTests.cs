using System;
using System.IO;
using Xunit;

namespace LeXtudio.LibreWpf.Cli.UnitTests;

public class CliEntryTests
{
    [Fact]
    public void Main_NoArgs_ReturnsZero()
    {
        var exitCode = Program.Main(Array.Empty<string>());
        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void Main_HelpArg_ReturnsZero()
    {
        var exitCode = Program.Main(new[] { "--help" });
        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void Main_UnknownCommand_ReturnsOne()
    {
        var exitCode = Program.Main(new[] { "unknown-command" });
        Assert.Equal(1, exitCode);
    }

    [Fact]
    public void Main_VersionCommand_ReturnsZero()
    {
        var exitCode = Program.Main(new[] { "version" });
        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void Main_DoctorCommand_ReturnsZero()
    {
        var exitCode = Program.Main(new[] { "doctor" });
        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void RunNew_CreatesProjectFiles()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var originalDir = Environment.CurrentDirectory;
            Environment.CurrentDirectory = tempDir;
            try
            {
                var exitCode = Program.Main(new[] { "new", "TestLibreWpfApp" });
                Assert.Equal(0, exitCode);
                Assert.True(File.Exists(Path.Combine(tempDir, "TestLibreWpfApp", "TestLibreWpfApp.csproj")));
                Assert.True(File.Exists(Path.Combine(tempDir, "TestLibreWpfApp", "Program.cs")));

                var csproj = File.ReadAllText(Path.Combine(tempDir, "TestLibreWpfApp", "TestLibreWpfApp.csproj"));
                Assert.Contains("LibreWPF.Sdk", csproj);
            }
            finally
            {
                Environment.CurrentDirectory = originalDir;
            }
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void Main_JsonFlag_EmitsJson()
    {
        var output = new StringWriter();
        var originalOut = Console.Out;
        Console.SetOut(output);
        try
        {
            var exitCode = Program.Main(new[] { "--json", "version" });
            Assert.Equal(0, exitCode);
            var result = output.ToString();
            Assert.Contains("\"command\"", result);
            Assert.Contains("\"message\"", result);
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    [Fact]
    public void Main_DryRun_DoesNotCreateFiles()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var originalDir = Environment.CurrentDirectory;
            Environment.CurrentDirectory = tempDir;
            try
            {
                var exitCode = Program.Main(new[] { "--dry-run", "new", "DryRunApp" });
                Assert.Equal(0, exitCode);
                Assert.False(Directory.Exists(Path.Combine(tempDir, "DryRunApp")));
            }
            finally
            {
                Environment.CurrentDirectory = originalDir;
            }
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }
}
