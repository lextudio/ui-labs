using System;
using System.Reflection;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Microsoft.Maui.DevFlow.Agent.Core;
using LeXtudio.DevFlow.Agent.MewUI;

var port = GetAgentPort();
var statusLabel = new Label
{
    Text = "Starting DevFlow...",
    Tag = "ResponseText"
};

var pressButton = new Button
{
    Tag = "ActionButton",
    Content = new Label { Text = "Press me" }
};
pressButton.Click += () => statusLabel.Text = "Button pressed.";

var window = new Window()
    .Title("MewUI DevFlow Test")
    .Resizable(900, 700)
    .Content(
        new StackPanel()
            .Padding(16)
            .Spacing(12)
            .Children(
                new Label
                {
                    Text = "MewUI DevFlow Sample",
                    FontSize = 24
                },
                pressButton,
                statusLabel,
                new Label { Text = $"Open http://localhost:{port}/api/v1/agent/status to verify DevFlow is running." }
            )
    )
    .OnLoaded(() =>
    {
        var agent = Application.Current.AddMewUIDevFlowAgent(new AgentOptions { Port = port });
        statusLabel.Text = $"DevFlow agent started on port {agent.Port}.";
    });

RegisterMewUIPlatformHost();

try
{
    Application.Run(window);
}
catch (Exception ex)
{
    Console.WriteLine($"[MewUIDevFlowTestApp] Application.Run failed: {ex}");
    throw;
}

static void RegisterMewUIPlatformHost()
{
    if (OperatingSystem.IsWindows())
    {
        TryRegisterPlatform("Aprillz.MewUI.Win32Platform, Aprillz.MewUI.Platform.Win32");
        TryRegisterPlatform("Aprillz.MewUI.Direct2DBackend, Aprillz.MewUI.Backend.Direct2D");
        Console.WriteLine("[MewUIDevFlowTestApp] Registered Aprillz.MewUI Win32 platform host.");
        return;
    }

    if (OperatingSystem.IsLinux())
    {
        TryRegisterPlatform("Aprillz.MewUI.X11Platform, Aprillz.MewUI.Platform.X11");
        TryRegisterPlatform("Aprillz.MewUI.MewVGX11Backend, Aprillz.MewUI.Backend.MewVG.X11");
        Console.WriteLine("[MewUIDevFlowTestApp] Registered Aprillz.MewUI X11 platform host.");
        return;
    }

    if (OperatingSystem.IsMacOS())
    {
        TryRegisterPlatform("Aprillz.MewUI.MacOSPlatform, Aprillz.MewUI.Platform.MacOS");
        TryRegisterPlatform("Aprillz.MewUI.MewVGMacOSBackend, Aprillz.MewUI.Backend.MewVG.MacOS");
        Console.WriteLine("[MewUIDevFlowTestApp] Registered Aprillz.MewUI MacOS platform host.");
        return;
    }

    Console.WriteLine("[MewUIDevFlowTestApp] No explicit MewUI platform host registration performed for this OS.");
}

static void TryRegisterPlatform(string typeName)
{
    var type = Type.GetType(typeName, throwOnError: false, ignoreCase: false);
    if (type == null)
    {
        Console.WriteLine($"[MewUIDevFlowTestApp] Platform registration type not found: {typeName}");
        return;
    }

    var registerMethod = type.GetMethod("Register", BindingFlags.Public | BindingFlags.Static);
    if (registerMethod == null)
    {
        Console.WriteLine($"[MewUIDevFlowTestApp] Register method not found on {typeName}");
        return;
    }

    registerMethod.Invoke(null, null);
}

static int GetAgentPort()
{
    var portValue = Environment.GetEnvironmentVariable("DEVFLOW_AGENT_PORT");
    return int.TryParse(portValue, out var port) && port > 0 ? port : 5500;
}
