# WPF DevFlow

A WPF-first DevFlow product designed for classic WPF applications.

This folder contains the shared DevFlow runtime packages for WPF, WinUI 3, and Uno Platform applications.

## NuGet packages

[![LeXtudio.DevFlow.Agent.Core](https://img.shields.io/nuget/v/LeXtudio.DevFlow.Agent.Core.svg)](https://www.nuget.org/packages/LeXtudio.DevFlow.Agent.Core)
[![LeXtudio.DevFlow.Agent.WPF](https://img.shields.io/nuget/v/LeXtudio.DevFlow.Agent.WPF.svg)](https://www.nuget.org/packages/LeXtudio.DevFlow.Agent.WPF)
[![LeXtudio.DevFlow.Agent.Uno](https://img.shields.io/nuget/v/LeXtudio.DevFlow.Agent.Uno.svg)](https://www.nuget.org/packages/LeXtudio.DevFlow.Agent.Uno)
[![LeXtudio.DevFlow.Driver](https://img.shields.io/nuget/v/LeXtudio.DevFlow.Driver.svg)](https://www.nuget.org/packages/LeXtudio.DevFlow.Driver)

Install the runtime package for your UI stack:

```powershell
dotnet add package LeXtudio.DevFlow.Agent.WPF
dotnet add package LeXtudio.DevFlow.Driver
```

```powershell
dotnet add package LeXtudio.DevFlow.Agent.Uno
dotnet add package LeXtudio.DevFlow.Driver
```

## What is included

- `LeXtudio.DevFlow.Agent.Core` — WPF-agnostic DevFlow HTTP server, DTOs, and shared agent plumbing
- `LeXtudio.DevFlow.Agent.WPF` — WPF-specific visual tree walker, screenshot capture, and UI interaction support
- `LeXtudio.DevFlow.Agent.Uno` — Uno Platform and WinUI 3 registration and visual tree support
- `LeXtudio.DevFlow.Agent.MewUI` — MewUI runtime support via NuGet-deployed Aprillz.MewUI packages
- `LeXtudio.DevFlow.Driver` — HTTP client for querying a running DevFlow agent

## Build

From the repo root:

```powershell
cd src\DevFlow
dotnet build WpfDevFlow.sln
```

## Use

WPF apps can register the agent during startup:

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

Uno Platform and WinUI 3 apps can register the Uno agent:

```csharp
using LeXtudio.DevFlow.Agent.Uno;

builder.UseUnoDevFlowAgent();
```

## Web API

By default, the sample apps start the agent on port `5500`.

| Request | Description |
|---------|-------------|
| `GET /api/v1/agent/status` | Read agent status. |
| `GET /api/v1/ui/tree` | Read the live UI tree. |
| `GET /api/v1/ui/element?id=<id>` | Read one UI element by id. |
| `GET /api/v1/ui/screenshot` | Capture a screenshot. |
| `POST /api/v1/ui/tap` | Tap an element with body `{ "id": "<element-id>" }`. |
| `POST /api/v1/ui/actions/scroll` | Scroll an element with body `{ "id": "<element-id>", "deltaX": 0, "deltaY": 600 }`. |

## Uno support preview

- `LeXtudio.DevFlow.Agent.Uno` is the Uno DevFlow platform package.
- `UnoDevFlow.sln` contains the shared agent core plus the Uno project.
- The Uno package supports registration, tree walking, screenshots, tap, and scroll through the shared Web API.

## MewUI support preview

- `LeXtudio.DevFlow.Agent.MewUI` is a new MewUI runtime package that uses `Aprillz.MewUI.Core` and `Aprillz.MewUI.Platform.Win32`.
- `MewUIDevFlowTestApp` is a reference sample project demonstrating how to start a MewUI app and host the DevFlow HTTP agent.
- Register DevFlow during your app startup with `Application.Current.AddMewUIDevFlowAgent()`.

## Reuse strategy

The DevFlow projects reuse source from `external/maui-labs/src/DevFlow/Microsoft.Maui.DevFlow.Agent.Core` where it makes sense. Those files are consumed as linked source files in `LeXtudio.DevFlow.Agent.Core`.

## Notes

- The WPF DevFlow product is focused on WPF, WinUI 3, and Uno Platform, not MAUI.
- `WpfDevFlow.sln` is the local WPF solution for this product.
- `DevFlow.slnf` is an existing external MAUI DevFlow wrapper and is unrelated to the new local WPF product.
