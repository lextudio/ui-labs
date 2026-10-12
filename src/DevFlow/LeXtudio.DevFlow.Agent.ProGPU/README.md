# LeXtudio.DevFlow.Agent.ProGPU

DevFlow agent for ProGPU.WinUI apps. Exposes a local HTTP API that AI tools and DevFlow clients can use to inspect and drive a running ProGPU application.

## Install

```powershell
dotnet add package LeXtudio.DevFlow.Agent.ProGPU
dotnet add package LeXtudio.DevFlow.Driver
```

## Register

After the ProGPU application is created:

```csharp
using LeXtudio.DevFlow.Agent.ProGPU;

var agent = app.AddProGpuDevFlowAgent();
```
