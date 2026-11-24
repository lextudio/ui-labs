# Sync roadmap: wpf-labs DevFlow with upstream MAUI DevFlow (2026-11-24)

Goals: (1) take the important upstream changes, (2) shrink our own code, (3) fix what is broken (macOS screenshot, NativeAOT).

## Which upstream revision

`dotnet/maui-labs` has **no stable tag newer than our base**. The latest release is `v0.1.0-preview.10` (2026-05-24); our submodule
(`external/maui-labs`, the lextudio fork) was `cb4a968d`, which sat 133 commits *after* that tag (upstream main of 2026-10-02) plus 2 fork
commits. Moving to the tag would be a downgrade. So the target is an upstream **main commit whose CI is green**:

- Target: `a29273ad` (upstream main, 2026-11-24; the fork is rebased onto it and carries 3 commits on top (the 2 older ones and `AgentOptions.SerializeMutations`), so the submodule pointer is `6af8a904`, tagged `lextudio-sync-2026-11-24`; "CI - DevFlow", "CI - Linux GTK4" and the others succeeded on that sha).
- Distance: 142 upstream commits (50 touch `src/DevFlow`), 1344 files. The fork's three commits on top (the response/port commits and the `SerializeMutations` patch) are replayed onto it.
- Keep the pin as an annotated tag on the fork (`lextudio-sync-2026-11-24`) so the next sync has a stable point to diff from.

**Uncommitted work in the submodule must survive**: `AgentHttpServer.cs` and `DevFlowAgentService.cs` carry an uncommitted change
(`SerializeMutations`: a host that disables mutation leases also stops serializing mutating routes; OpenDevelop's integration-test child
needs it to drive a modal dialog). Commit it to the fork first (branch `lextudio/serialize-mutations`), then rebase onto the target; propose it upstream.

## What upstream changed that matters to us

| Upstream | Why it matters here |
|---|---|
| #435 focus-safe macOS capture, `AppKitScreenshotCapture.cs` (Abstractions), AppKit `NativeUi.AppKit.cs` | The **macOS screenshot** fix: LibreWPF has no `RenderTargetBitmap` path off Windows. Capture the app's own window with AppKit/CoreGraphics (no Screen Recording permission for a process's own window; my sample's `MacWindowCapture` proved that). |
| #548 / #822 pointer sessions (`DevFlowAgentService.PointerSessions.cs`), #488 | Real touch/drag sequences: the missing "drag" and "gesture" parity in `docs/devflow/plan.md`. |
| #750, #839 | .NET 11 migration; backends (GTK, AppKit, **WPF**) now `net10.0;net11.0` compatible. Abstractions is `net10.0;net11.0`; we stay on `net10.0` for consumers. |
| `Agent.WPF` (upstream, 1418 lines) | Upstream now ships its own WPF agent. Ours (`LeXtudio.DevFlow.Agent.Wpf`, 2022 lines) predates it. |
| #526/#527 and the `Agent.AotSmoke` project | Reflection-disabled NativeAOT: source-generated JSON contexts (`MauiAgentJsonContext`, `AgentJsonContext`); CI publishes the agent as a NativeAOT exe. |
| #845, #826, #707, #695 | Native process execution instead of ProcessUtils, no host-side SkiaSharp in CLI screenshots, Driver packages bundle SDK tooling, CLI operation groups. |
| #816-#823 Inspector, #486, #476 | Inspector protocol: versioned frames, latest-wins refresh, capability-driven commands. Our `LeXtudio.DevFlow.Inspector` links 15 upstream files and will break on renames. |
| #677 | DevFlow integration CI that runs automatically. |

## What to simplify on our side

1. **Agent.Wpf vs upstream `Agent.WPF`.** Diff the two (`WpfAgentService.cs`, `WpfVisualTreeWalker.cs`). Everything identical or superseded is deleted; what remains is
   a thin layer (or `partial`/subclass) for what only we need. Decision rule: keep our code only where it implements something upstream lacks (LibreWPF specifics,
   WebView CDP, OpenDevelop hooks). `LeXtudio.DevFlow.Agent.LibreWpf` (614 lines) is built on top of it and must keep working.
