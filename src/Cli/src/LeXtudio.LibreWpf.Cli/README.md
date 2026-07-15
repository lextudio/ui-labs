# LeXtudio.LibreWpf.Cli

LibreWPF command-line tool for DevFlow workflows.

## Documentation

- [LibreWPF CLI Guide](https://github.com/lextudio/wpf-labs/blob/master/docs/cli/librewpf-cli.md)

## Install

```powershell
dotnet tool install --global LeXtudio.LibreWpf.Cli
```

## Usage

```bash
dotnet librewpf version
dotnet librewpf doctor
dotnet librewpf new MyApp
dotnet librewpf build
dotnet librewpf commands
dotnet librewpf devflow status
```

## DevFlow Commands

Beyond `status`/`screenshot`/`tap`/`webview`, the CLI also exposes:

| Command | Description |
|---------|-------------|
| `devflow extensions list\|describe\|call` | Discover and invoke `[DevFlowAction]`-annotated methods in the running app. |
| `devflow inspector` | Start a browser-based live UI inspector proxying the running agent. |
| `devflow broker start\|stop\|status\|list` | Manage the multi-agent broker daemon. |
| `devflow network list\|detail\|clear` | Inspect HTTP traffic captured from apps using `DevFlowHttp.CreateClient()`. |
| `devflow ui query\|hit-test\|assert` | Query the live tree with CSS selectors, hit-test a point, or assert on selector matches. |
| `devflow alert detect\|dismiss` | Detect and dismiss native dialog boxes. |

Plus top-level `commands` (machine-readable command schema) and `batch` (newline-delimited JSON command execution over stdin).
