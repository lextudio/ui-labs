# Adopt DevFlow in a MewUI Project

This guide shows how to add DevFlow to an existing `Aprillz.MewUI` app.

## 1. Prerequisites

- .NET 10.0 or later (matching current package compatibility)
- A MewUI app that already runs on your target OS

## 2. Add NuGet packages

From your app project folder:

```powershell
dotnet add package LeXtudio.DevFlow.Agent.MewUI
dotnet add package LeXtudio.DevFlow.Driver
```

## 3. Ensure MewUI platform registration still happens first

Keep your existing MewUI platform host registration (`Register()` calls for Win32/X11/macOS platform + backend). DevFlow depends on a running MewUI application context; it does not replace platform setup.

## 4. Register DevFlow after app starts

In your app startup flow (for example `OnLoaded` of the main window):

```csharp
using Aprillz.MewUI;
using Microsoft.Maui.DevFlow.Agent.Core;
using LeXtudio.DevFlow.Agent.MewUI;

var agent = Application.Current.AddMewUIDevFlowAgent();
```

## 5. Build and run

```powershell
dotnet build
dotnet run
```

## 6. Verify the agent

```powershell
Invoke-WebRequest http://localhost:9223/api/v1/agent/status | Select-Object -ExpandProperty Content
```

## 7. What to expect after adoption

When your MewUI app is running, DevFlow hosts a local HTTP API that can:

- expose agent status
- expose live UI tree data
- fetch a specific element by id
- capture screenshots
- perform tap and scroll actions

Routes:

- `GET /api/v1/agent/status`
- `GET /api/v1/ui/tree`
- `GET /api/v1/ui/elements/<id>`
- `GET /api/v1/ui/screenshot`
- `POST /api/v1/ui/actions/tap`
- `POST /api/v1/ui/actions/scroll`

## 8. Optional port configuration

- `MauiDevFlowPort`: override the agent port at build time with `dotnet build -p:MauiDevFlowPort=9500`.
- `.mauidevflow`: create a file in the project directory with:

```json
{
  "port": 9500
}
```

The agent defaults to port `9223` when no custom port is configured.

## 9. Recommended practice

Like MAUI onboarding guidance, keep DevFlow registration in debug-only code unless you explicitly need it in non-debug runs.
