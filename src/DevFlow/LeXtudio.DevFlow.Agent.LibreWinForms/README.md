# LeXtudio.DevFlow.Agent.LibreWinForms

LibreWinForms DevFlow runtime package for instrumenting LibreWinForms applications — WinForms-shaped apps
rendered on the ProGPU/Silk.NET backend, so they run on macOS and Linux as well as Windows.

This package builds on `LeXtudio.DevFlow.Agent.Core` and compiles the same source files as
`LeXtudio.DevFlow.Agent.WinForms`, so the HTTP surface, element model, and `[DevFlowAction]` support match
the desktop WinForms agent.

## Install

```bash
dotnet add package LeXtudio.DevFlow.Agent.LibreWinForms
```

## Usage

```csharp
using System.Windows.Forms;
using LeXtudio.DevFlow.Agent.WinForms;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        var form = new MainForm();
        var context = new ApplicationContext(form);
        context.AddWinFormsDevFlowAgent(new AgentOptions { Port = 9223 });

        Application.Run(context);
    }
}
```

The agent serves the same endpoints as the other DevFlow agents — `GET /api/v1/agent/status`,
`GET /api/v1/ui/tree`, `GET /api/v1/ui/elements/{id}`, `POST /api/v1/ui/actions/tap`, and so on.

## Relationship to the WinForms agent

The two agents share source rather than a package, so behavior can diverge. Where LibreWinForms cannot
match desktop WinForms today, the shared code uses a `LIBREWINFORMS` conditional compilation symbol, the
same mechanism the LibreWPF agent uses with `LIBREWPF`.

Desktop WinForms behavior that depends on Win32 does not carry over. The agent's tree walking, element
queries, `PerformClick`-based tapping, scrolling, and text input are managed WinForms APIs and work as-is;
injection that P/Invokes Win32 (for example `WindowsNativeActions`) is guarded by
`OperatingSystem.IsWindows()` and degrades to a no-op on other platforms.

## Build notes

This package multi-targets `net10.0` and `net11.0` and references the LibreWinForms runtime packages
(`LibreWinForms.System.Windows.Forms`) directly rather than going through `LibreWinForms.Sdk`.

The reason is the SDK's target framework gate: `LibreWinForms.Sdk` 0.1.0-preview.57 and later fail the
build with an unconditional `TargetFramework != net11.0` error, which would force this agent onto a .NET 11
preview SDK. The SDK's own default runtime, preview.45, is not an alternative — it only implements 456
types and lacks `Application.OpenForms`, `Control.DrawToBitmap`, and `Application.ProductName`, all of which
this agent needs. preview.65 implements 1737 types and still ships `net10.0` assemblies.

Referencing the runtime packages directly also matches how `LeXtudio.DevFlow.Agent.ProGPU` consumes ProGPU
in this repository.

`LibreWinForms.Sdk` remains the right choice for LibreWinForms *applications*, because it supplies the
ProGPU platform bootstrap that a host app needs at startup, which is why the sample app in this repository
targets `net11.0`.

See [the main DevFlow README](../README.md) for the endpoint table and the other agent packages.