2. **`LeXtudio.DevFlow.Agent.Core`** is 39 source links plus 3734 own lines: re-derive the link list from upstream's new file set (new: `AppKitScreenshotCapture.cs`,
   `DevFlowAgentService.PointerSessions.cs`, `DevFlowAgentService.UiActionEvents.cs`; changed: `AgentJsonContext.cs`, `AgentOptions.cs`, `BrokerRegistration.cs`, ...), and
   delete own code that now duplicates a linked file.
3. **`Driver` (12 links), `Inspector` (15), `Broker` (7)**: same re-derivation; prefer referencing upstream projects/packages over linking files where the TFM allows.
4. `docs/devflow/plan.md` ("Still missing vs MAUI") is rewritten from the new feature comparison; the "no changes in `external/maui-labs`" rule keeps its exception for the
   `SerializeMutations` patch (upstreamed or carried on the fork branch).

## What to fix

1. **macOS screenshot** (`LeXtudio.DevFlow.Agent.LibreWpf`, `...ProGPU`): use the upstream AppKit capture for the host window; verify on macOS with a real run
   (`/api/v1/ui/screenshot` returns a non-blank PNG of the window; the screen being locked yields blank captures, so the test must skip, not fail, on a locked session).
2. **NativeAOT**: with upstream's source-generated contexts, add the types our agents return to an extension context (upstream README, "AgentExtension"), and add an
   AotSmoke-style test for the WPF/LibreWPF agent (`status`, `ui/tree`, one action under `PublishAot`, reflection disabled). Property reads in the visual-tree walker that use
   reflection get explicit accessors or a documented rd.xml root.
3. Anything the upstream diff shows that our tests cover: the existing suites (Core, Wpf, LibreWpf, Uno, MewUI, WinForms, Jalium) must stay green.

## Phases (each one a reviewed commit)

1. **Prepare**: commit the `SerializeMutations` patch on the fork; record the baseline test result of every suite that runs on macOS.
2. **Bump**: fork rebased on `a29273ad`, tag `lextudio-sync-2026-11-24`, submodule pointer moved; fix the link lists until everything builds (net10).
3. **Simplify**: Agent.Wpf / Core / Driver / Inspector / Broker as above, one commit per project.
4. **Fix**: macOS screenshot; NativeAOT contexts + smoke test.
5. **Docs and release**: `plan.md`, `howto-wpf.md`; the publish workflow (`publish-github-packages.yml`) runs on a push of `master` that touches `src/DevFlow/**` and publishes to the lextudio GitHub Packages feed only. Each run publishes a new prerelease version `<next patch after the newest stable Agent.Core on nuget.org>-<label>.<run number>` (label = the `prerelease` input, default `preview`; e.g. `0.3.2-preview.57`). A nuget.org release is a separate, explicit step (`dist.publish2nugetdotorg.bat`).

## Acceptance

- `dotnet build` of `labs.slnx` and every DevFlow test suite that ran before still pass (same count, no new skips).
- macOS: a real screenshot of the LibreWPF test app is non-blank.
- A NativeAOT-published smoke host answers `status` and `ui/tree`.
- Our own source under `src/DevFlow` is smaller than before the sync (report the line counts before/after).
- Nothing is pushed until the reviewer approved each tree and the user has seen the summary.

## Phase 2 result and known gaps (macOS arm64, 2026-11-24)

- `dotnet build src/labs.slnx -c Debug -p:EnableWindowsTargeting=true`: 0 errors. Without that property six Windows-only projects fail with NETSDK1100 (unchanged).
- Core.Tests 22/22 (21 before the plumbing test), MewUI.Tests 8/8.
- Uno.Tests 22/23: one global-`cliclick` input test fails per run and the failing test changed between two runs (environment dependent). **No pre-bump baseline was taken**, so
  it is not proven that it also failed before the bump.
- Jalium.Tests: 5 skipped (Windows only). Wpf.Tests, LibreWpf.Tests, WinForms.Tests: compile, but **cannot run on macOS** (no `Microsoft.WindowsDesktop.App`): unverified until a Windows run.

## Phase 4a result: macOS screenshot (2026-11-24)

- Reproduced on the real OpenDevelop host (`OD_TEST_MODE=1`, port 9299): `/api/v1/ui/screenshot` failed with `DllNotFoundException: wpfgfx_cor3.dll`. Two roadmap assumptions were wrong:
  upstream's `AppKitScreenshotCapture` (Abstractions) is only a ScreenCaptureKit-vs-composite *policy* helper (22 lines) and has no capture of its own, and the
  `ProGpuWpfScreenshot.TryCapturePng` that `TryCaptureViaProGpu` reflects on does not exist in any openavalon source (only a test names it), so it always returned null.
