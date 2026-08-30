# WPF CLI Design

## Vision

`LeXtudio.Wpf.Cli` should be the command-line companion for WPF developers on Windows. It should make tooling setup, project workflows, diagnostics, packaging, and DevFlow agent checks easier by providing a single, consistent CLI surface modeled after the MAUI CLI experience.

The same CLI shape now also applies to `LeXtudio.WinForms.Cli`, exposed as `dotnet winflex`, for classic WinForms applications.

## Design Goals

- Deliver a focused WPF-first CLI for Windows desktop development
- Provide a parallel WinForms-first CLI with the same command structure where the underlying workflow maps cleanly to WinForms
- Mirror MAUI CLI UX patterns: nested commands, `--help`, `--json`, `--ci`, and a structured error envelope
- Provide project workflow commands plus environment validation and diagnostics
- Support scripting and CI through JSON output and stable exit behavior
- Keep the initial scope small and expandable

## Recommended Command Surface

### Primary commands

- `dotnet wpflex doctor`
  - Validate the installed Windows developer toolchain
  - Detect `.NET SDK`, `Windows SDK`, `MSBuild`, Visual Studio / Build Tools, and WPF targeting support
  - Report missing dependencies and provide autofix guidance where available

- `dotnet wpflex version`
  - Show CLI version and relevant runtime/tool versions
  - Print installed SDKs and supported target frameworks

- `dotnet wpflex new`
  - Scaffold new WPF apps and libraries
  - Example templates:
    - `app`
    - `library`
    - `mvvm`
  - Example usage:
    - `dotnet wpflex new app --name MyApp --framework net10.0-windows`

- `dotnet wpflex build`
  - Build the current WPF project or specified `.csproj`
  - Provide WPF-aware defaults and diagnostics

- `dotnet wpflex run`
  - Run a WPF app from the current project
  - Support `--no-build`, `--configuration`, and `--framework`

- `dotnet wpflex publish`
  - Publish a WPF app for deployment
  - Support self-contained publish, single-file, and trimmed binaries
  - Optionally support MSIX packaging helper flows

- `dotnet wpflex package`
  - Package output artifacts into installer or MSIX bundles
  - Validate required metadata and certificate information

- `dotnet wpflex diagnostics`
  - Run WPF-specific sanity checks
  - Example subcommands:
    - `dotnet wpflex diagnostics xaml`
    - `dotnet wpflex diagnostics project`
    - `dotnet wpflex diagnostics manifest`

- `dotnet wpflex env`
  - Inspect installed SDKs, tooling versions, and Windows environment details
  - Example: `dotnet wpflex env list`, `dotnet wpflex env check`

- `dotnet wpflex devflow`
  - Query a running WPF DevFlow agent
  - Current implementation: `dotnet wpflex devflow status [--host <host>] [--port <port>]`

- `dotnet winflex devflow`
  - Query a running WinForms DevFlow agent
  - Current implementation: `dotnet winflex devflow status [--host <host>] [--port <port>]`
  - Additional DevFlow operations include screenshot capture and tap/action requests through the shared DevFlow HTTP API

## Proposed Future Commands

- `dotnet wpflex trace`
  - Collect runtime trace data for WPF applications using ETW or `dotnet trace`

- `dotnet wpflex inspect`
  - Inspect runtime UI state or application manifest information

- `dotnet wpflex doctor fix`
  - Attempt autofixes for common environment issues

## Output Model

The CLI should support:

- interactive output with tables, colors, and progress indicators
- structured JSON output for automation and CI via `--json`
- a `--ci` mode that disables prompts and treats failures as hard errors

### Error envelope

For machine-consumable failure scenarios, the CLI should emit a structured error object with fields like `code`, `category`, `severity`, `message`, and optional remediation guidance.

## Architecture

### Layers

- `Host / Program`
  - Entry point for parsing global options and invoking commands

- `Commands`
  - Each command maps to a class and/or handler
  - Supports nested groups like `dotnet wpflex diagnostics xaml`

- `Providers`
  - Abstract Windows-specific services such as SDK discovery, VS/MSBuild discovery, and package validation

- `Output`
  - Shared output formatting for interactive and JSON modes
  - Common helper for tables, progress, and errors

- `Diagnostics`
  - Implementation of `doctor`, environment checks, and validation rules

- `Templates`
  - Project scaffolding templates for WPF apps and libraries

- `Errors`
  - Stable error codes and categories for CLI failures

## Project scaffolding

A recommended folder layout:

- `wpf-cli/`
  - `README.md`
  - `DESIGN.md`
  - `src/LeXtudio.Wpf.Cli/`
  - `src/LeXtudio.Wpf.Cli.UnitTests/`
  - `templates/`
  - `docs/`
  - `wpf-cli.slnf`

## MVP Implementation Plan

### Phase 1

- `dotnet wpflex doctor`
- `dotnet wpflex version`
- `dotnet wpflex new`
- `dotnet wpflex build`
- `dotnet wpflex run`
- `dotnet wpflex publish`
- `dotnet wpflex env check`
- `dotnet wpflex devflow status`
- JSON output support
- Basic Windows toolchain detection

### Phase 2

- `dotnet wpflex diagnostics`
- `dotnet wpflex package msix`
- improved `dotnet wpflex new` templates
- better project resolution and multi-target support

### Phase 3

- `dotnet wpflex trace`
- runtime inspection helpers
- diagnostics rule library
- more packaging scenarios

## Alignment with MAUI CLI

This design borrows the same high-level patterns as MAUI CLI while staying WPF-specific:

- no mobile/device groups
- no Apple/Linux runtime toolchains
- Windows SDK and MSBuild as primary platform concerns
- a strong CLI-first developer experience for WPF apps and libraries

The WinForms CLI follows the same alignment: it keeps Windows desktop development as the center of gravity while replacing WPF-specific project and runtime assumptions with WinForms equivalents.

## Notes

The first WPF CLI iteration should be conservative: provide useful, repeatable tooling without trying to replace all of Visual Studio or the existing `dotnet` tooling. The CLI should complement existing workflows and simplify the most common WPF developer tasks.
