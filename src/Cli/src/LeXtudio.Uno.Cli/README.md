# LeXtudio Uno CLI

A command-line tool for Uno development environment workflows, project scaffolding, packaging, and DevFlow integration.

## Package

[![LeXtudio.Uno.Cli](https://img.shields.io/nuget/v/LeXtudio.Uno.Cli.svg?label=Uno%20CLI)](https://www.nuget.org/packages/LeXtudio.Uno.Cli)
[![LeXtudio.Uno.Cli Downloads](https://img.shields.io/nuget/dt/LeXtudio.Uno.Cli.svg?label=Downloads)](https://www.nuget.org/packages/LeXtudio.Uno.Cli)

## Install

```powershell
dotnet tool install -g LeXtudio.Uno.Cli
```

## Documentation

- [Uno CLI Guide](https://github.com/lextudio/wpf-labs/blob/master/docs/cli/uno-cli.md)

## Quick Start

```powershell
dotnet unolex doctor
```

Create a new Uno app using the official Uno Skia desktop template:

```powershell
dotnet unolex new MyUnoApp
```

Build and run:

```powershell
dotnet unolex build
dotnet unolex run
```

## Core Commands

| Command | Description |
|---------|-------------|
| `dotnet unolex doctor` | Validate the Uno development environment. |
| `dotnet unolex version` | Display CLI and environment version information. |
| `dotnet unolex new` | Scaffold a new Uno application. |
| `dotnet unolex build` | Build an Uno project. |
| `dotnet unolex run` | Run an Uno application. |
| `dotnet unolex publish` | Publish an Uno application. |
| `dotnet unolex package` | Package Uno output artifacts. |
| `dotnet unolex diagnostics` | Run Uno diagnostics and validation. |
| `dotnet unolex env` | Inspect installed SDKs and tooling. |
| `dotnet unolex commands` | List available commands in machine-readable JSON form. |
| `dotnet unolex batch` | Execute newline-delimited JSON command batches from stdin. |
| `dotnet unolex devflow` | Query a running Uno DevFlow agent. |

## DevFlow Commands

```powershell
dotnet unolex devflow status --host localhost --port 9223
```

Capture a screenshot:

```powershell
dotnet unolex devflow screenshot --host localhost --port 9223 --output screenshot.png
```

Beyond `status`/`screenshot`/`tap`/`webview`, the CLI also exposes:

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

This CLI is the Uno toolchain companion for DevFlow-enabled apps.
