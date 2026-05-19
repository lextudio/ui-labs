using System;
using System.IO;
using Xunit;

namespace LeXtudio.Wpf.Cli.UnitTests
{
    public class CliEntryTests
    {
        [Fact]
        public void NoArguments_ShowsHelp()
        {
            var output = CaptureConsole(() => Program.Main(Array.Empty<string>()));
            Assert.Contains("LeXtudio.Wpf.Cli - WPF command line utility", output);
        }

        [Fact]
        public void DoctorCommand_PrintsDoctorMessage()
        {
            var output = CaptureConsole(() => Program.Main(new[] { "doctor" }));
            Assert.Contains("Validate the WPF development environment.", output);
        }

        [Fact]
        public void JsonMode_OutputsJsonPayload()
        {
            var output = CaptureConsole(() => Program.Main(new[] { "--json", "version" }));
            Assert.Contains("\"command\":\"version\"", output);
        }

        private static string CaptureConsole(Func<int> action)
        {
            var stdout = Console.Out;
            try
            {
                using var writer = new StringWriter();
                Console.SetOut(writer);
                action();
                return writer.ToString();
            }
            finally
            {
                Console.SetOut(stdout);
            }
        }
    }
}
