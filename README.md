# UI Labs

Initially this repo contains the CLI/DevFlow proof-of-concept and supporting tooling for the WPF tooling research project. Now experiments on other UI frameworks like WinForms, WinUI 3, Uno Platform, MewUI, LibreWPF, and Jalium are also included for comparison and validation purposes.

This project is not affiliated with, endorsed by, or sponsored by Microsoft, or other vendors of the frameworks.

## NuGet packages

- [![LeXtudio.DevFlow.Agent.Core](https://img.shields.io/nuget/v/LeXtudio.DevFlow.Agent.Core.svg?label=LeXtudio.DevFlow.Agent.Core)](https://www.nuget.org/packages/LeXtudio.DevFlow.Agent.Core)
- [![LeXtudio.DevFlow.Agent.WPF](https://img.shields.io/nuget/v/LeXtudio.DevFlow.Agent.WPF.svg?label=LeXtudio.DevFlow.Agent.WPF)](https://www.nuget.org/packages/LeXtudio.DevFlow.Agent.WPF)
- [![LeXtudio.DevFlow.Agent.WinForms](https://img.shields.io/nuget/v/LeXtudio.DevFlow.Agent.WinForms.svg?label=LeXtudio.DevFlow.Agent.WinForms)](https://www.nuget.org/packages/LeXtudio.DevFlow.Agent.WinForms)
- [![LeXtudio.DevFlow.Agent.Uno](https://img.shields.io/nuget/v/LeXtudio.DevFlow.Agent.Uno.svg?label=LeXtudio.DevFlow.Agent.Uno)](https://www.nuget.org/packages/LeXtudio.DevFlow.Agent.Uno)
- [![LeXtudio.DevFlow.Agent.MewUI](https://img.shields.io/nuget/v/LeXtudio.DevFlow.Agent.MewUI.svg?label=LeXtudio.DevFlow.Agent.MewUI)](https://www.nuget.org/packages/LeXtudio.DevFlow.Agent.MewUI)
- [![LeXtudio.DevFlow.Agent.LibreWpf](https://img.shields.io/nuget/v/LeXtudio.DevFlow.Agent.LibreWpf.svg?label=LeXtudio.DevFlow.Agent.LibreWpf)](https://www.nuget.org/packages/LeXtudio.DevFlow.Agent.LibreWpf)
- [![LeXtudio.DevFlow.Agent.Jalium](https://img.shields.io/nuget/v/LeXtudio.DevFlow.Agent.Jalium.svg?label=LeXtudio.DevFlow.Agent.Jalium)](https://www.nuget.org/packages/LeXtudio.DevFlow.Agent.Jalium)
- [![LeXtudio.DevFlow.Driver](https://img.shields.io/nuget/v/LeXtudio.DevFlow.Driver.svg?label=LeXtudio.DevFlow.Driver)](https://www.nuget.org/packages/LeXtudio.DevFlow.Driver)
- [![LeXtudio.DevFlow.Inspector](https://img.shields.io/nuget/v/LeXtudio.DevFlow.Inspector.svg?label=LeXtudio.DevFlow.Inspector)](https://www.nuget.org/packages/LeXtudio.DevFlow.Inspector)
- [![LeXtudio.DevFlow.Broker](https://img.shields.io/nuget/v/LeXtudio.DevFlow.Broker.svg?label=LeXtudio.DevFlow.Broker)](https://www.nuget.org/packages/LeXtudio.DevFlow.Broker)
- [![LeXtudio.Wpf.Cli](https://img.shields.io/nuget/v/LeXtudio.Wpf.Cli.svg?label=LeXtudio.Wpf.Cli)](https://www.nuget.org/packages/LeXtudio.Wpf.Cli)
- [![LeXtudio.WinForms.Cli](https://img.shields.io/nuget/v/LeXtudio.WinForms.Cli.svg?label=LeXtudio.WinForms.Cli)](https://www.nuget.org/packages/LeXtudio.WinForms.Cli)
- [![LeXtudio.Uno.Cli](https://img.shields.io/nuget/v/LeXtudio.Uno.Cli.svg?label=LeXtudio.Uno.Cli)](https://www.nuget.org/packages/LeXtudio.Uno.Cli)
- [![LeXtudio.MewUI.Cli](https://img.shields.io/nuget/v/LeXtudio.MewUI.Cli.svg?label=LeXtudio.MewUI.Cli)](https://www.nuget.org/packages/LeXtudio.MewUI.Cli)
- [![LeXtudio.LibreWpf.Cli](https://img.shields.io/nuget/v/LeXtudio.LibreWpf.Cli.svg?label=LeXtudio.LibreWpf.Cli)](https://www.nuget.org/packages/LeXtudio.LibreWpf.Cli)
- [![LeXtudio.Jalium.Cli](https://img.shields.io/nuget/v/LeXtudio.Jalium.Cli.svg?label=LeXtudio.Jalium.Cli)](https://www.nuget.org/packages/LeXtudio.Jalium.Cli)

## Workspace structure

- `src/DevFlow/`
  - `LeXtudio.DevFlow.Agent.Core/` — shared DevFlow core service layer (HTTP server, CSS selector/query engine, network capture, Win32 alert detection).
  - `LeXtudio.DevFlow.Agent.XXX/` — plain runtime implementation for DevFlow for XXX UI framework (WPF, WinForms, Uno, MewUI, LibreWPF, Jalium).
  - `LeXtudio.DevFlow.Agent.XXX.Tests/` — integration tests covering DevFlow status, tree, screenshot, tap, and scroll behavior.
  - `XXXDevFlowTestApp/` — a small sample app instrumented with DevFlow for runtime validation for XXX UI framework.
  - `LeXtudio.DevFlow.Driver/` — HTTP client for querying a running DevFlow agent.
  - `LeXtudio.DevFlow.Inspector/` — browser-based live UI inspector server (`devflow inspector`).
  - `LeXtudio.DevFlow.Broker/` — multi-agent broker daemon for discovery/registration (`devflow broker`).
  - `LeXtudio.DevFlow.Analyzers/` — Roslyn analyzer validating `[DevFlowAction]` usage at compile time.
- `src/Cli/` — global tool prototypes for scaffolding and project workflows, one CLI per UI framework.
- `docs/` — plan and session documentation for the CLI/DevFlow work.

## Key goals

- Build DevFlow agents that expose runtime UI state for WinForms, WPF, WinUI 3, Uno Platform, MewUI, LibreWPF, and Jalium apps via HTTP.
- Reuse shared DevFlow infrastructure where it makes sense, while keeping platform code in focused runtime packages.
- Validate the approach with an end-to-end integration test and a live sample app.

## DevFlow feature set

Every agent (regardless of UI framework) exposes the same HTTP surface, reachable through each CLI's `devflow` subcommand:

| Area | Highlights |
|------|------------|
| UI tree & interaction | Live tree/element inspection, screenshots, tap/fill/clear/focus/key/scroll/back |
| `extensions` | Discover and invoke `[DevFlowAction]`-annotated methods in the running app |
| `inspector` | Browser-based live UI inspector — proxies screenshots + tree + interaction over HTTP |
| `broker` | Multi-agent discovery daemon (`start`/`stop`/`status`/`list`) for targeting a specific running app |
| `network` | Captured HTTP traffic (`list`/`detail`/`clear`) for apps using `DevFlowHttp.CreateClient()` |
| `ui` | CSS-selector queries (`query`), point hit-testing (`hit-test`), and structural assertions (`assert`) |
| `alert` | Detect and dismiss native dialog boxes (`detect`/`dismiss`) |
| `webview` | WebView context listing, screenshot, and CDP command execution (WPF/WinForms/LibreWPF) |
| `batch` / `commands` | Scripted multi-command execution over stdin and machine-readable command schema discovery |

## How to use DevFlow with WPF/WinForms/WinUI 3/Uno Platform/MewUI/LibreWPF/Jalium

### Build all relevant projects

```powershell
cd src
dotnet build labs.slnx
```

### Run the WPF sample app

```powershell
cd src\DevFlow\WpfDevFlowTestApp
dotnet run --no-build
```

The sample app starts DevFlow on port `9223` and exposes:

- `GET http://localhost:9223/api/v1/agent/status`
- `GET http://localhost:9223/api/v1/ui/tree`
- `GET http://localhost:9223/api/v1/ui/element?id=<id>`
- `GET http://localhost:9223/api/v1/ui/screenshot`
- `POST http://localhost:9223/api/v1/ui/tap`
- `POST http://localhost:9223/api/v1/ui/actions/scroll`

### Run the WinUI 3/Uno Platform sample app

```powershell
cd src\DevFlow\UnoDevFlowTestApp
dotnet run -f net10.0-desktop --no-build
```

This launches the sample app on Uno Platform and it starts DevFlow on port `9223` and exposes:

- `GET http://localhost:9223/api/v1/agent/status`
- `GET http://localhost:9223/api/v1/ui/tree`
- `GET http://localhost:9223/api/v1/ui/element?id=<id>`
- `GET http://localhost:9223/api/v1/ui/screenshot`
- `POST http://localhost:9223/api/v1/ui/tap`
- `POST http://localhost:9223/api/v1/ui/actions/scroll`

> Note: WinUI apps can also use the WinApp CLI UI commands to achieve a similar runtime UI inspection and interaction workflow. DevFlow is therefore optional for WinUI scenarios when comparable WinApp CLI support already exists.

To launch the sample app on WinUI 3,

```powershell
cd src\DevFlow\UnoDevFlowTestApp
dotnet run -f net10.0-windows10.0.19041.0 --no-build
```

### Run the WinForms sample app

```powershell
cd src\DevFlow\WinFormsDevFlowTestApp
dotnet run --no-build
```

The sample app starts DevFlow on port `9223` by default and exposes the same shared DevFlow HTTP API used by the other runtime agents.

### Run the MewUI sample app

```powershell
cd src\DevFlow\MewUIDevFlowTestApp
dotnet run --no-build
```

The sample app starts DevFlow on port `9223` by default and exposes the same shared DevFlow HTTP API used by the other runtime agents.

### Run the LibreWPF sample app

```powershell
cd src\DevFlow\LibreWpfDevFlowTestApp
dotnet run --no-build
```

The sample app starts DevFlow on port `9223` by default and exposes the same shared DevFlow HTTP API used by the other runtime agents.

### Run the Jalium sample app

```powershell
cd src\DevFlow\JaliumDevFlowTestApp
dotnet run --no-build
```

The sample app starts DevFlow on port `9223` by default and exposes the same shared DevFlow HTTP API used by the other runtime agents.

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

### Run WinForms integration tests

```powershell
cd src\DevFlow\LeXtudio.DevFlow.Agent.WinForms.Tests
dotnet test --project LeXtudio.DevFlow.Agent.WinForms.Tests.csproj
```

### Run MewUI integration tests

```powershell
cd src\DevFlow\LeXtudio.DevFlow.Agent.MewUI.Tests
dotnet test --project LeXtudio.DevFlow.Agent.MewUI.Tests.csproj
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

### Install packages to a WinForms project

```powershell
dotnet add package LeXtudio.DevFlow.Agent.WinForms
dotnet add package LeXtudio.DevFlow.Driver
```

### Install packages to a MewUI project

```powershell
dotnet add package LeXtudio.DevFlow.Agent.MewUI
dotnet add package LeXtudio.DevFlow.Driver
```

### Install packages to a LibreWPF project

```powershell
dotnet add package LeXtudio.DevFlow.Agent.LibreWpf
dotnet add package LeXtudio.DevFlow.Driver
```

### Install packages to a Jalium project

```powershell
dotnet add package LeXtudio.DevFlow.Agent.Jalium
dotnet add package LeXtudio.DevFlow.Driver
```

## Install WPF CLI tool

```powershell
dotnet tool install -g LeXtudio.Wpf.Cli
dotnet wpflex --help
```

## Install WinUI 3/Uno Platform CLI tool

```powershell
dotnet tool install -g LeXtudio.Uno.Cli
dotnet unolex --help
```

## Install WinForms CLI tool

```powershell
dotnet tool install -g LeXtudio.WinForms.Cli
dotnet winflex --help
```

## Install MewUI CLI tool

```powershell
dotnet tool install -g LeXtudio.MewUI.Cli
dotnet mewlex --help
```

## Install LibreWPF CLI tool

```powershell
dotnet tool install -g LeXtudio.LibreWpf.Cli
dotnet librewpf --help
```

## Install Jalium CLI tool

```powershell
dotnet tool install -g LeXtudio.Jalium.Cli
dotnet jalex --help
```

## Notes

- The DevFlow agent is intentionally lightweight and focused on WPF/WinForms/WinUI 3/Uno Platform/MewUI/LibreWPF/Jalium runtime automation.
- The host app and test app demonstrate live UI tree inspection, screenshot capture, tap, and scroll interaction, plus the extensions/inspector/broker/network/ui/alert feature set described above.
- Documentation for the DevFlow plan is available under `docs/devflow/`.

## License

MIT

## Copyright

(c) 2026 LeXtudio Inc. All rights reserved.