- Fix: `Agent.Wpf/MacWindowCapture.cs` (compiled only under `LIBREWPF`) captures the process's own window with CoreGraphics + ImageIO. `CapturePrimaryWindowScreenshot` uses it
  on macOS and returns null (HTTP 500 "capture failed", not an exception) on other non-Windows hosts.
- Verified: with the rebuilt `LeXtudio.DevFlow.Agent.LibreWpf.dll` in the app bundle, the screenshot is a 2048x1536 non-blank PNG of the OpenDevelop window. Element/selector screenshots on macOS are cropped from the window capture (`CGImageCreateWithImageInRect`, with the native title-bar offset); checked on the OpenDevelop start page "開啟方案" button (146x36 px, correct). Not yet done: an automated
  test (needs a host with a window, so it belongs in the OpenDevelop integration suite and must skip on a locked session), and the ProGPU agent still reports `screenshots = false`.

## Phase 3 findings and macOS baseline (2026-11-24)

macOS is the verification platform; other platforms are re-verified later. Baseline on the real OpenDevelop host (macOS arm64, rebuilt LibreWpf agent), all HTTP 200 with
sensible bodies: `agent/status`, `ui/tree`, `ui/elements`, `ui/query-selector`, `ui/hit-test`, `invoke/actions`, `ui/input-source`, `alert/detect`, `device/app/theme`,
`network/requests`, `webview/contexts`, `ui/screenshot` (window; `selector=` returned 200 only because it fell back to the window), `actions/{click,move,press,release,drag,key,tap}` (cliclick/semantic). Scroll on a non-scrollable
button correctly reports failure. Use this list as the regression check after each simplification.

Simplification, measured, no code deleted yet:
- **Agent.Wpf cannot be replaced by upstream `Agent.WPF`.** Upstream's derives `MauiDevFlowAgentService` and needs `Microsoft.Maui.Controls` + `platforms/Windows.WPF`
  (MAUI-on-WPF); ours derives the plain native `DevFlowAgentServiceBase`. Nothing is "identical": the decision rule keeps all of it.
- **Core**: our base already derives upstream's `DevFlowAgentService` (so the 39 links are real reuse). The remaining overlap is 19 route names that we re-register with our own
  handlers (`private new RegisterRoutes`). They are not pure duplicates: wire shapes differ (e.g. `network/requests` is `{requests,total}` with `count=` here, a bare array with
  `limit=` upstream), and Driver/Mcp/tests depend on ours. Collapsing them is a wire-contract change, not a deletion. Do it only per route, with the Driver/Mcp change in the same commit and a macOS check.
- Not yet looked at: Driver (12 links), Inspector (15), Broker (7); these are small (340/198/84 own lines).

### Driver / Inspector / Broker (measured)

- Own code is small: Driver 340 lines (`AgentClient` 117, `DevFlowMutation` 189, `AgentStatus` 27), Inspector 198 (two shims), Broker 84 (two shims). The upstream files they link all still exist and build.
- The Driver has *two* `AgentClient` classes: ours (`LeXtudio.DevFlow.Driver`, 9 methods) and the linked upstream one (`Microsoft.Maui.DevFlow.Driver`, 117 methods). Dropping ours means
  migrating the call sites in six `src/Cli/**/CommandHandlers.cs` and six test projects (`AgentClient`, `DevFlowMutation.Post/Delete`, `AgentStatus`). Saves roughly 140 lines.
  Of these, only the LibreWpf and Uno CLIs/tests can be checked on macOS; the other four need Windows. Left alone until a Windows run is available.
- The Inspector/Broker shims adapt upstream CLI types to our assemblies; they are not duplicates and have nothing to delete.
- Net result of this sync on size: `src/DevFlow` own code *grew* (macOS screenshot +185 lines in `Agent.Wpf`); the acceptance line "smaller than before" is not met and is not reachable
  without the wire-contract changes listed under Core above.

## Phase 4b result: NativeAOT smoke (macOS arm64, 2026-11-24)

