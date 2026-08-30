# LeXtudio.DevFlow.Driver

DevFlow client driver for communicating with a running DevFlow agent.

This package contains the HTTP client and helper APIs for querying the agent HTTP API from test harnesses, automation tooling, or host applications.

## Install

```powershell
dotnet add package LeXtudio.DevFlow.Driver
```

## What is included

- HTTP client wrappers for DevFlow agent API
- request models for status, UI tree, element lookup, screenshots, and actions
- helper utilities for driver workflows

## Usage

Use this package to connect to a running DevFlow agent and execute automation workflows.

```csharp
var driver = new DevFlowDriver("http://localhost:9223");
await driver.GetStatusAsync();
```

## Related Packages

- [LeXtudio.DevFlow.Agent.Core](https://www.nuget.org/packages/LeXtudio.DevFlow.Agent.Core)
- [LeXtudio.DevFlow.Agent.WPF](https://www.nuget.org/packages/LeXtudio.DevFlow.Agent.WPF)
- [LeXtudio.DevFlow.Agent.Uno](https://www.nuget.org/packages/LeXtudio.DevFlow.Agent.Uno)
- [LeXtudio.DevFlow.Agent.MewUI](https://www.nuget.org/packages/LeXtudio.DevFlow.Agent.MewUI)

## Compatibility

- .NET 10.0+
- Works with any DevFlow agent runtime package
