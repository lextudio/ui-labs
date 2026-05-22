# WPF Labs

This workspace contains the WPF DevFlow proof-of-concept and supporting tooling for the WPF tooling research project.

## NuGet packages

- [![LeXtudio.DevFlow.Agent.Core](https://img.shields.io/nuget/v/LeXtudio.DevFlow.Agent.Core.svg?label=LeXtudio.DevFlow.Agent.Core)](https://www.nuget.org/packages/LeXtudio.DevFlow.Agent.Core)
- [![LeXtudio.DevFlow.Agent.WPF](https://img.shields.io/nuget/v/LeXtudio.DevFlow.Agent.WPF.svg?label=LeXtudio.DevFlow.Agent.WPF)](https://www.nuget.org/packages/LeXtudio.DevFlow.Agent.WPF)
- [![LeXtudio.DevFlow.Agent.Uno](https://img.shields.io/nuget/v/LeXtudio.DevFlow.Agent.Uno.svg?label=LeXtudio.DevFlow.Agent.Uno)](https://www.nuget.org/packages/LeXtudio.DevFlow.Agent.Uno)
- [![LeXtudio.DevFlow.Driver](https://img.shields.io/nuget/v/LeXtudio.DevFlow.Driver.svg?label=LeXtudio.DevFlow.Driver)](https://www.nuget.org/packages/LeXtudio.DevFlow.Driver)
- [![LeXtudio.Wpf.Cli](https://img.shields.io/nuget/v/LeXtudio.Wpf.Cli.svg?label=LeXtudio.Wpf.Cli)](https://www.nuget.org/packages/LeXtudio.Wpf.Cli)

## Workspace structure

- `src/DevFlow/`
  - `LeXtudio.DevFlow.Agent.Core/` — shared DevFlow core service layer.
  - `LeXtudio.DevFlow.Agent.WPF/` — plain WPF runtime implementation for DevFlow.
  - `LeXtudio.DevFlow.Agent.WPF.Tests/` — integration tests covering DevFlow status, tree, screenshot, tap, and scroll behavior.
  - `WpfDevFlowTestApp/` — a small WPF sample app instrumented with DevFlow for runtime validation.
- `src/Cli/` — `LeXtudio.Wpf.Cli` global tool prototype for scaffolding and project workflows.
- `docs/devflow/` — plan and session documentation for the DevFlow work.

## Key goals

- Build DevFlow agents that expose runtime UI state for WPF, WinUI 3, and Uno Platform apps via HTTP.
- Reuse shared DevFlow infrastructure where it makes sense, while keeping platform code in focused runtime packages.
- Validate the approach with an end-to-end integration test and a live sample app.

## How to use DevFlow with WPF/WinUI 3/Uno Platform

### Build all relevant projects

```powershell
cd src\DevFlow
dotnet build WpfDevFlow.sln
```

### Run the WPF sample app

```powershell
cd src\DevFlow\WpfDevFlowTestApp
dotnet run --no-build
```

The sample app starts DevFlow on port `5500` and exposes:

- `GET http://localhost:5500/api/v1/agent/status`
- `GET http://localhost:5500/api/v1/ui/tree`
- `GET http://localhost:5500/api/v1/ui/element?id=<id>`
- `GET http://localhost:5500/api/v1/ui/screenshot`
- `POST http://localhost:5500/api/v1/ui/tap`
- `POST http://localhost:5500/api/v1/ui/actions/scroll`

### Run the WinUI 3/Uno Platform sample app

```powershell
cd src\DevFlow\UnoDevFlowTestApp
dotnet run -f net10.0-desktop --no-build
```

This launches the sample app on Uno Platform and it starts DevFlow on port `5500` and exposes:

- `GET http://localhost:5500/api/v1/agent/status`
- `GET http://localhost:5500/api/v1/ui/tree`
- `GET http://localhost:5500/api/v1/ui/element?id=<id>`
- `GET http://localhost:5500/api/v1/ui/screenshot`
- `POST http://localhost:5500/api/v1/ui/tap`
- `POST http://localhost:5500/api/v1/ui/actions/scroll`

> Note: WinUI apps can also use the WinApp CLI UI commands to achieve a similar runtime UI inspection and interaction workflow. DevFlow is therefore optional for WinUI scenarios when comparable WinApp CLI support already exists.

To launch the sample app on WinUI 3,

```powershell
cd src\DevFlow\UnoDevFlowTestApp
dotnet run -f net10.0-windows10.0.19041.0 --no-build
```

### Run WPF integration tests

```powershell
cd src\DevFlow\LeXtudio.DevFlow.Agent.WPF.Tests
dotnet test --project LeXtudio.DevFlow.Agent.WPF.Tests.csproj
```

### Run WinUI 3/Uno Platform integration tests

```powershell
cd src\DevFlow\LeXtudio.DevFlow.Agent.Uno.Tests
dotnet test --project LeXtudio.DevFlow.Agent.Uno.Tests.csproj
```
## Use in your projects

You can use prebuilt NuGet packages

### Install packages to a WPF project

```powershell
dotnet add package LeXtudio.DevFlow.Agent.WPF
dotnet add package LeXtudio.DevFlow.Driver
```

### Install packages to a WinUI 3/Uno Platform project

```powershell
dotnet add package LeXtudio.DevFlow.Agent.Uno
dotnet add package LeXtudio.DevFlow.Driver
```

## Install WPF CLI tool

```powershell
dotnet tool install -g LeXtudio.Wpf.Cli
dotnet wpflex --help
```

## Notes

- The DevFlow agent is intentionally lightweight and focused on WPF/WinUI 3/Uno Platform runtime automation.
- The host app and test app demonstrate live UI tree inspection, screenshot capture, tap, and scroll interaction.
- Documentation for the DevFlow plan is available under `docs/devflow/`.

## License

MIT

## Copyright

(c) 2026 LeXtudio Inc.
