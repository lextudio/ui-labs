using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using LeXtudio.DevFlow.Agent.Avalonia;
using LeXtudio.DevFlow.Agent.Core;
using Microsoft.Maui.DevFlow.Agent.Core;

namespace AvaloniaDevFlowTestApp;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            return AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AvaloniaDevFlowTestApp] Startup failed: {ex}");
            throw;
        }
    }

    internal static int GetAgentPort()
    {
        var portValue = Environment.GetEnvironmentVariable("DEVFLOW_AGENT_PORT");
        if (int.TryParse(portValue, out var port) && port > 0)
            return port;

        return DevFlowAgentPortResolver.GetPortFromAssemblyMetadata() ?? AgentOptions.DefaultPort;
    }
}

public sealed class App : Application
{
    private AvaloniaAgentService? _agent;

    public override void Initialize()
    {
        Name = "AvaloniaDevFlowTestApp";
        Styles.Add(new FluentTheme());
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow();
            desktop.MainWindow = window;
            desktop.Exit += (_, _) => _agent?.Dispose();

            _agent = this.AddAvaloniaDevFlowAgent(new AgentOptions { Port = Program.GetAgentPort() });
            window.SetStatus($"DevFlow agent started on port {_agent.Port}.");
            Console.WriteLine($"[AvaloniaDevFlowTestApp] DevFlow agent started on port {_agent.Port}.");
        }

        base.OnFrameworkInitializationCompleted();
    }
}
