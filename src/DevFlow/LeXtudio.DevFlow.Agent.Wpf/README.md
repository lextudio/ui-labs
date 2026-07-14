# LeXtudio.DevFlow.Agent.Wpf

Windows WPF DevFlow runtime package for instrumenting classic WPF applications.

This package builds on `LeXtudio.DevFlow.Agent.Core` and adds the WPF visual tree walker, screenshot capture, and UI interaction support required for WPF application automation.

## Install

```powershell
dotnet add package LeXtudio.DevFlow.Agent.Wpf
```

## What is included

- WPF visual tree inspection
- live screenshot capture from the application window
- mouse/tap action support for WPF elements
- integration with the shared DevFlow HTTP API

## Usage

Register the WPF DevFlow agent in your application startup:

```csharp
using LeXtudio.DevFlow.Agent.Wpf;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        this.AddWpfDevFlowAgent();
    }
}
```

## Compatibility

- .NET 8.0+ on Windows
- WPF applications only
