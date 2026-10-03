# Adopt DevFlow in a Jalium Project

This guide shows how to add DevFlow to an existing Jalium app.

## 1. Prerequisites

- .NET 10.0 or later
- A Jalium app that already runs on your target OS

## 2. Add NuGet packages

From your app project folder:

```powershell
dotnet add package LeXtudio.DevFlow.Agent.Jalium
dotnet add package LeXtudio.DevFlow.Driver
```

## 3. Register DevFlow after app starts

In your app startup flow:

```csharp
using LeXtudio.Jalium;
using Microsoft.Maui.DevFlow.Agent.Core;
using LeXtudio.DevFlow.Agent.Jalium;

var app = new Application();
app.Started += () =>
{
    var agent = app.AddJaliumDevFlowAgent();
};
app.Run();
```

## 4. Build and run

```powershell
dotnet build
dotnet run
```

## 5. Verify the agent

```powershell
Invoke-WebRequest http://localhost:9223/api/v1/agent/status | Select-Object -ExpandProperty Content
```

## 6. What to expect after adoption

When your Jalium app is running, DevFlow hosts a local HTTP API that can:

- expose agent status
- expose live UI tree data
- fetch a specific element by id
- capture screenshots
- perform tap, scroll, fill, clear, focus, key, and back actions
- execute batch action sequences

Routes:

- `GET /api/v1/agent/status`
- `GET /api/v1/ui/tree`
- `GET /api/v1/ui/elements/<id>`
- `GET /api/v1/ui/elements`
- `GET /api/v1/ui/screenshot`
- `POST /api/v1/ui/actions/tap`
- `POST /api/v1/ui/actions/scroll`
- `POST /api/v1/ui/actions/fill`
- `POST /api/v1/ui/actions/clear`
- `POST /api/v1/ui/actions/focus`
- `POST /api/v1/ui/actions/key`
- `POST /api/v1/ui/actions/back`
- `POST /api/v1/ui/actions/batch`

## 7. Optional port configuration

- `MauiDevFlowPort`: override the agent port at build time with `dotnet build -p:MauiDevFlowPort=9500`.
- `.jaliumdevflow`: create a file in the project directory with:

```json
{
  "port": 9500
}
```

The agent defaults to port `9223` when no custom port is configured.

## 8. Recommended practice

Keep DevFlow registration in debug-only code unless you explicitly need it in non-debug runs.
