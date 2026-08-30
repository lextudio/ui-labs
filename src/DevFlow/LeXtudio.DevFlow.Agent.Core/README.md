# LeXtudio.DevFlow.Agent.Core

Shared DevFlow runtime core for LeXtudio DevFlow agents.

This package contains the HTTP server, transport DTOs, request routing, and shared platform-agnostic plumbing used by all DevFlow agent runtime packages.

## Install

```powershell
dotnet add package LeXtudio.DevFlow.Agent.Core
```

## What is included

- HTTP agent host for DevFlow communication
- shared request and response models
- agent lifecycle support
- common serialization and error handling

## Usage

This package is consumed by platform-specific runtime packages such as `LeXtudio.DevFlow.Agent.WPF`, `LeXtudio.DevFlow.Agent.WinForms`, `LeXtudio.DevFlow.Agent.Uno`, and `LeXtudio.DevFlow.Agent.MewUI`.

Add a platform-specific agent package and register it in your app startup.

## Related Packages

- [LeXtudio.DevFlow.Agent.WPF](https://www.nuget.org/packages/LeXtudio.DevFlow.Agent.WPF)
- [LeXtudio.DevFlow.Agent.WinForms](https://www.nuget.org/packages/LeXtudio.DevFlow.Agent.WinForms)
- [LeXtudio.DevFlow.Agent.Uno](https://www.nuget.org/packages/LeXtudio.DevFlow.Agent.Uno)
- [LeXtudio.DevFlow.Agent.MewUI](https://www.nuget.org/packages/LeXtudio.DevFlow.Agent.MewUI)
- [LeXtudio.DevFlow.Driver](https://www.nuget.org/packages/LeXtudio.DevFlow.Driver)

## Compatibility

- .NET 10.0+
- Used by WPF, WinForms, Uno, and MewUI runtime packages
