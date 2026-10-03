# LibreWinForms DevFlow Test App

A small LibreWinForms application used to validate `LeXtudio.DevFlow.Agent.LibreWinForms` end to end. It
renders through the ProGPU/Silk.NET backend, so it runs on macOS, Linux, and Windows.

LibreWinForms apps normally use the `LibreWinForms.Sdk` MSBuild SDK (this app targets `net11.0`, which that
SDK requires from preview.57 onward). The agent library under test stays on `net10.0`.

## Controls

| Name | Type | Purpose |
|---|---|---|
| `MainForm` | `Form` | Root form. |
| `ActionButton` | `Button` | Sets `ResponseLabel` text to `Button clicked` when clicked. |
| `InputBox` | `TextBox` | Initialized to `initial`; used for fill/clear tests. |
| `ResponseLabel` | `Label` | Starts as `ready`; becomes `Button clicked` after a tap. |
| `MainScrollPanel` | `Panel` | Auto-scrolling panel; used for scroll tests. |
| `ScrollSpacer` | `Label` | Content below the fold, so scrolling is observable. |

The app also exposes a `[DevFlowAction]` named `librewinforms.echo` for invoke API tests.

## Run

```bash
export DEVFLOW_AGENT_PORT=9223
dotnet run --project LibreWinFormsDevFlowTestApp
```

Endpoints:

- `GET http://localhost:9223/api/v1/agent/status`
- `GET http://localhost:9223/api/v1/ui/tree`
- `GET http://localhost:9223/api/v1/ui/elements/ActionButton`
- `POST http://localhost:9223/api/v1/ui/actions/tap` with `{ "id": "ActionButton" }`

## Tests

`LeXtudio.DevFlow.Agent.LibreWinForms.Tests` launches this app and asserts the endpoints above. Because
LibreWinForms is cross-platform, those tests are not Windows-only and run on macOS and Linux too.

Building this app requires a .NET 11 SDK on the machine. The test suite locates one and skips with an
explanatory message when it cannot find it.