- New `src/DevFlow/LeXtudio.DevFlow.Agent.Core.AotSmoke`: a stub agent over `DevFlowAgentServiceBase`, `JsonSerializerIsReflectionEnabledByDefault=false`, `PublishAot` with `-p:AotSmoke=true`
  (`dotnet publish -c Release -p:AotSmoke=true`). The published arm64 Mach-O answers `agent/status` and `ui/tree` (nested children) and exits 0.
- Needed in Core: `DevFlowJson` now serializes through upstream `AgentJson.Serialize(data, Options)` (keeps depth 256 / cycle ignoring, adds generated metadata); the default capabilities and the
  status body are string-keyed dictionaries instead of anonymous objects. Reflection-enabled hosts behave as before (Core.Tests 22/22; live OpenDevelop status/tree/screenshot 200).
- Follow-up (same day): the other anonymous responses in `DevFlowAgentServiceBase` (alerts, query-selector, hit-test, assert, network, webview contexts, batch, invoke list) are dictionaries too, and the private
  request bodies are covered by a nested source-generated `RequestJsonContext` registered with `AgentJson.RegisterContext` (so `BodyAs<T>` and the batch endpoint work with reflection off). The smoke now
  also covers `ui/query-selector`, `ui/hit-test`, `network/requests`, `webview/contexts`, `invoke/actions`, `ui/assert` and `ui/actions/batch` (POSTs need `RequireMutationLease = false`); the published
  NativeAOT binary passes all nine. Core's JSON publish warnings are gone.
- Still reflection by design (publish warnings remain, same as upstream): `[DevFlowAction]` discovery and invocation (`Assembly.GetTypes`, `GetMethod`) and `Exception.TargetSite` in `DescribeException`;
  `invoke/actions/{name}` responses carry an arbitrary return value. The WPF/LibreWPF agents (anonymous capabilities, visual-tree walker property reflection) are not published as AOT here;
  a LibreWPF AOT host (openavalon `XsgNativeAot`) is the next target.

## Phase 4c: macOS screenshot tests (2026-11-24)

- The crop maths moved to `Agent.Wpf/CropGeometry.cs` (pure, no platform types) and is linked into `Agent.Core.Tests`, where `CropGeometryTests` (13 cases) runs on macOS: the measured OpenDevelop
  button (2x, 28 pt title bar) lands on (624, 748) 146x36 px, clamping at the image edge, empty/outside regions, unknown window size. Core.Tests is now 35/35. A live run after the refactor
  returned a crop byte-identical to the one checked by eye before it.
- Still no automated end-to-end capture test: it needs a host with an on-screen window, so it belongs in the OpenDevelop integration suite (skip on a locked session), a separate repository.

## Phase 4d: LibreWPF agent under NativeAOT, measured (2026-11-24)

- `dotnet build src/DevFlow/LeXtudio.DevFlow.Agent.LibreWpf -p:IsAotCompatible=true` (the AOT/trim analyzers, runnable on macOS) reports **41** warnings outside upstream's files: 28 in `WpfAgentService.cs`,
  8 in `WpfMenuPopupDiagnostics.cs`, 5 in Core (`[DevFlowAction]` discovery/invoke, `TargetSite`, the ones listed under Phase 4b).
- Every WPF-side warning is reflection by *name* to members the agent cannot reference at compile time: `Assembly.GetType("...")`/`GetMethod`/`GetProperty` into WPF internals and ProGPU
  (`ProGpuWpfScreenshot` lookup, popup/menu internals, WebView2 types, `TypeDescriptor.GetConverter` for property values), plus one `JsonSerializer.Serialize` of an anonymous payload in
  `WpfMenuPopupDiagnostics`. They are not mechanical: each needs either a direct typed reference (impossible for internal members), a `DynamicDependency`/rd.xml root in the host, or removal of the feature.
- The only existing LibreWPF NativeAOT host is openavalon's `samples/XsgNativeAot` (a prototype with a 2 GB private package tree and patched LibreWPF assemblies; its repo has uncommitted changes). Putting
  the agent into it is not verifiable from this repository, so **no code was changed for it**. Order of work when it is wanted: (1) replace the anonymous `GetCapabilities` and the menu-diagnostics payload,
  (2) root the name-based lookups in the host's `rd.xml` and add a `status`/`ui/tree` check to that sample's `check` run, (3) only then fix individual warnings.
