# WPF CLI Design

## Vision

`LeXtudio.Wpf.Cli` should be the command-line companion for WPF developers on Windows. It should make tooling setup, project workflows, diagnostics, and packaging easier by providing a single, consistent CLI surface modeled after the MAUI CLI experience.

## Design Goals

- Deliver a focused WPF-first CLI for Windows desktop development
- Mirror MAUI CLI UX patterns: nested commands, `--help`, `--json`, `--ci`, and a structured error envelope
- Provide project workflow commands plus environment validation and diagnostics
- Support scripting and CI through JSON output and stable exit behavior
- Keep the initial scope small and expandable

## Recommended Command Surface

### Primary commands

- `wpf doctor`
  - Validate the installed Windows developer toolchain
  - Detect `.NET SDK`, `Windows SDK`, `MSBuild`, Visual Studio / Build Tools, and WPF targeting support
  - Report missing dependencies and provide autofix guidance where available

- `wpf version`
  - Show CLI version and relevant runtime/tool versions
  - Print installed SDKs and supported target frameworks

- `wpf new`
  - Scaffold new WPF apps and libraries
  - Example templates:
    - `app`
    - `library`
    - `mvvm`
  - Example usage:
    - `wpf new app --name MyApp --framework net8.0-windows`

- `wpf build`
  - Build the current WPF project or specified `.csproj`
  - Provide WPF-aware defaults and diagnostics

- `wpf run`
  - Run a WPF app from the current project
  - Support `--no-build`, `--configuration`, and `--framework`

- `wpf publish`
  - Publish a WPF app for deployment
  - Support self-contained publish, single-file, and trimmed binaries
  - Optionally support MSIX packaging helper flows

- `wpf package`
  - Package output artifacts into installer or MSIX bundles
  - Validate required metadata and certificate information

- `wpf diagnostics`
  - Run WPF-specific sanity checks
  - Example subcommands:
    - `wpf diagnostics xaml`
    - `wpf diagnostics project`
    - `wpf diagnostics manifest`

- `wpf env`
  - Inspect installed SDKs, tooling versions, and Windows environment details
  - Example: `wpf env list`, `wpf env check`

## Proposed Future Commands

- `wpf trace`
  - Collect runtime trace data for WPF applications using ETW or `dotnet trace`

- `wpf inspect`
  - Inspect runtime UI state or application manifest information

- `wpf doctor fix`
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
  - Supports nested groups like `wpf diagnostics xaml`

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

- `wpf doctor`
- `wpf version`
- `wpf new`
- `wpf build`
- `wpf run`
- `wpf publish`
- `wpf env check`
- JSON output support
- Basic Windows toolchain detection

### Phase 2

- `wpf diagnostics`
- `wpf package msix`
- improved `wpf new` templates
- better project resolution and multi-target support

### Phase 3

- `wpf trace`
- runtime inspection helpers
- diagnostics rule library
- more packaging scenarios

## Alignment with MAUI CLI

This design borrows the same high-level patterns as MAUI CLI while staying WPF-specific:

- no mobile/device groups
- no Apple/Linux runtime toolchains
- Windows SDK and MSBuild as primary platform concerns
- a strong CLI-first developer experience for WPF apps and libraries

## Notes

The first WPF CLI iteration should be conservative: provide useful, repeatable tooling without trying to replace all of Visual Studio or the existing `dotnet` tooling. The CLI should complement existing workflows and simplify the most common WPF developer tasks.
