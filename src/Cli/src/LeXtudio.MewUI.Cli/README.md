# LeXtudio MewUI CLI

A command-line tool for MewUI application workflows, project scaffolding, and DevFlow integration.

## Package

[![LeXtudio.MewUI.Cli](https://img.shields.io/nuget/v/LeXtudio.MewUI.Cli.svg?label=MewUI%20CLI)](https://www.nuget.org/packages/LeXtudio.MewUI.Cli)
[![LeXtudio.MewUI.Cli Downloads](https://img.shields.io/nuget/dt/LeXtudio.MewUI.Cli.svg?label=Downloads)](https://www.nuget.org/packages/LeXtudio.MewUI.Cli)

## Install

```powershell
dotnet tool install -g LeXtudio.MewUI.Cli
```

## Documentation

- [MewUI CLI Guide](https://github.com/lextudio/wpf-labs/blob/master/docs/cli/mewui-cli.md)

## Quick Start

```powershell
dotnet mewlex doctor
```

Create a new MewUI app:

```powershell
dotnet mewlex new MyMewApp
```

Build and run:

```powershell
dotnet mewlex build
dotnet mewlex run
```

## Core Commands

| Command | Description |
|---------|-------------|
| `dotnet mewlex doctor` | Validate the MewUI development environment. |
| `dotnet mewlex version` | Display CLI version and environment details. |
| `dotnet mewlex new` | Scaffold a new MewUI application. |
| `dotnet mewlex build` | Build a MewUI project. |
| `dotnet mewlex run` | Run a MewUI application. |
| `dotnet mewlex publish` | Publish a MewUI application for deployment. |
| `dotnet mewlex package` | Package MewUI output artifacts. |
| `dotnet mewlex diagnostics` | Run MewUI diagnostics and validation. |
| `dotnet mewlex env` | Inspect installed SDKs and tooling. |
| `dotnet mewlex commands` | List available commands in machine-readable JSON form. |
| `dotnet mewlex batch` | Execute newline-delimited JSON command batches from stdin. |
| `dotnet mewlex devflow` | Query a running MewUI DevFlow agent. |

## DevFlow Commands

```powershell
dotnet mewlex devflow status --host localhost --port 9223
```

Capture a screenshot:

```powershell
dotnet mewlex devflow screenshot --host localhost --port 9223 --output screenshot.png
```

Use `--json` to script responses:

```powershell
dotnet mewlex --json devflow status
```

Beyond `status`/`screenshot`/`tap`, the CLI also exposes:

| Command | Description |
|---------|-------------|
| `devflow extensions list\|describe\|call` | Discover and invoke `[DevFlowAction]`-annotated methods in the running app. |
| `devflow inspector` | Start a browser-based live UI inspector proxying the running agent. |
| `devflow broker start\|stop\|status\|list` | Manage the multi-agent broker daemon. |
| `devflow network list\|detail\|clear` | Inspect HTTP traffic captured from apps using `DevFlowHttp.CreateClient()`. |
| `devflow ui query\|hit-test\|assert` | Query the live tree with CSS selectors, hit-test a point, or assert on selector matches. |
| `devflow alert detect\|dismiss` | Detect and dismiss native dialog boxes. |

## Output and Automation

| Option | Description |
|--------|-------------|
| `--json` | Emit structured JSON output for scripting and CI. |
| `-v`, `--verbose` | Enable verbose diagnostic output. |
| `--dry-run` | Show planned actions without changing the system. |
| `--ci` | Run in CI-friendly non-interactive mode. |

## Notes

This CLI is the MewUI toolchain companion for DevFlow-enabled apps, with a custom `dotnet mewlex` command entrypoint.
