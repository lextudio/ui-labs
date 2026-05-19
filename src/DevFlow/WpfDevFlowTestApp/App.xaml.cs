using System;
using System.Windows;
using Microsoft.Maui.DevFlow.Agent.Core;
using LeXtudio.Wpf.DevFlow.Agent.WPF;

namespace WpfDevFlowTestApp;

public partial class App : Application
{
    private WpfAgentService? _devFlowService;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var port = 5500;
        var portValue = Environment.GetEnvironmentVariable("DEVFLOW_AGENT_PORT");
        if (!string.IsNullOrWhiteSpace(portValue) && int.TryParse(portValue, out var parsedPort))
        {
            port = parsedPort;
        }

        _devFlowService = this.AddWpfDevFlowAgent(new AgentOptions
        {
            Port = port
        });

        var hideWindow = false;
        var hideWindowValue = Environment.GetEnvironmentVariable("DEVFLOW_HIDE_WINDOW");
        if (!string.IsNullOrWhiteSpace(hideWindowValue) && bool.TryParse(hideWindowValue, out var parsedHide))
        {
            hideWindow = parsedHide;
        }

        if (hideWindow && MainWindow != null)
        {
            MainWindow.Visibility = Visibility.Hidden;
            MainWindow.ShowInTaskbar = false;
        }
    }
}
