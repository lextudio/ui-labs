# DevFlow Port Plan (MAUI -> WPF/Uno/MewUI)

## Scope rule
- Port only features that already exist in MAUI DevFlow contract.
- Do not invent new protocol fields/endpoints unless MAUI adds them first.

## Already ported
- Core agent status and UI inspection:
  - `GET /api/v1/agent/status`
  - `GET /api/v1/ui/tree`
  - `GET /api/v1/ui/elements/{id}`
  - `GET /api/v1/ui/elements`
  - `GET /api/v1/ui/screenshot` (full + element; selector where supported)
- UI actions:
  - `POST /api/v1/ui/actions/tap`
  - `POST /api/v1/ui/actions/scroll`
  - `POST /api/v1/ui/actions/fill`
  - `POST /api/v1/ui/actions/clear`
  - `POST /api/v1/ui/actions/focus`
  - `POST /api/v1/ui/actions/key`
  - `POST /api/v1/ui/actions/back`
  - `POST /api/v1/ui/actions/batch`
- WebView surface currently in local code:
  - `GET /api/v1/webview/contexts`
  - `GET /api/v1/webview/screenshot`
  - `POST /api/v1/webview/cdp`
- Integration tests in place for WPF/Uno/MewUI for status, screenshot, tap/scroll, and batch baseline.
- Invoke parity baseline:
  - `GET /api/v1/invoke/actions`
  - `POST /api/v1/invoke/actions/{name}`

## Feature comparison
| Feature | MAUI | wpf-labs status | Notes |
|---|---|---|---|
| Agent status/tree/element/screenshot | Yes | Done | Parity baseline complete |
| UI actions: tap/fill/clear/focus/key/back/scroll | Yes | Done | Parity baseline complete |
| UI actions: batch | Yes | Done | Added + integration tests |
| Invoke: list/invoke actions | Yes | Done (baseline) | Static public action discovery + invoke |
| WebView contexts/screenshot/cdp | Yes | Done (baseline) | DOM/input helper endpoints still missing |
| WebView DOM/query/navigate/input APIs | Yes | Missing | Next WebView parity slice |
| Profiler APIs | Yes | Missing | Capabilities/sessions/samples/spans |
| UI actions: navigate/gesture/resize | Yes | Missing | Keep MAUI payload parity |

## Still missing vs MAUI
- MAUI `invoke` action system:
  - `GET /api/v1/invoke/actions`
  - `POST /api/v1/invoke/actions/{name}`
  - Runtime action discovery/execution plumbing
- MAUI profiler API family:
  - `/api/v1/profiler/capabilities`
  - `/api/v1/profiler/sessions` (create/delete)
  - `/api/v1/profiler/sessions/{id}/samples`
  - `/api/v1/profiler/spans`
- MAUI richer WebView endpoints (beyond raw CDP passthrough), for example:
  - `/api/v1/webview/dom`
  - `/api/v1/webview/dom/query`
  - `/api/v1/webview/navigate`
  - `/api/v1/webview/input/*`
- MAUI action endpoints not yet present locally:
  - `POST /api/v1/ui/actions/navigate`
  - `POST /api/v1/ui/actions/gesture`
  - `POST /api/v1/ui/actions/resize` (if we keep strict MAUI parity, this must match MAUI semantics)
- Capability parity gaps:
  - MAUI-style capability documents for jobs/profiler/invoke feature sets.

## Runtime caveats
- Uno WinUI target (`net10.0-windows10.0.19041.0`) still has intermittent/blocked test-host build issues (XAML compiler), so WebView-related integration validation should remain desktop-only for now.
- WPF supports WebView runtime paths; keep tests scoped to stable scenarios to avoid long hangs.

## Recommended next port order
1. Profiler API parity (capabilities/sessions/samples/spans) with capability flags aligned to MAUI.
2. UI action parity endpoints (`navigate`, `gesture`, `resize`) only with MAUI-compatible payloads.
3. Rich WebView DOM/navigation/input helpers (layered over CDP where applicable), with opt-in tests per runtime.

## Acceptance criteria for each new port
- Contract matches MAUI route + payload names.
- No changes required in `external/maui-labs` sources.
- Integration test added for WPF and for Uno/MewUI where runtime support is real.
- Capability flags updated only for existing MAUI features.

## Contract alignment (route renames)

The desktop agents now use the same route names as the shared DevFlow contract
(`external/maui-labs/docs/DevFlow/spec/openapi.yaml`). Upstream states that spec is framework-agnostic and
intended for "MAUI and other UI stacks", so it is the authority for the wire surface.

| Previous route | Contract route |
|---|---|
| `POST /api/v1/ui/tap` | `POST /api/v1/ui/actions/tap` |
| `GET /api/v1/ui/element?id=<id>` | `GET /api/v1/ui/elements/<id>` |
| `GET /api/v1/network/list` | `GET /api/v1/network/requests` |
| `GET /api/v1/network/detail?id=<id>` | `GET /api/v1/network/requests/<id>` |
| `POST /api/v1/network/clear` | `DELETE /api/v1/network/requests` |

Notes:
- These are breaking changes for any external script or automation that called the old paths. CLI
  subcommand names (`devflow network list|detail|clear`) are unchanged.
- Element and network ids moved from a query string to a path segment, so they are percent-encoded by
  the caller and unescaped by the agent.
- `POST /api/v1/ui/actions/tap` was the last action still outside `/api/v1/ui/actions/`; the rename also
  removed an internal inconsistency.

## Contract enforcement

`LeXtudio.DevFlow.Agent.Core.Tests` (cross-platform, runs on macOS) holds the tests that keep the
desktop agents aligned with the pinned contract:

- `DevFlowProtocolParityTests` — parses the pinned `openapi.yaml` and compares it against the routes the
  agent actually registers. Two tracked lists act as the sync backlog:
  - `ContractPathsNotYetServed` — contract paths not implemented yet (42 remaining).
  - `DesktopAdditionsOutsideContract` — routes we serve that the contract does not describe (16:
    pointer input, alert detection, invoke, `ui/assert`, `ui/query-selector`, `webview/cdp`).
  Shrink these lists as endpoints are adopted; the tests then fail on any undeclared drift.
- `DeepTreeSerializationTests` — protects the LeXtudio fork JSON behavior that upstream does not carry
  (deep UI trees and self-referencing trees). Current shape: 120-level trees and cycles serialize, and
  the tests fail if that regresses.

Because the spec is copied from the submodule at build time, bumping the submodule pointer moves the
contract these tests enforce without editing them.
