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
5. **Docs and release**: `plan.md`, `howto-wpf.md`; the publish workflow (`publish-github-packages.yml`) runs on a push of `master` that touches `src/DevFlow/**`; the version is forced to the newest stable `LeXtudio.DevFlow.Agent.Core` on nuget.org, so a *new* number needs a nuget.org release first (`dist.publish2nugetdotorg.bat`), which is a separate, explicit step.

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
