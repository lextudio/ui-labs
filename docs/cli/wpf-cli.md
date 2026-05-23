# WPF CLI Guide (`dotnet wpflex`)

This guide explains how to use the WPF CLI for project workflows and DevFlow agent operations.

## 1. Install

```powershell
dotnet tool install -g LeXtudio.Wpf.Cli
```

If already installed:

```powershell
dotnet tool update -g LeXtudio.Wpf.Cli
```

## 2. Check available commands

```powershell
dotnet wpflex --help
```

Core commands:

- `doctor`
- `version`
- `new`
- `build`
- `run`
- `publish`
- `package`
- `diagnostics`
- `env`
- `devflow` (`status`, `screenshot`, `tap`)

## 3. Scaffold a new WPF app

```powershell
dotnet wpflex new MyWpfApp
```

What happens:

- Creates folder `MyWpfApp`
- Generates `MyWpfApp.csproj`
- Generates `Program.cs`

## 4. Build and run projects

From a solution/project folder:

```powershell
dotnet wpflex build
dotnet wpflex run
```

Target a specific project:

```powershell
dotnet wpflex build .\src\MyWpfApp\MyWpfApp.csproj
dotnet wpflex run .\src\MyWpfApp\MyWpfApp.csproj
```

Common build options:

- `-c|--configuration Debug|Release`
- `-r|--runtime <RID>`
- `--framework <TFM>`
- `--output <folder>` (for `build`/`publish`)

## 5. Publish and package

Publish:

```powershell
dotnet wpflex publish .\src\MyWpfApp\MyWpfApp.csproj -c Release
```

Package (publishes first, then zips output as `publish.zip` in current directory):

```powershell
dotnet wpflex package .\src\MyWpfApp\MyWpfApp.csproj -c Release
```

## 6. Environment and diagnostics

Validate CLI environment:

```powershell
dotnet wpflex doctor
```

Show runtime + SDK environment:

```powershell
dotnet wpflex env
dotnet wpflex diagnostics
```

## 7. Use WPF CLI with DevFlow

Assumes your app is running with DevFlow agent enabled (default `localhost:9223`).

Check agent status:

```powershell
dotnet wpflex devflow status
dotnet wpflex devflow status --host localhost --port 9223
```

Capture screenshot:

```powershell
dotnet wpflex devflow screenshot --output wpf-shot.png
```

Tap a UI element by DevFlow element id:

```powershell
dotnet wpflex devflow tap --id <element-id>
```

## 8. Output modes for automation

Global options (can appear before command):

- `--json` for machine-readable output
- `-v|--verbose` for verbose logs
- `--dry-run` to preview actions
- `--ci` for CI-friendly mode

Example:

```powershell
dotnet wpflex --json devflow status
dotnet wpflex --dry-run package .\src\MyWpfApp\MyWpfApp.csproj
```

## 9. Expected behavior and exit codes

- Success returns exit code `0`.
- Unknown command/invalid options return non-zero.
- `devflow status|screenshot|tap` return actionable error messages when agent is unreachable.
- `package` fails if publish fails; zip is only generated after successful publish.
