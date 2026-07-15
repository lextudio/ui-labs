# CLI Projects

This directory contains the README entrypoint for the desktop CLI projects.

- [`src/Cli/src/LeXtudio.Wpf.Cli/README.md`](src/Cli/src/LeXtudio.Wpf.Cli/README.md) — Documentation for the WPF CLI package.
- [`src/Cli/src/LeXtudio.LibreWpf.Cli/README.md`](src/Cli/src/LeXtudio.LibreWpf.Cli/README.md) — Documentation for the LibreWPF CLI package.
- [`src/Cli/src/LeXtudio.WinForms.Cli/README.md`](src/Cli/src/LeXtudio.WinForms.Cli/README.md) — Documentation for the WinForms CLI package.
- [`src/Cli/src/LeXtudio.MewUI.Cli/README.md`](src/Cli/src/LeXtudio.MewUI.Cli/README.md) — Documentation for the MewUI CLI package.
- [`src/Cli/src/LeXtudio.Uno.Cli/README.md`](src/Cli/src/LeXtudio.Uno.Cli/README.md) — Documentation for the Uno CLI package.
- [`src/Cli/src/LeXtudio.Jalium.Cli/README.md`](src/Cli/src/LeXtudio.Jalium.Cli/README.md) — Documentation for the Jalium CLI package.

Each package should publish its own README independently so the NuGet contents stay separate and focused.

## DevFlow command surface

Every CLI exposes the same `devflow` subcommand tree against a running agent (default `localhost:9223`):

- `status`, `screenshot`, `tap` — core inspection/interaction (all CLIs)
- `webview` (`contexts`, `screenshot`, `cdp`) — WPF, WinForms, LibreWPF only
- `extensions` (`list`, `describe`, `call`) — discover and invoke `[DevFlowAction]` methods
- `inspector` — start the browser-based live UI inspector (`--inspector-port`, default `9300`)
- `broker` (`start`, `stop`, `status`, `list`) — multi-agent discovery daemon
- `network` (`list`, `detail`, `clear`) — captured HTTP traffic for apps using `DevFlowHttp.CreateClient()`
- `ui` (`query`, `hit-test`, `assert`) — CSS-selector queries, point hit-testing, structural assertions
- `alert` (`detect`, `dismiss`) — native dialog box detection/dismissal

Plus top-level `commands` (machine-readable command schema) and `batch` (newline-delimited JSON command execution over stdin).
