# LeXtudio.Jalium.Cli

CLI for Jalium app workflows and DevFlow agent operations.

Install:

```powershell
dotnet tool install -g LeXtudio.Jalium.Cli
```

Usage:

```powershell
dotnet jalex --help
```

## DevFlow Commands

Beyond `status`/`screenshot`/`tap`, the CLI also exposes:

| Command | Description |
|---------|-------------|
| `devflow extensions list\|describe\|call` | Discover and invoke `[DevFlowAction]`-annotated methods in the running app. |
| `devflow inspector` | Start a browser-based live UI inspector proxying the running agent. |
| `devflow broker start\|stop\|status\|list` | Manage the multi-agent broker daemon. |
| `devflow network list\|detail\|clear` | Inspect HTTP traffic captured from apps using `DevFlowHttp.CreateClient()`. |
| `devflow ui query\|hit-test\|assert` | Query the live tree with CSS selectors, hit-test a point, or assert on selector matches. |
| `devflow alert detect\|dismiss` | Detect and dismiss native dialog boxes. |

Plus top-level `commands` (machine-readable command schema) and `batch` (newline-delimited JSON command execution over stdin).

See `docs/cli/jalium-cli.md` for full documentation.
