# DevFlow FAQ

## Port Configuration

### Q: What is the default port?

The default port is `9223`, which matches the MAUI DevFlow agent default.

### Q: How do I override the port in code?

Pass a custom port when registering the agent:

**WPF:**
```csharp
using LeXtudio.DevFlow.Agent.WPF;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        this.AddWpfDevFlowAgent(new AgentOptions { Port = 9500 });
    }
}
```

**Uno Platform / WinUI 3:**
```csharp
using LeXtudio.DevFlow.Agent.Uno;

Application.Current.AddUnoDevFlowAgent(new AgentOptions { Port = 9500 });
```

**MewUI:**
```csharp
using LeXtudio.DevFlow.Agent.MewUI;

Application.Current.AddMewUIDevFlowAgent(new AgentOptions { Port = 9500 });
```

**Jalium:**
```csharp
using LeXtudio.DevFlow.Agent.Jalium;

app.AddJaliumDevFlowAgent(new AgentOptions { Port = 9500 });
```

### Q: macOS drag, click and press actions report success but nothing happens. Why?

On macOS the WPF agents post OS-level mouse events through CliclickSharp (or Homebrew `cliclick`). macOS
silently discards those events unless the process that runs them is trusted for Accessibility (System
Settings > Privacy & Security > Accessibility); the helper still exits successfully, so the action reports
`ok`. Either grant Accessibility to the terminal/IDE running the tests, or run the app with

```bash
DEVFLOW_POINTER_MODE=in-process dotnet run ...
```

which routes `click`, `drag` and the decomposed `press`/`drag-move`/`release` actions through the app's own
portable WPF input pipeline (responses report `"mode": "portable-wpf"`). That needs no Accessibility
permission and does not move the real cursor, but it only reaches the app's own windows, supports the left
button only, and `tap` on a button is unchanged (it already invokes buttons semantically). Linux and
Windows are unaffected.

### Q: How do I override the port with an environment variable?

If your app reads `DEVFLOW_AGENT_PORT` at startup and passes it to the agent, you can override the port:

**WPF Example:**
```csharp
using LeXtudio.DevFlow.Agent.WPF;
using Microsoft.Maui.DevFlow.Agent.Core;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var port = AgentOptions.DefaultPort; // 9223
        
        var portValue = Environment.GetEnvironmentVariable("DEVFLOW_AGENT_PORT");
        if (!string.IsNullOrWhiteSpace(portValue) && int.TryParse(portValue, out var customPort) && customPort > 0)
        {
            port = customPort;
        }

        this.AddWpfDevFlowAgent(new AgentOptions { Port = port });
    }
}
```

Then run with:
```powershell
$env:DEVFLOW_AGENT_PORT = "9500"
dotnet run
```

### Q: How do I override the port at build time?

Use the `MauiDevFlowPort` MSBuild property:

```powershell
dotnet build -p:MauiDevFlowPort=9500
```

This generates assembly metadata that the agent reads at runtime.

### Q: How do I configure the port in a project file?

Create a `.mauidevflow` file in your project directory:

```json
{
  "port": 9500
}
```

The agent will read this file at runtime if no explicit code-based port is set.

### Q: What is the port priority order?

1. Explicit code: `new AgentOptions { Port = X }`
2. Environment variable: `DEVFLOW_AGENT_PORT`
3. Assembly metadata: compiled from `.mauidevflow` or `-p:MauiDevFlowPort`
4. Default: `9223`

The agent uses the first applicable option in this order.

### Q: How do I verify the agent is running on the correct port?

```powershell
# Check default port
curl http://localhost:9223/api/v1/agent/status

# Check custom port
curl http://localhost:9500/api/v1/agent/status
```

Or from PowerShell:
```powershell
Invoke-WebRequest http://localhost:9223/api/v1/agent/status | Select-Object -ExpandProperty Content
```

### Q: Can I run multiple agents on different ports?

Yes. Each application instance can run on a different port by:

1. Passing different ports in code via `AgentOptions`
2. Using different environment variables per app
3. Creating separate `.mauidevflow` files per project

For example, in a multi-app test scenario:
```csharp
// WPF
app1.AddWpfDevFlowAgent(new AgentOptions { Port = 9223 });
app2.AddWpfDevFlowAgent(new AgentOptions { Port = 9224 });

// Jalium
app1.AddJaliumDevFlowAgent(new AgentOptions { Port = 9223 });
app2.AddJaliumDevFlowAgent(new AgentOptions { Port = 9224 });
```

Or with environment variables:
```powershell
# App 1
$env:DEVFLOW_AGENT_PORT = "9223"; dotnet run --project App1

# App 2 (in another terminal)
$env:DEVFLOW_AGENT_PORT = "9224"; dotnet run --project App2
```
