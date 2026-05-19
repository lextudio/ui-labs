# LeXtudio.Wpf.Cli

A command-line tool for WPF development environment validation, project workflows, packaging, and diagnostics.

> ⚠️ **Experimental** — APIs and command names may evolve. This project is intended as a foundation for a WPF CLI experience modeled after the MAUI CLI design.

## Package

| Package | Description |
|---------|-------------|
| **LeXtudio.Wpf.Cli** | Global CLI tool (`wpf`) for WPF project workflows, diagnostics, and packaging. |

## Quick Start

### 1. Install the CLI tool

```powershell
dotnet tool install -g LeXtudio.Wpf.Cli
```

### 2. Check your environment

```powershell
wpf doctor
```

### 3. Create a new WPF project

```powershell
wpf new app --name MyWpfApp --framework net8.0-windows
```

### 4. Build and run

```powershell
wpf build
wpf run
```

## Core Commands

| Command | Description |
|---------|-------------|
| `wpf doctor` | Validate the WPF development environment and surface missing toolchain components. |
| `wpf version` | Display CLI and environment version information. |
| `wpf new` | Scaffold a new WPF app or library from templates. |
| `wpf build` | Build a WPF project with WPF-aware defaults. |
| `wpf run` | Run a WPF application. |
| `wpf publish` | Publish a WPF application for deployment. |
| `wpf package` | Package WPF outputs, including MSIX or self-contained bundles. |
| `wpf diagnostics` | Run WPF-specific diagnostics and validation checks. |
| `wpf env` | Inspect installed SDKs, tooling, and Windows environment status. |

Run `wpf <command> --help` for detailed options on any command.

## Output and Automation

The WPF CLI should support a human-friendly interactive console experience by default and a machine-readable JSON mode using `--json` for automation.

| Option | Description |
|--------|-------------|
| `--json` | Emit structured JSON output for scripting and CI. |
| `-v`, `--verbose` | Enable verbose diagnostic output. |
| `--dry-run` | Show planned actions without changing the system. |
| `--ci` | Run in CI-friendly non-interactive mode. |

## Foundation

This repository is the foundation for a WPF CLI that mirrors MAUI CLI design principles:

- nested command groups and clear surface area
- shared output layers for interactive and JSON modes
- platform-aware environment checks and diagnostics
- extensible provider architecture for Windows tooling
- stable error categories and remediation guidance

See `DESIGN.md` for the proposed architecture, command hierarchy, and implementation plan.
