# DevFlow Documentation

This directory contains guides and FAQs for adopting and using DevFlow in your applications.

## Getting Started

Choose the guide that matches your application framework:

- **[WPF Guide](howto-wpf.md)** — Add DevFlow to an existing WPF application
- **[Uno Platform / WinUI 3 Guide](howto-uno.md)** — Add DevFlow to an Uno or pure WinUI 3 project
- **[MewUI Guide](howto-mewui.md)** — Add DevFlow to a MewUI application
- **[Jalium Guide](howto-jalium.md)** — Add DevFlow to a Jalium application

Each guide covers:
- Prerequisites
- NuGet package installation
- Agent registration at startup
- Build and run instructions
- Verification steps
- Optional port configuration
- Recommended best practices

## Technical Notes

Deeper notes for DevFlow maintainers (not needed for adoption):

- **[Synthesized Drag on Uno-Skia-macOS](technote-uno-macos-drag.md)** — why injecting a real drag/drop gesture is subtle on macOS, and the fixes required to deliver a full `PointerPressed → PointerMoved → PointerReleased` sequence.

## FAQ

See the [DevFlow FAQ](faq.md) for answers to common questions, including:

- Port configuration (build-time, runtime, environment variables)
- How to override the port in code
- How to verify the agent is running
- Multi-app scenarios

## What is DevFlow?

DevFlow is a local HTTP server that your application hosts while running. It provides:

- Live UI tree inspection
- Screenshot capture
- Element introspection by ID
- UI interaction (tap, scroll)
- Agent status monitoring

The default port is `9223`, but can be customized during build or at runtime.

## Quick Reference

### Default port verification

```powershell
Invoke-WebRequest http://localhost:9223/api/v1/agent/status | Select-Object -ExpandProperty Content
```

### Build-time port override

```powershell
dotnet build -p:MauiDevFlowPort=9500
```

### Runtime port override (in code)

```csharp
// WPF
this.AddWpfDevFlowAgent(new AgentOptions { Port = 9500 });

// Uno / WinUI 3
Application.Current.AddUnoDevFlowAgent(new AgentOptions { Port = 9500 });

// MewUI
Application.Current.AddMewUIDevFlowAgent(new AgentOptions { Port = 9500 });

// Jalium
app.AddJaliumDevFlowAgent(new AgentOptions { Port = 9500 });
```

## Related Packages

- `LeXtudio.DevFlow.Agent.Core` — Shared DevFlow runtime and HTTP server
- `LeXtudio.DevFlow.Agent.WPF` — WPF integration
- `LeXtudio.DevFlow.Agent.Uno` — Uno Platform and WinUI 3 integration
- `LeXtudio.DevFlow.Agent.MewUI` — MewUI integration
- `LeXtudio.DevFlow.Agent.Jalium` — Jalium integration
- `LeXtudio.DevFlow.Driver` — HTTP client for querying a DevFlow agent

See [the main DevFlow README](../src/DevFlow/README.md) for more information about packages and architecture.
