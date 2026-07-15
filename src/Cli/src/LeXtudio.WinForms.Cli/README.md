# LeXtudio WinForms CLI

A command-line tool for WinForms project workflows, diagnostics, packaging, and DevFlow integration.

## Package

[![LeXtudio.WinForms.Cli](https://img.shields.io/nuget/v/LeXtudio.WinForms.Cli.svg?label=WinForms%20CLI)](https://www.nuget.org/packages/LeXtudio.WinForms.Cli)
[![LeXtudio.WinForms.Cli Downloads](https://img.shields.io/nuget/dt/LeXtudio.WinForms.Cli.svg?label=Downloads)](https://www.nuget.org/packages/LeXtudio.WinForms.Cli)

## Install

```powershell
dotnet tool install -g LeXtudio.WinForms.Cli
```

## Documentation

- [CLI project overview](../../README.md)
- [CLI design notes](../../DESIGN.md)

## Quick Start

```powershell
dotnet winflex doctor
```

Create a new WinForms project:

```powershell
dotnet winflex new app --name MyWinFormsApp --framework net8.0-windows
```

Build and run:

```powershell
dotnet winflex build
dotnet winflex run
```

## Core Commands

| Command | Description |
|---------|-------------|
| `dotnet winflex doctor` | Validate the WinForms development environment and surface missing toolchain components. |
| `dotnet winflex version` | Display CLI and environment version information. |
| `dotnet winflex new` | Scaffold a new WinForms app or library. |
| `dotnet winflex build` | Build a WinForms project with WinForms-aware defaults. |
| `dotnet winflex run` | Run a WinForms application. |
| `dotnet winflex publish` | Publish a WinForms application for deployment. |
| `dotnet winflex package` | Package WinForms outputs for distribution. |
| `dotnet winflex diagnostics` | Run WinForms-specific diagnostics and validation checks. |
| `dotnet winflex env` | Inspect installed SDKs, tooling, and environment status. |
| `dotnet winflex commands` | List available commands in machine-readable JSON form. |
| `dotnet winflex batch` | Execute newline-delimited JSON command batches from stdin. |

## DevFlow Commands

Query a running WinForms DevFlow agent:

```powershell
dotnet winflex devflow status --host localhost --port 9223
```

Capture a screenshot from a running WinForms DevFlow agent:

```powershell
dotnet winflex devflow screenshot --host localhost --port 9223 --output screenshot.png
```

Use `--json` for scripting:

```powershell
dotnet winflex --json devflow status
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

This CLI is a WinForms-focused tool that mirrors MAUI CLI design patterns and is intended for environment validation, project workflows, and DevFlow connectivity.
