# WPF DevFlow

A WPF-first DevFlow product designed for classic WPF applications.

This folder contains a new local WPF DevFlow implementation that reuses generic DevFlow infrastructure from the external MAUI DevFlow repository where it makes sense.

## What is included

- `LeXtudio.Wpf.DevFlow.Agent.Core` — WPF-agnostic DevFlow HTTP server, DTOs, and shared agent plumbing
- `LeXtudio.Wpf.DevFlow.Agent.WPF` — WPF-specific visual tree walker, screenshot capture, and UI interaction support

## Reuse strategy

The WPF DevFlow projects reuse existing source from `external/maui-labs/src/DevFlow/Microsoft.Maui.DevFlow.Agent.Core` for:

- HTTP server (`AgentHttpServer.cs`)
- protocol DTOs and element model (`ElementInfo.cs`)
- agent configuration (`AgentOptions.cs`)
- DevFlow action attribute metadata (`DevFlowActionAttribute.cs`)

Those files are consumed as linked source files in the local `LeXtudio.Wpf.DevFlow.Agent.Core` project.

## Build

From the repo root:

```powershell
cd src\DevFlow
dotnet build WpfDevFlow.sln
```

## Use

WPF apps can register the agent during startup:

```csharp
using LeXtudio.Wpf.DevFlow.Agent.WPF;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        this.AddWpfDevFlowAgent();
    }
}
```

## Notes

- The WPF DevFlow product is intentionally focused on classic WPF, not MAUI.
- `WpfDevFlow.sln` is the local WPF solution for this product.
- `DevFlow.slnf` is an existing external MAUI DevFlow wrapper and is unrelated to the new local WPF product.
