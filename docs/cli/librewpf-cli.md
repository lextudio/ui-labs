# LibreWPF CLI Guide (`dotnet librewpf`)

This guide explains how to use the LibreWPF CLI for project workflows and DevFlow agent operations. It mirrors the WPF CLI (`dotnet wpflex`) since LibreWPF shares the WPF agent and visual tree walker via linked source.

## 1. Install

```powershell
dotnet tool install -g LeXtudio.LibreWpf.Cli
```

If already installed:

```powershell
dotnet tool update -g LeXtudio.LibreWpf.Cli
```

## 2. Check available commands

```powershell
dotnet librewpf --help
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

## 3. Scaffold a new LibreWPF app

```powershell
dotnet librewpf new MyApp
```

What happens:

- Creates folder `MyApp`
- Generates `MyApp.csproj`
- Generates `Program.cs`

## 4. Build and run projects

From a solution/project folder:

```powershell
dotnet librewpf build
dotnet librewpf run
```

Target a specific project:

```powershell
dotnet librewpf build .\src\MyApp\MyApp.csproj
dotnet librewpf run .\src\MyApp\MyApp.csproj
```

Common build options:

- `-c|--configuration Debug|Release`
- `-r|--runtime <RID>`
- `--framework <TFM>`
- `--output <folder>` (for `build`/`publish`)

## 5. Publish and package

Publish:

```powershell
dotnet librewpf publish .\src\MyApp\MyApp.csproj -c Release
```

Package (publishes first, then zips output as `publish.zip` in current directory):

```powershell
dotnet librewpf package .\src\MyApp\MyApp.csproj -c Release
```

## 6. Environment and diagnostics

Validate CLI environment:

```powershell
dotnet librewpf doctor
```

Show runtime + SDK environment:

```powershell
dotnet librewpf env
dotnet librewpf diagnostics
```

## 7. Use LibreWPF CLI with DevFlow

Assumes your app is running with DevFlow agent enabled (default `localhost:9223`).

Check agent status:

```powershell
dotnet librewpf devflow status
dotnet librewpf devflow status --host localhost --port 9223
```

Capture screenshot:

```powershell
dotnet librewpf devflow screenshot --output librewpf-shot.png
```

Tap a UI element by DevFlow element id:

```powershell
dotnet librewpf devflow tap --id <element-id>
```

## 8. DevFlow: extensions, inspector, broker, network, ui, alert

Discover and invoke `[DevFlowAction]`-annotated methods in the running app:

```powershell
dotnet librewpf devflow extensions list
dotnet librewpf devflow extensions describe --name wpf.echo
dotnet librewpf devflow extensions call wpf.echo --arg "hello"
```

Start the browser-based live UI inspector (open the printed URL in a browser):

```powershell
dotnet librewpf devflow inspector --port 9223 --inspector-port 9300
```

Manage the multi-agent broker daemon (useful when several DevFlow-enabled apps are running at once):

```powershell
dotnet librewpf devflow broker start
dotnet librewpf devflow broker status
dotnet librewpf devflow broker list
dotnet librewpf devflow broker stop
```

Inspect HTTP traffic captured from apps that construct their `HttpClient` via `DevFlowHttp.CreateClient()`:

```powershell
dotnet librewpf devflow network list
dotnet librewpf devflow network detail --id <request-id>
dotnet librewpf devflow network clear
```

Query the live tree with CSS selectors, hit-test a point, or assert on selector matches:

```powershell
dotnet librewpf devflow ui query --selector "Button:visible"
dotnet librewpf devflow ui hit-test --x 223 --y 108
dotnet librewpf devflow ui assert --selector "Button#Submit" --exists true
```

Detect and dismiss native dialog boxes (`MessageBox`):

```powershell
dotnet librewpf devflow alert detect
dotnet librewpf devflow alert dismiss --button OK
```

## 9. Output modes for automation

Global options (can appear before command):

- `--json` for machine-readable output
- `-v|--verbose` for verbose logs
- `--dry-run` to preview actions
- `--ci` for CI-friendly mode

Example:

```powershell
dotnet librewpf --json devflow status
dotnet librewpf --dry-run package .\src\MyApp\MyApp.csproj
```

## 10. Expected behavior and exit codes

- Success returns exit code `0`.
- Unknown command/invalid options return non-zero.
- `devflow status|screenshot|tap` return actionable error messages when agent is unreachable.
- `package` fails if publish fails; zip is only generated after successful publish.
