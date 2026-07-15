# Uno CLI Guide (`dotnet unolex`)

This guide explains how to use the Uno CLI for app workflows and DevFlow agent operations.

## 1. Install

```powershell
dotnet tool install -g LeXtudio.Uno.Cli
```

If already installed:

```powershell
dotnet tool update -g LeXtudio.Uno.Cli
```

## 2. Check available commands

```powershell
dotnet unolex --help
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
- `commands` — list available commands in machine-readable JSON form
- `batch` — execute newline-delimited JSON command batches from stdin
- `devflow` (`status`, `screenshot`, `tap`, `webview`, `extensions`, `inspector`, `broker`, `network`, `ui`, `alert`)

## 3. Scaffold a new Uno app

```powershell
dotnet unolex new MyUnoApp
```

What happens:

- Creates folder `MyUnoApp`
- Generates `MyUnoApp.csproj`
- Generates `Program.cs`

## 4. Build and run projects

From your project folder:

```powershell
dotnet unolex build
dotnet unolex run
```

Target a specific project:

```powershell
dotnet unolex build .\src\MyUnoApp\MyUnoApp.csproj
dotnet unolex run .\src\MyUnoApp\MyUnoApp.csproj
```

Common options:

- `-c|--configuration Debug|Release`
- `-r|--runtime <RID>`
- `--framework <TFM>`
- `--output <folder>` (for `build`/`publish`)

## 5. Publish and package

Publish:

```powershell
dotnet unolex publish .\src\MyUnoApp\MyUnoApp.csproj -c Release
```

Package (publishes first, then zips output as `publish.zip` in current directory):

```powershell
dotnet unolex package .\src\MyUnoApp\MyUnoApp.csproj -c Release
```

## 6. Environment and diagnostics

```powershell
dotnet unolex doctor
```

Show runtime + SDK environment:

```powershell
dotnet unolex env
dotnet unolex diagnostics
```

## 7. Use Uno CLI with DevFlow

Assumes your app is running with DevFlow enabled on `localhost:9223`.

Check agent status:

```powershell
dotnet unolex devflow status
dotnet unolex devflow status --host localhost --port 9223
```

Capture screenshot:

```powershell
dotnet unolex devflow screenshot --output uno-shot.png
```

Tap an element by DevFlow element id:

```powershell
dotnet unolex devflow tap --id <element-id>
```

## 8. DevFlow: extensions, inspector, broker, network, ui, alert

```powershell
dotnet unolex devflow extensions list
dotnet unolex devflow extensions call uno.echo --arg "hello"

dotnet unolex devflow inspector --port 9223 --inspector-port 9300

dotnet unolex devflow broker start
dotnet unolex devflow broker list

dotnet unolex devflow network list
dotnet unolex devflow network detail --id <request-id>

dotnet unolex devflow ui query --selector "Button:visible"
dotnet unolex devflow ui hit-test --x 100 --y 50
dotnet unolex devflow ui assert --selector "Button#Submit" --exists true

dotnet unolex devflow alert detect
dotnet unolex devflow alert dismiss --button OK
```

## 9. Output modes for automation

Global options:

- `--json`
- `-v|--verbose`
- `--dry-run`
- `--ci`

Examples:

```powershell
dotnet unolex --json devflow status
dotnet unolex --dry-run package .\src\MyUnoApp\MyUnoApp.csproj
```

## 10. Expected behavior and exit codes

- Success returns exit code `0`.
- Unknown command/invalid options return non-zero.
- `devflow` commands return clear connectivity/action errors if the agent is unavailable.
- `package` generates `publish.zip` only after a successful publish.
