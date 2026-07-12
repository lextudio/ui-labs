# LeXtudio.DevFlow.Agent.WPF

WPF-specific DevFlow runtime package for instrumenting classic WPF applications.

This package builds on `LeXtudio.DevFlow.Agent.Core` and adds the WPF visual tree walker, screenshot capture, and UI interaction support required for WPF application automation.

## Install

```powershell
dotnet add package LeXtudio.DevFlow.Agent.WPF
```

## What is included

- WPF visual tree inspection
- live screenshot capture from the application window
- mouse/tap action support for WPF elements
- menu and popup diagnostics through DevFlow invoke actions
- integration with the shared DevFlow HTTP API

## Usage

Register the WPF DevFlow agent in your application startup:

```csharp
using LeXtudio.DevFlow.Agent.WPF;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        this.AddWpfDevFlowAgent();
    }
}
```

## Menu and popup diagnostics

The WPF agent exposes a `wpf.menu-popup-diagnostics` invoke action for diagnosing popup placement, DPI conversion, monitor selection, mouse hit-testing, and LibreWPF menu logs from a running application:

```bash
curl -X POST http://127.0.0.1:5252/api/v1/invoke/actions/wpf.menu-popup-diagnostics \
  -H 'Content-Type: application/json' \
  -d '{"args":[300,32]}'
```

The action returns a JSON snapshot string in `returnValue`. The optional arguments are `maxLogLines` and `maxVisualDepth`.

## Getting Started

For detailed setup and configuration instructions, see the [WPF DevFlow Guide](https://github.com/lextudio/wpf-labs/blob/master/docs/devflow/howto-wpf.md).

For common questions about port configuration and troubleshooting, see the [DevFlow FAQ](https://github.com/lextudio/wpf-labs/blob/master/docs/devflow/faq.md).

## Related Packages

- [LeXtudio.DevFlow.Agent.Core](https://www.nuget.org/packages/LeXtudio.DevFlow.Agent.Core)
- [LeXtudio.DevFlow.Driver](https://www.nuget.org/packages/LeXtudio.DevFlow.Driver)
- [LeXtudio.DevFlow.Agent.Uno](https://www.nuget.org/packages/LeXtudio.DevFlow.Agent.Uno)
- [LeXtudio.DevFlow.Agent.MewUI](https://www.nuget.org/packages/LeXtudio.DevFlow.Agent.MewUI)

## Compatibility

- .NET 8.0+ on Windows
- WPF applications only
