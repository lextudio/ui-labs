# macOS key injection: post to the process, not the session tap

`/api/v1/ui/actions/keydown` and `/keyup` press and release a real OS-level key, so a modifier can be
held across pointer actions or several frames. This note records why the macOS implementation posts
to the app's own process instead of using the generic cliclick path.

## The failure

CliclickSharp posts keyboard events with `CGEventPost(kCGSessionEventTap, …)`. That route delivers
the event to the **frontmost application**, not to a chosen target. Under automation the target
window is activated by the test driver, and the helper process takes roughly 200–250 ms to start and
post (process startup plus its 65 ms first-action delay). When the foreground or key-window state
changes in that window the event is delivered elsewhere, which produced:

- a press that sometimes arrived and sometimes did not, from the identical command;
- a delivered key-down whose key-up was lost, leaving the key logically down - later presses of the
  same key were then treated as auto-repeats and never reached the app, so a second `Tab` looked
  like "nothing happened" while a different key (`Delete`) still arrived;
- no visible focus loss: the canvas kept WPF focus, the app stayed the active
  `NSRunningApplication`, and `NSApp.keyWindow` was the main window when checked.

In short: the tool is not flaky; the delivery route depends on global frontmost state that
automation cannot keep stable.

## The fix

`MacOSKeyInput` posts **ordinary** keys (Tab, letters, Delete, …) with
`CGEventPostToPid(Environment.ProcessId, …)`. The event goes to this application's key window
regardless of which application is frontmost, needs no extra Accessibility permission, and works
while the window is activated by automation.

**Modifiers** (ctrl/cmd/alt/shift/fn) keep the session-tap route (`CGEventPost`): native mouse
events created by CliclickSharp read their flags from the session's global modifier state, so a
held Ctrl only reaches a Ctrl-click if that state has it down. The app is frontmost while a
modifier is held for a pointer gesture, so it sees the key there too. Posting a modifier to both
places is not an option - the session post suppresses the process one and `Keyboard.Modifiers`
stays empty.

A comma-separated key is a chord: `"shift,tab"` posts shift down (session tap, for its flags), waits
20 ms, then tab down (process), and the release posts in the same order. The wait lets the app
observe the modifier when the second key's `KeyDown` arrives.

`CliclickInput.TryKeyDown`/`TryKeyUp` keep a set of held keys and a 10-minute safety timer that
releases a key whose `keyup` never arrived, so a failed run cannot leave the machine's keyboard
stuck. The holder process is gone on macOS; other platforms keep the CliclickSharp path (their
key-down state persists until the matching key-up, and the holder is only the safety release).

## Verification

`OpenDevelop/tests/OpenDevelop.IntegrationTests/GtkDesignerNavigationTests` presses Tab, Shift+Tab
and Tab again against the GTK designer canvas. All three arrive, the primary selection walks the
snapshot order forward, back and forward again, and the document/undo state is unchanged. The same
journey failed with "only the first synthetic key arrives" before this change.

`WpfStripEditingTests.CtrlPointerGestures_DistinguishClickCollapseFromGroupDrag` still holds Ctrl
across a native press and release. It now waits for `od.pointer-events` to record the press before
releasing Ctrl: the in-process key-up no longer has the helper process's latency and could
otherwise overtake the queued press, while the canvas snapshots modifiers when the press is
processed.
