# Adopt DevFlow in an Avalonia Project

This guide shows how to add DevFlow to an existing [Avalonia](https://avaloniaui.net/) desktop app.

## 1. Prerequisites

- .NET 10.0 or later (matching current package compatibility)
- An Avalonia 12 desktop app (`IClassicDesktopStyleApplicationLifetime`) that already runs on your target OS

## 2. Add NuGet packages

From your app project folder:

```powershell
dotnet add package LeXtudio.DevFlow.Agent.Avalonia
dotnet add package LeXtudio.DevFlow.Driver
```

## 3. Register DevFlow after the app is initialized

In your `Application` class:

```csharp
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using LeXtudio.DevFlow.Agent.Avalonia;

public override void OnFrameworkInitializationCompleted()
{
    if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        desktop.MainWindow = new MainWindow();

    var agent = this.AddAvaloniaDevFlowAgent();
    base.OnFrameworkInitializationCompleted();
}
```

Give the controls you want to address a `Name` (`x:Name` in XAML); the agent reports it as the element id.

## 4. Build and run

```powershell
dotnet build
dotnet run
```

## 5. Verify the agent

```powershell
Invoke-WebRequest http://localhost:9223/api/v1/agent/status | Select-Object -ExpandProperty Content
```

The status reports `"framework": "avalonia"`.

## 6. What to expect after adoption

When your Avalonia app is running, DevFlow hosts a local HTTP API that can:

- expose agent status
- expose the live UI tree of every open window
- fetch a specific element by id
- capture window and element screenshots
- perform tap, fill, clear, focus, key, scroll and back actions
- get and set the application theme
- invoke `[DevFlowAction]` methods on the application, its windows and their data contexts
- inject OS-level pointer input: click, move, drag, and press / drag-move / release for drags that are inspected step by step

Routes:

- `GET /api/v1/agent/status`
- `GET /api/v1/ui/tree`
- `GET /api/v1/ui/elements/<id>`
- `GET /api/v1/ui/screenshot`
- `POST /api/v1/ui/actions/tap`
- `POST /api/v1/ui/actions/scroll`
- `POST /api/v1/ui/actions/press`, `/drag-move`, `/release`
- `GET /api/v1/invoke/actions`, `POST /api/v1/invoke/actions/<name>`

Mutating requests need a mutation lease; `LeXtudio.DevFlow.Driver` acquires it for you.

## 7. Coordinates and native input

Element bounds are device independent units relative to the element's window; a window's bounds are its screen position. The global input actions take screen pixels, which is what `Visual.PointToScreen` returns.

Native pointer input uses XTest on Linux (X11 or XWayland), SendInput on Windows (the window has to be in the foreground) and cliclick on macOS (which needs Accessibility permission).

## 8. Optional port configuration

- `MauiDevFlowPort`: override the agent port at build time with `dotnet build -p:MauiDevFlowPort=9500`.
- `AgentOptions.Port`: pass a port in code, `this.AddAvaloniaDevFlowAgent(new AgentOptions { Port = 9500 })`.

The agent defaults to port `9223` when no custom port is configured.

## 9. Recommended practice

Keep DevFlow registration in debug-only code unless you explicitly need it in non-debug runs.
