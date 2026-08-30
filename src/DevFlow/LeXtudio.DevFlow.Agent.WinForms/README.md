# LeXtudio.DevFlow.Agent.WinForms

WinForms-specific DevFlow runtime package for instrumenting classic WinForms applications.

This package builds on `LeXtudio.DevFlow.Agent.Core` and adds the WinForms visual tree walker, screenshot capture, and UI interaction support required for WinForms application automation.

## Install

```powershell
dotnet add package LeXtudio.DevFlow.Agent.WinForms
```

## What is included

- WinForms visual tree inspection
- live screenshot capture from the application window
- element and selector screenshot capture
- mouse/tap action support for WinForms elements
- text input, clear, focus, key, and scroll actions for supported controls
- integration with the shared DevFlow HTTP API

## Usage

Register the WinForms DevFlow agent in your application startup:

```csharp
using System.Windows.Forms;
using LeXtudio.DevFlow.Agent.WinForms;
using Microsoft.Maui.DevFlow.Agent.Core;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        var form = new MainForm();
        var context = new ApplicationContext(form);
        context.AddWinFormsDevFlowAgent(new AgentOptions { Port = 9223 });

        Application.Run(context);
    }
}
```

## Getting Started

For a complete sample, see `WinFormsDevFlowTestApp` in this repository.

For common agent endpoints and port configuration notes, see the top-level DevFlow README.

## Related Packages

- [LeXtudio.DevFlow.Agent.Core](https://www.nuget.org/packages/LeXtudio.DevFlow.Agent.Core)
- [LeXtudio.DevFlow.Driver](https://www.nuget.org/packages/LeXtudio.DevFlow.Driver)
- [LeXtudio.DevFlow.Agent.WPF](https://www.nuget.org/packages/LeXtudio.DevFlow.Agent.WPF)
- [LeXtudio.DevFlow.Agent.Uno](https://www.nuget.org/packages/LeXtudio.DevFlow.Agent.Uno)
- [LeXtudio.DevFlow.Agent.MewUI](https://www.nuget.org/packages/LeXtudio.DevFlow.Agent.MewUI)

## Compatibility

- .NET 10.0+ on Windows
- WinForms applications only
