using System.Windows.Forms;
using LeXtudio.DevFlow.Agent.WinForms;
using Microsoft.Maui.DevFlow.Agent.Core;

namespace LibreWinFormsDevFlowTestApp;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();

        var portText = Environment.GetEnvironmentVariable("DEVFLOW_AGENT_PORT");
        var port = int.TryParse(portText, out var p) && p > 0 ? p : AgentOptions.DefaultPort;

        var form = new MainForm();
        var context = new TestAppContext(form, port);
        Application.Run(context);
    }

    private sealed class TestAppContext : ApplicationContext
    {
        private readonly WinFormsAgentService _agent;

        public TestAppContext(Form form, int port)
        {
            _agent = this.AddWinFormsDevFlowAgent(new AgentOptions { Port = port });
            MainForm = form;
            form.FormClosed += (_, _) => ExitThread();
            form.Show();
        }

        protected override void ExitThreadCore()
        {
            _agent.StopAsync().GetAwaiter().GetResult();
            base.ExitThreadCore();
        }
    }
}