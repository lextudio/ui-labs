# LeXtudio.DevFlow.Agent.LibreWpf

LibreWPF-specific DevFlow runtime package for instrumenting LibreWPF applications.

This package builds on `LeXtudio.DevFlow.Agent.Core` and shares source files with `LeXtudio.DevFlow.Agent.Wpf`, adding LibreWPF-specific rendering, input, and diagnostic support.

## Install

```powershell
dotnet add package LeXtudio.DevFlow.Agent.LibreWpf
```

## Usage

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

## Menu and popup diagnostics

The LibreWPF agent exposes a `wpf.menu-popup-diagnostics` invoke action for diagnosing popup placement, DPI conversion, monitor selection, mouse hit-testing, and LibreWPF menu logs from a running application.

## Related Packages

- [LeXtudio.DevFlow.Agent.Core](https://www.nuget.org/packages/LeXtudio.DevFlow.Agent.Core)
- [LeXtudio.DevFlow.Driver](https://www.nuget.org/packages/LeXtudio.DevFlow.Driver)
