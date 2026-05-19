# WPF Labs

This workspace contains the WPF DevFlow proof-of-concept and supporting tooling for the WPF tooling research project.

## Workspace structure

- `src/DevFlow/`
  - `Microsoft.Wpf.DevFlow.Agent.Core/` — shared DevFlow core service layer.
  - `Microsoft.Wpf.DevFlow.Agent.WPF/` — plain WPF runtime implementation for DevFlow.
  - `Microsoft.Wpf.DevFlow.Agent.WPF.Tests/` — integration tests covering DevFlow status, tree, screenshot, and tap behavior.
  - `WpfDevFlowTestApp/` — a small WPF sample app instrumented with DevFlow for runtime validation.
- `src/Cli/` — WPF CLI prototype for scaffolding and project workflows.
- `docs/devflow/` — plan and session documentation for the DevFlow work.

## Key goals

- Build a WPF-native DevFlow agent that exposes runtime UI state via HTTP.
- Reuse shared DevFlow infrastructure where it makes sense, while keeping the runtime WPF-only.
- Validate the approach with an end-to-end integration test and a live sample app.

## How to run

### Build all relevant projects

```powershell
cd src\DevFlow
dotnet build WpfDevFlow.sln
```

### Run the sample app

```powershell
cd src\DevFlow\WpfDevFlowTestApp
dotnet run
```

The sample app starts DevFlow on port `5500` and exposes:

- `GET http://localhost:5500/api/v1/agent/status`
- `GET http://localhost:5500/api/v1/ui/tree`
- `GET http://localhost:5500/api/v1/ui/element?id=<id>`
- `GET http://localhost:5500/api/v1/ui/screenshot`
- `POST http://localhost:5500/api/v1/ui/tap`

### Run integration tests

```powershell
cd src\DevFlow\Microsoft.Wpf.DevFlow.Agent.WPF.Tests
dotnet test
```

## Notes

- The WPF DevFlow agent is intentionally lightweight and focused on WPF runtime automation.
- The host app and test app demonstrate live UI tree inspection, screenshot capture, and tap interaction.
- Documentation for the DevFlow plan is available under `docs/devflow/`.

## License

MIT

## Copyright

(c) 2026 LeXtudio Inc.
