# Desktop DevFlow

A Windows desktop DevFlow product designed for classic WPF and WinForms applications, with additional Uno, MewUI, LibreWPF, and Jalium coverage.

This folder contains the shared DevFlow runtime packages for WPF, WinForms, WinUI 3, Uno Platform, MewUI, LibreWPF, and Jalium applications.

Each package has its own package-specific README for the most relevant installation and usage guidance:

- `LeXtudio.DevFlow.Agent.Core/README.md`
- `LeXtudio.DevFlow.Agent.WPF/README.md`
- `LeXtudio.DevFlow.Agent.WinForms/README.md`
- `LeXtudio.DevFlow.Agent.Uno/README.md`
- `LeXtudio.DevFlow.Agent.MewUI/README.md`
- `LeXtudio.DevFlow.Agent.LibreWpf/README.md`
- `LeXtudio.DevFlow.Agent.Jalium/README.md`
- `LeXtudio.DevFlow.Driver/README.md`
- `LeXtudio.DevFlow.Inspector/README.md`
- `LeXtudio.DevFlow.Broker/README.md`

## NuGet packages

[![LeXtudio.DevFlow.Agent.Core](https://img.shields.io/nuget/v/LeXtudio.DevFlow.Agent.Core.svg)](https://www.nuget.org/packages/LeXtudio.DevFlow.Agent.Core)
[![LeXtudio.DevFlow.Agent.WPF](https://img.shields.io/nuget/v/LeXtudio.DevFlow.Agent.WPF.svg)](https://www.nuget.org/packages/LeXtudio.DevFlow.Agent.WPF)
[![LeXtudio.DevFlow.Agent.WinForms](https://img.shields.io/nuget/v/LeXtudio.DevFlow.Agent.WinForms.svg)](https://www.nuget.org/packages/LeXtudio.DevFlow.Agent.WinForms)
[![LeXtudio.DevFlow.Agent.Uno](https://img.shields.io/nuget/v/LeXtudio.DevFlow.Agent.Uno.svg)](https://www.nuget.org/packages/LeXtudio.DevFlow.Agent.Uno)
[![LeXtudio.DevFlow.Agent.MewUI](https://img.shields.io/nuget/v/LeXtudio.DevFlow.Agent.MewUI.svg)](https://www.nuget.org/packages/LeXtudio.DevFlow.Agent.MewUI)
[![LeXtudio.DevFlow.Agent.LibreWpf](https://img.shields.io/nuget/v/LeXtudio.DevFlow.Agent.LibreWpf.svg)](https://www.nuget.org/packages/LeXtudio.DevFlow.Agent.LibreWpf)
[![LeXtudio.DevFlow.Agent.Jalium](https://img.shields.io/nuget/v/LeXtudio.DevFlow.Agent.Jalium.svg)](https://www.nuget.org/packages/LeXtudio.DevFlow.Agent.Jalium)
[![LeXtudio.DevFlow.Driver](https://img.shields.io/nuget/v/LeXtudio.DevFlow.Driver.svg)](https://www.nuget.org/packages/LeXtudio.DevFlow.Driver)

Install the runtime package for your UI stack:

```powershell
dotnet add package LeXtudio.DevFlow.Agent.WPF
dotnet add package LeXtudio.DevFlow.Driver
```

```powershell
dotnet add package LeXtudio.DevFlow.Agent.WinForms
dotnet add package LeXtudio.DevFlow.Driver
```

```powershell
dotnet add package LeXtudio.DevFlow.Agent.Uno
dotnet add package LeXtudio.DevFlow.Driver
```

```powershell
dotnet add package LeXtudio.DevFlow.Agent.LibreWpf
dotnet add package LeXtudio.DevFlow.Driver
```

```powershell
dotnet add package LeXtudio.DevFlow.Agent.Jalium
dotnet add package LeXtudio.DevFlow.Driver
```

## What is included

- `LeXtudio.DevFlow.Agent.Core` — UI-stack-agnostic DevFlow HTTP server, DTOs, CSS selector/query engine, network traffic capture, and Win32 alert detection
- `LeXtudio.DevFlow.Agent.WPF` — WPF-specific visual tree walker, screenshot capture, and UI interaction support
- `LeXtudio.DevFlow.Agent.WinForms` — WinForms-specific control tree walker, screenshot capture, and UI interaction support
- `LeXtudio.DevFlow.Agent.Uno` — Uno Platform and WinUI 3 registration and visual tree support
- `LeXtudio.DevFlow.Agent.MewUI` — MewUI runtime support via NuGet-deployed Aprillz.MewUI packages
- `LeXtudio.DevFlow.Agent.LibreWpf` — LibreWPF runtime support, sharing the WPF visual tree walker via linked source
- `LeXtudio.DevFlow.Agent.Jalium` — Jalium runtime support via Jalium.UI.Controls
- `LeXtudio.DevFlow.Driver` — HTTP client for querying a running DevFlow agent
- `LeXtudio.DevFlow.Inspector` — browser-based live UI inspector server, hosted by each CLI's `devflow inspector` command
- `LeXtudio.DevFlow.Broker` — multi-agent broker daemon for discovery/registration, hosted by each CLI's `devflow broker` command
- `LeXtudio.DevFlow.Analyzers` — Roslyn analyzer validating `[DevFlowAction]` method signatures at compile time

## Build

From the repo root:

```powershell
cd src\DevFlow
dotnet build WpfDevFlow.sln
```

## Use

WPF apps can register the agent during startup:

```csharp
using LeXtudio.DevFlow.Agent.WPF;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        this.AddWpfDevFlowAgent();
    }
}
```

Uno Platform and WinUI 3 apps can register the Uno agent:

```csharp
using LeXtudio.DevFlow.Agent.Uno;

builder.UseUnoDevFlowAgent();
```

WinForms apps can register the agent through an `ApplicationContext`:

```csharp
using LeXtudio.DevFlow.Agent.WinForms;
using Microsoft.Maui.DevFlow.Agent.Core;

var form = new MainForm();
var context = new ApplicationContext(form);
context.AddWinFormsDevFlowAgent(new AgentOptions { Port = 9223 });
Application.Run(context);
```

LibreWPF apps register the agent the same way as WPF — `LeXtudio.DevFlow.Agent.LibreWpf` shares the WPF agent service via linked source, so the API lives under the `LeXtudio.DevFlow.Agent.Wpf` namespace:

```csharp
using LeXtudio.DevFlow.Agent.Wpf;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        this.AddWpfDevFlowAgent();
    }
}
```

Jalium apps register the agent during startup:

```csharp
using LeXtudio.DevFlow.Agent.Jalium;
using Microsoft.Maui.DevFlow.Agent.Core;

Application.Current!.AddJaliumDevFlowAgent(new AgentOptions { Port = 9223 });
```

## Web API

By default, the sample apps start the agent on port `9223`.
You can override the port at build time with `dotnet build -p:MauiDevFlowPort=9500` or by adding a `.mauidevflow` file to your project directory.

| Request | Description |
|---------|-------------|
| `GET /api/v1/agent/status` | Read agent status. |
| `GET /api/v1/ui/tree` | Read the live UI tree. |
| `GET /api/v1/ui/element?id=<id>` | Read one UI element by id. |
| `GET /api/v1/ui/elements?type=&automationId=&text=` | Query elements by type, automation id, or text. |
| `GET /api/v1/ui/query-selector?selector=<css>` | Query elements with a CSS selector (type, class, attribute, `:visible`/`:enabled`/`:focused`, combinators). |
| `GET /api/v1/ui/hit-test?x=&y=` | Return the ancestor chain (and topmost match) at a point. |
| `POST /api/v1/ui/assert` | Assert selector match count/existence/text with body `{ "selector": "...", "exists": true, "count": 1, "textEquals": "...", "textContains": "..." }`. |
| `GET /api/v1/ui/screenshot` | Capture a screenshot. |
| `GET /api/v1/ui/screenshot?id=<id>` | Capture a screenshot for one element/control. |
| `GET /api/v1/ui/screenshot?selector=%23<id>` | Capture a screenshot using a selector. |
| `GET /api/v1/webview/contexts` | List discoverable WebView contexts. |
| `GET /api/v1/webview/screenshot?context=<id>` | Capture a WebView screenshot. |
| `POST /api/v1/webview/cdp` | Execute a WebView CDP command. |
| `POST /api/v1/ui/tap` | Tap an element with body `{ "id": "<element-id>" }`. |
| `POST /api/v1/ui/actions/fill` | Fill text with body `{ "elementId": "<element-id>", "text": "value" }`. |
| `POST /api/v1/ui/actions/clear` | Clear text with body `{ "elementId": "<element-id>" }`. |
| `POST /api/v1/ui/actions/focus` | Focus an element with body `{ "elementId": "<element-id>" }`. |
| `POST /api/v1/ui/actions/key` | Send key/text input with body `{ "elementId": "<element-id>", "text": "A" }`. |
| `POST /api/v1/ui/actions/scroll` | Scroll an element with body `{ "id": "<element-id>", "deltaX": 0, "deltaY": 600 }`. |
| `GET /api/v1/invoke/actions` | List discovered `[DevFlowAction]` methods with their parameters. |
| `POST /api/v1/invoke/actions/{name}` | Invoke a `[DevFlowAction]` method with body `{ "args": [...] }`. |
| `GET /api/v1/network/list?count=&host=&method=&status=` | List captured HTTP requests (apps opt in via `DevFlowHttp.CreateClient()`). |
| `GET /api/v1/network/detail?id=<id>` | Full detail (headers/body) for one captured request. |
| `POST /api/v1/network/clear` | Clear the captured network log. |
| `GET /api/v1/alert/detect` | Detect a native dialog box (Win32 `#32770` class) and return its message/buttons. |
| `POST /api/v1/alert/dismiss` | Dismiss a detected dialog with body `{ "buttonLabel": "OK" }` (omit to click the first button). |
| `GET /api/v1/device/app/theme` / `PUT /api/v1/device/app/theme` | Get or set the app theme. |

Each CLI's `devflow` subcommand wraps this API — see [Core Commands / DevFlow Commands](../Cli/README.md) for the command-line surface, plus the standalone `LeXtudio.DevFlow.Inspector` (`devflow inspector`) and `LeXtudio.DevFlow.Broker` (`devflow broker`) packages for the browser inspector and multi-agent daemon.

## WinForms support

- `LeXtudio.DevFlow.Agent.WinForms` is the WinForms DevFlow runtime package.
- `WinFormsDevFlowTestApp` is a reference sample project demonstrating a process-local DevFlow HTTP agent in a classic WinForms app.
- `LeXtudio.DevFlow.Agent.WinForms.Tests` covers status, tree inspection, query, screenshots, tap, fill, clear, focus, key, scroll, and structured error behavior.
- WinForms WebView/CDP and app theme APIs are currently not advertised as supported capabilities.

## Uno support preview

- `LeXtudio.DevFlow.Agent.Uno` is the Uno DevFlow platform package.
- `UnoDevFlow.sln` contains the shared agent core plus the Uno project.
- The Uno package supports registration, tree walking, screenshots, tap, and scroll through the shared Web API.
- Uno WebView/CDP support is currently best-effort and target-dependent.
- WebView integration tests currently run on `net10.0-desktop` only; `net10.0-windows10.0.19041.0` (WinUI) WebView tests are temporarily excluded due to unstable build/runtime behavior in CI/dev environments.

## MewUI support preview

- `LeXtudio.DevFlow.Agent.MewUI` is a new MewUI runtime package that uses `Aprillz.MewUI.Core` and `Aprillz.MewUI.Platform.Win32`.
- `MewUIDevFlowTestApp` is a reference sample project demonstrating how to start a MewUI app and host the DevFlow HTTP agent.
- Register DevFlow during your app startup with `Application.Current.AddMewUIDevFlowAgent()`.

## LibreWPF support preview

- `LeXtudio.DevFlow.Agent.LibreWpf` is the LibreWPF DevFlow runtime package. It shares the WPF visual tree walker and agent service via linked source rather than duplicating it.
- `LibreWpfDevFlowTestApp` is a reference sample project demonstrating a process-local DevFlow HTTP agent in a LibreWPF app.
- `LeXtudio.DevFlow.Agent.LibreWpf.Tests` mirrors the WPF integration test coverage.

## Jalium support preview

- `LeXtudio.DevFlow.Agent.Jalium` is the Jalium DevFlow runtime package, built on `Jalium.UI.Controls`.
- `JaliumDevFlowTestApp` is a reference sample project demonstrating how to start a Jalium app and host the DevFlow HTTP agent.
- `LeXtudio.DevFlow.Agent.Jalium.Tests` covers the same status/tree/query/screenshot/interaction surface as the other agents.

## Reuse strategy

The DevFlow projects reuse source from `external/maui-labs` where it makes sense, consumed as linked (`<Compile Include>`) source files rather than copy-pasted:

- `LeXtudio.DevFlow.Agent.Core` links `AgentHttpServer.cs`, `AgentOptions.cs`, `AgentExtension.cs`, `DevFlowActionAttribute.cs`, `ElementInfo.cs`, `BrokerRegistration.cs` from `Microsoft.Maui.DevFlow.Agent.Core`, plus the framework-agnostic network capture (`Network/*.cs`) and CSS selector engine (`Css/*.cs`, backed by the `Fizzler` package).
- `LeXtudio.DevFlow.Driver` links the full upstream `AgentClient.cs` and its DTOs from `Microsoft.Maui.DevFlow.Driver`, coexisting with a smaller hand-written `LeXtudio.DevFlow.Driver.AgentClient` used by the CLIs — both talk to the same linked `AgentHttpServer.cs`, so no adaptation is needed.
- `LeXtudio.DevFlow.Inspector` links `InspectorServer.cs`, `HtmlRenderer.cs`, and `LocalOriginValidator.cs` (plus embedded `devflow.css`/`devflow.js`/`inspector.html`) from `Microsoft.Maui.Cli`'s Web Inspector.
- `LeXtudio.DevFlow.Broker` links `BrokerServer.cs`, `BrokerClient.cs`, `AgentRegistration.cs`, `BrokerPaths.cs` from `Microsoft.Maui.Cli`'s broker daemon, with a small original `CliJson` helper replacing upstream's AOT source-gen JSON context (which pulls in mobile/Android-specific types we don't need).
- Some features could not be linked because upstream ties them to MAUI-specific APIs or a monolithic MAUI-coupled service class — for those (the `ui` query/hit-test/assert routes, network route wiring, and Win32 alert detection), the wiring lives directly in `LeXtudio.DevFlow.Agent.Core/DevFlowAgentServiceBase.cs` as original code operating over the shared `ElementInfo` model, so it still works uniformly across every framework.

## Notes

- The Desktop DevFlow product is focused on WPF, WinForms, WinUI 3, Uno Platform, MewUI, LibreWPF, and Jalium, not MAUI.
- `WpfDevFlow.sln` is the local desktop solution for this product.
- `DevFlow.slnf` is an external MAUI DevFlow wrapper that now also references the local WinForms projects used by this product.
