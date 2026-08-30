# Adopt DevFlow in a WPF Project

This guide shows how to add DevFlow to an existing WPF app and verify it is working.

## 1. Prerequisites

- .NET 10.0
- A WPF app project on Windows
- App can run locally

## 2. Add NuGet packages

From your WPF app folder:

```powershell
dotnet add package LeXtudio.DevFlow.Agent.WPF
dotnet add package LeXtudio.DevFlow.Driver
```

`LeXtudio.DevFlow.Agent.WPF` brings in the shared core package automatically.

## 3. Register DevFlow at app startup

Update `App.xaml.cs`:

```csharp
using System;
using System.Windows;
using Microsoft.Maui.DevFlow.Agent.Core;
using LeXtudio.DevFlow.Agent.WPF;

namespace YourAppNamespace;

public partial class App : Application
{
    private WpfAgentService? _devFlowService;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _devFlowService = this.AddWpfDevFlowAgent();
    }
}
```

Recommended: keep this inside debug-only logic if you do not want the local HTTP agent in release runs.

## 4. Build and run

```powershell
dotnet build
dotnet run
```

## 5. Verify the agent

Check status:

```powershell
Invoke-WebRequest http://localhost:9223/api/v1/agent/status | Select-Object -ExpandProperty Content
```

If you configured a custom port, replace `9223`.

## 6. What to expect after adoption

After startup, your app hosts a local DevFlow HTTP agent with these endpoints:

- `GET /api/v1/agent/status`
- `GET /api/v1/ui/tree`
- `GET /api/v1/ui/element?id=<id>`
- `GET /api/v1/ui/screenshot`
- `POST /api/v1/ui/tap`
- `POST /api/v1/ui/actions/scroll`

Practically, this means tooling can inspect the live UI tree, capture screenshots, and drive supported UI actions through HTTP while your app is running.

## 7. Optional port configuration

- `MauiDevFlowPort`: override the agent port at build time with `dotnet build -p:MauiDevFlowPort=5600`.
- `.mauidevflow`: create a file in the project directory with:

```json
{
  "port": 5600
}
```

- `DEVFLOW_HIDE_WINDOW`: for automation scenarios, hides the main window when set to `true`.

The agent defaults to port `9223` when no custom port is configured.

## 8. Recommended practice

Like MAUI onboarding guidance, keep DevFlow registration in debug-only code unless you explicitly need it in non-debug runs.
