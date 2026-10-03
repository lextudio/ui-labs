# WpfDevFlowTestApp

This sample WPF app demonstrates a plain WPF app instrumented with DevFlow.

## Run the app

```powershell
cd src\DevFlow\WpfDevFlowTestApp
dotnet run
```

The app starts a DevFlow agent on port `9223`.

## DevFlow endpoints

- `GET http://localhost:9223/api/v1/agent/status`
- `GET http://localhost:9223/api/v1/ui/tree`
- `GET http://localhost:9223/api/v1/ui/elements/<id>`
- `GET http://localhost:9223/api/v1/ui/screenshot`
- `POST http://localhost:9223/api/v1/ui/actions/tap` with JSON body `{ "id": "<element-id>" }`
- `POST http://localhost:9223/api/v1/ui/actions/scroll` with JSON body `{ "id": "<element-id>", "deltaX": 0, "deltaY": 600 }`

## Example status request

```powershell
Invoke-WebRequest http://localhost:9223/api/v1/agent/status | Select-Object -ExpandProperty Content
```

## Example scroll request

```powershell
Invoke-RestMethod `
  -Method Post `
  -Uri http://localhost:9223/api/v1/ui/actions/scroll `
  -ContentType application/json `
  -Body '{ "id": "<element-id>", "deltaX": 0, "deltaY": 600 }'
```

## Why this is useful

- You get a process-local DevFlow agent without changing the WPF app architecture.
- The agent exposes a live DOM-like tree and screen capture for inspection and automation.
- This lets test tooling or remote helpers interact with the app using a simple HTTP API.
