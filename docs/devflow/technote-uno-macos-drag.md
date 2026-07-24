# Technical Note: Synthesized Drag on Uno-Skia-macOS

> Audience: DevFlow maintainers. This documents why injecting a real OS-level
> drag/drop gesture into an Uno Platform app on Skia-macOS is subtle, and the four
> independent fixes required to make `POST /api/v1/ui/actions/drag` deliver a full
> `PointerPressed → PointerMoved → PointerReleased` sequence to a target element.

## Background

On macOS, DevFlow drives real pointer gestures by posting CoreGraphics (`CGEvent`)
mouse events, executed through the bundled **CliclickSharp** helper (a Native-AOT
build of a C#/`cliclick`-compatible tool shipped in
`LeXtudio.DevFlow.Agent.Core`'s `build/` folder). This exercises the same OS input
path a human uses, so gestures that poll global cursor/button state — not just XAML
routed events — are reproduced faithfully.

Getting a *click* to land was easy. Getting a *drag whose release actually commits*
required fixing four unrelated problems. Each one, alone, silently degrades the drag
to "press + moves but no release," which reads as "the drop never happened."

## The four fixes

### 1. CliclickSharp was 100% broken under Native AOT

`PublishAot=true` breaks two reflection patterns the action dispatcher relied on:

- `Activator.CreateInstance(actionType)` — the parameterless constructor is not
  rooted, so it throws `MissingMethodException` on the very first command.
- Reading a `static abstract` interface member (`CommandShortcut`) via
  `Type.GetProperty(...).GetValue(null)` returns `null`, leaving the command table
  empty ("Unknown command").

**Fix** (`external/cliclick-sharp/.../ActionExecutor.cs`): reflection-free
registration — a generic `Register<T>() where T : IAction` accesses the
static-abstract members directly (`T.CommandShortcut`) and stores a compiled `new`
factory delegate instead of activating by `Type`.

Also, `CGEventGetLocation(IntPtr.Zero)` always returns `(0,0)` — a null event has no
location. Matching the original `cliclick`, create a throwaway event first
(`CGEventCreate(NULL)` → `GetCurrentMouseLocation()`). Without this, absolute-coordinate
drags happen to still work (the two errors cancel) but cursor reads and relative
coordinates are wrong.

### 2. Content origin: measure it natively, flip against the primary screen

Global drag coordinates are `contentOrigin + elementWindowLocalPoint`, where the
element-local point comes from `TransformToVisual(null)`.

- Do **not** use `AppWindow.Position` for `contentOrigin` — on Skia-macOS it is the
  outer window-frame origin and drifts from the content area by roughly a title-bar
  height. Measure the real NSWindow content origin instead (`MacOSWindowOrigin`:
  ObjC `contentView.bounds` + `convertRectToScreen:`).
- The Cocoa (bottom-left origin) → Quartz (top-left origin) Y-flip must use the
  **primary** display height — `NSScreen.screens[0]` (the menu-bar screen whose frame
  origin is `(0,0)`), **not** `NSScreen.mainScreen`. `mainScreen` is the *focus*
  screen, which on a multi-monitor setup is frequently a different display; using its
  height shifts the computed origin by the height difference and the drag lands off
  the window.

### 3. Focus the window before the real drag

A background window's *first* click is consumed by macOS to activate the window and
is not delivered to content. Click the title bar once (a zero-length drag works) to
make the window key before issuing the gesture, otherwise `PointerPressed` is missed.

### 4. Deliver the release as a separate, time-separated event

This is the one that looks like a framework bug. Uno's Skia-macOS input backend
**drops a mouse-up that arrives too soon after the preceding drag events** — they get
coalesced. A drag posted as one rapid `m / dd / dm… / du` batch delivers press and
moves but no `PointerReleased`.

In-batch `w:` (wait) commands help but remain intermittent. What is reliable is
posting the **release (`du:`) as a separate cliclick invocation** after a real settle
delay. The left-button-down state persists globally on the HID event tap between
processes, so the well-isolated up is consistently seen as a distinct
`PointerReleased`. See `CliclickInput.TryDrag`.

## Consuming-side gotcha: capture is unreliable for synthesized pointers

If the target element takes `CapturePointer` (e.g. a drag-reorder gesture), be aware
that `PointerCaptureLost` can fire mid-drag under synthesized input, after which the
capturing element receives no further move/release. Prefer driving move/release from
an **ancestor** with `AddHandler(..., handledEventsToo: true)` — an ancestor sees every
event that bubbles through it regardless of capture state, and `handledEventsToo`
catches events a control template has already marked `Handled`. (The plain CLR
`element.PointerReleased +=` subscription skips handled events, which is itself a
common cause of "the handler never fires.")

## Verifying

`UnoDevFlowTestApp` contains a dedicated drag-probe surface and probes
(`uno.drag-surface-rect`, `uno.drag-log`, `uno.drag-log-reset`) that record the exact
`down/move/up` sequence a synthesized drag produces — the canonical way to confirm
end-to-end delivery after touching any of the above.

The regression test is `UnoAgentIntegrationTests.Drag_OverProbeSurface_DeliversPressMoveRelease`
(`src/DevFlow/LeXtudio.DevFlow.Agent.Uno.Tests`). It focuses the window, drags across
the probe surface, and asserts the log contains `down@`, `move@`, and `up@`. It is
macOS-only and **self-skips** when the window's content origin reads `(0,0)` — i.e. the
window was never placed on a real screen (a headless/background runner). It polls a few
seconds first, since a freshly-launched window is not placed instantly; run it in a
session with a display to exercise the real gesture rather than skip.
