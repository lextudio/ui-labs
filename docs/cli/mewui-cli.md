# MewUI CLI Guide (`dotnet mewlex`)

This guide explains how to use the MewUI CLI for app workflows and DevFlow agent operations.

## 1. Install

```powershell
dotnet tool install -g LeXtudio.MewUI.Cli
```

If already installed:

```powershell
dotnet tool update -g LeXtudio.MewUI.Cli
```

## 2. Check available commands

```powershell
dotnet mewlex --help
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
- `devflow` (`status`, `screenshot`, `tap`, `extensions`, `inspector`, `broker`, `network`, `ui`, `alert`)

## 3. Scaffold a new MewUI app

```powershell
dotnet mewlex new MyMewApp
```

What happens:

- Creates folder `MyMewApp`
- Generates `MyMewApp.csproj`
- Generates `Program.cs`
- If run inside this repo layout, it also wires a local project reference to `LeXtudio.DevFlow.Agent.MewUI`

## 4. Build and run projects

From your project folder:

```powershell
dotnet mewlex build
dotnet mewlex run
```

Target a specific project:

```powershell
dotnet mewlex build .\src\MyMewApp\MyMewApp.csproj
dotnet mewlex run .\src\MyMewApp\MyMewApp.csproj
```

Common options:

- `-c|--configuration Debug|Release`
- `-r|--runtime <RID>`
- `--framework <TFM>`
- `--output <folder>` (for `build`/`publish`)

## 5. Publish and package

Publish:

```powershell
dotnet mewlex publish .\src\MyMewApp\MyMewApp.csproj -c Release
```

Package (publishes first, then zips output as `publish.zip` in current directory):

```powershell
dotnet mewlex package .\src\MyMewApp\MyMewApp.csproj -c Release
```

## 6. Environment and diagnostics

```powershell
dotnet mewlex doctor
dotnet mewlex env
dotnet mewlex diagnostics
```

## 7. Use MewUI CLI with DevFlow

Assumes your app is running with DevFlow enabled on `localhost:9223`.

Check agent status:

```powershell
dotnet mewlex devflow status
dotnet mewlex devflow status --host localhost --port 9223
```

Capture screenshot:

```powershell
dotnet mewlex devflow screenshot --output mewui-shot.png
```

Tap an element:

```powershell
dotnet mewlex devflow tap --id <element-id>
```

## 8. DevFlow: extensions, inspector, broker, network, ui, alert

```powershell
dotnet mewlex devflow extensions list
dotnet mewlex devflow extensions call mewui.echo --arg "hello"

dotnet mewlex devflow inspector --port 9223 --inspector-port 9300

dotnet mewlex devflow broker start
dotnet mewlex devflow broker list

dotnet mewlex devflow network list
dotnet mewlex devflow network detail --id <request-id>

dotnet mewlex devflow ui query --selector "Button:visible"
dotnet mewlex devflow ui hit-test --x 100 --y 50
dotnet mewlex devflow ui assert --selector "Button#Submit" --exists true

dotnet mewlex devflow alert detect
dotnet mewlex devflow alert dismiss --button OK
```

## 9. Output modes for automation

Global options:

- `--json`
- `-v|--verbose`
- `--dry-run`
- `--ci`

Examples:

```powershell
dotnet mewlex --json devflow status
dotnet mewlex --dry-run package .\src\MyMewApp\MyMewApp.csproj
```

## 10. Expected behavior and exit codes

- Success returns exit code `0`.
- Unknown command/invalid options return non-zero.
- `devflow` commands return clear connectivity/action errors if the agent is unavailable.
- `package` generates `publish.zip` only after a successful publish.
