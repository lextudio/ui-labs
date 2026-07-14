# LibreWPF DevFlow Test App

A sample WPF application instrumented with `LeXtudio.DevFlow.Agent.LibreWpf` for DevFlow testing.

## Usage

```bash
dotnet run
```

The DevFlow agent starts on port 5500 by default. Override via `DEVFLOW_AGENT_PORT` environment variable or `.mauidevflow` configuration.

## Features

- Button with tap interaction
- Scrollable content for scroll testing
- WebView2 control for WebView testing
