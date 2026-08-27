using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace LeXtudio.DevFlow.Agent.Core;

public static class CliclickInput
{
    private static readonly Lazy<string?> _path = new(ResolvePath);
    private static readonly object _dragLock = new();
    private static Process? _dragHoldProcess;
    private static readonly object _keyLock = new();
    private static Process? _keyHoldProcess;

    public static bool IsAvailable => _path.Value != null;

    private static string? ResolvePath()
    {
        if (!OperatingSystem.IsMacOS())
            return null;

        var bundled = Path.Combine(AppContext.BaseDirectory, "CliclickSharp");
        if (File.Exists(bundled))
            return bundled;

        foreach (var candidate in new[] { "/opt/homebrew/bin/cliclick", "/usr/local/bin/cliclick" })
        {
            if (File.Exists(candidate))
                return candidate;
        }

        var env = Environment.GetEnvironmentVariable("PATH");
        if (env != null)
        {
            foreach (var dir in env.Split(':'))
            {
                try
                {
                    var p = Path.Combine(dir, "cliclick");
                    if (File.Exists(p))
                        return p;
                }
                catch { }
            }
        }

        return null;
    }

    private static string Pt(double v) => ((int)Math.Round(v)).ToString(CultureInfo.InvariantCulture);

    public static bool TryMove(double x, double y) => Run($"m:{Pt(x)},{Pt(y)}");

    /// <summary>
    /// Which mouse button a decomposed press/drag-move/release gesture uses. Right and middle rely on
    /// commands the bundled CliclickSharp adds - the original cliclick can only ever left-drag and has
    /// no middle button - so a system cliclick found on PATH supports <see cref="Left"/> only.
    /// </summary>
    public enum MouseButton
    {
        Left,
        Right,
        Middle,
    }

    /// <summary>Maps a button name, defaulting to left when absent. False for an unrecognised name.</summary>
    public static bool TryParseButton(string? name, out MouseButton button)
    {
        switch (name?.Trim().ToLowerInvariant())
        {
            case null or "" or "left":
                button = MouseButton.Left;
                return true;
            case "right":
                button = MouseButton.Right;
                return true;
            case "middle" or "center" or "centre":
                button = MouseButton.Middle;
                return true;
            default:
                button = MouseButton.Left;
                return false;
        }
    }

    // Left uses the original dd/dm/du; the others use the CliclickSharp extensions
    // (see external/cliclick-sharp/README.md).
    private static string DownCmd(MouseButton b)
        => b switch { MouseButton.Right => "rdd", MouseButton.Middle => "mdd", _ => "dd" };

    private static string MoveCmd(MouseButton b)
        => b switch { MouseButton.Right => "rdm", MouseButton.Middle => "mdm", _ => "dm" };

    private static string UpCmd(MouseButton b)
        => b switch { MouseButton.Right => "rdu", MouseButton.Middle => "mdu", _ => "du" };

    public static bool TryPressDown(double x, double y) => TryPressDown(x, y, MouseButton.Left);

    public static bool TryPressDown(double x, double y, MouseButton button)
    {
        lock (_dragLock)
        {
            StopDragHoldProcess();
            var exe = _path.Value;
            if (exe == null)
                return false;

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = exe,
                    UseShellExecute = false,
                };
                psi.ArgumentList.Add($"m:{Pt(x)},{Pt(y)}");
                psi.ArgumentList.Add($"{DownCmd(button)}:{Pt(x)},{Pt(y)}");
                psi.ArgumentList.Add("w:600000");
                // Safety release if this holder process outlives the caller, so a stuck button cannot
                // be left down system-wide.
                psi.ArgumentList.Add($"{UpCmd(button)}:.");
                _dragHoldProcess = Process.Start(psi);
                if (_dragHoldProcess == null)
                    return false;

                System.Threading.Thread.Sleep(100);
                return !_dragHoldProcess.HasExited;
            }
            catch
            {
                StopDragHoldProcess();
                return false;
            }
        }
    }

    public static bool TryDragMoveTo(double x, double y) => TryDragMoveTo(x, y, MouseButton.Left);

    public static bool TryDragMoveTo(double x, double y, MouseButton button)
    {
        lock (_dragLock)
        {
            if (_dragHoldProcess == null || _dragHoldProcess.HasExited)
                return false;

            // Left keeps using a plain move, which is what the existing verified drag behaviour relies
            // on. The other buttons must post a *dragged* event carrying that button, or the target
            // sees a hover move with no button state.
            return button == MouseButton.Left
                ? Run($"m:{Pt(x)},{Pt(y)}")
                : Run($"{MoveCmd(button)}:{Pt(x)},{Pt(y)}");
        }
    }

    public static bool TryRelease(double x, double y) => TryRelease(x, y, MouseButton.Left);

    public static bool TryRelease(double x, double y, MouseButton button)
    {
        lock (_dragLock)
        {
            if (_dragHoldProcess == null || _dragHoldProcess.HasExited)
                return false;

            var released = Run($"{UpCmd(button)}:{Pt(x)},{Pt(y)}");
            StopDragHoldProcess();
            return released;
        }
    }

    private static void StopDragHoldProcess()
    {
        if (_dragHoldProcess == null)
            return;

        try
        {
            if (!_dragHoldProcess.HasExited)
                _dragHoldProcess.Kill();
        }
        catch { }
        _dragHoldProcess.Dispose();
        _dragHoldProcess = null;
    }

    public static bool TryClick(double x, double y, int clickCount)
    {
        var commands = new List<string> { $"m:{Pt(x)},{Pt(y)}" };
        for (var i = 0; i < Math.Max(1, clickCount); i++)
            commands.Add($"c:{Pt(x)},{Pt(y)}");
        return Run(commands.ToArray());
    }

    // Uno's Skia-macOS input backend DROPS a mouse-up that arrives too soon after the preceding
    // down/drag events (they get coalesced), so a drag posted as one rapid m/dd/dm.../du batch
    // delivers PointerPressed + PointerMoved but NO PointerReleased — the "release never arrived"
    // failure the DataGrid column-reorder drag hit. In-batch `w:` waits helped but were still
    // intermittent. What is reliable (verified) is posting the RELEASE as a SEPARATE, time-
    // separated cliclick invocation: the button-down state persists globally on the HID tap
    // between processes, and the well-isolated up is consistently seen as a distinct PointerReleased.
    private const int DragHoldAfterDownMs = 120;
    private const int DragSettleBeforeUpMs = 180;

    public static bool TryDrag(double fromX, double fromY, double toX, double toY, int steps)
    {
        if (steps < 1)
            steps = 1;

        // Phase 1 (one process): move → press → hold → drag to target. Button stays down after.
        var press = new List<string>
        {
            $"m:{Pt(fromX)},{Pt(fromY)}",
            $"dd:{Pt(fromX)},{Pt(fromY)}",
            $"w:{DragHoldAfterDownMs}",
        };
        for (var i = 1; i <= steps; i++)
        {
            var t = (double)i / steps;
            var x = fromX + (toX - fromX) * t;
            var y = fromY + (toY - fromY) * t;
            press.Add($"dm:{Pt(x)},{Pt(y)}");
        }
        if (!Run(press.ToArray()))
            return false;

        // Phase 2 (separate process, after a real settle): release. Kept out of the phase-1 batch
        // so the up is delivered as an isolated event Uno reliably turns into PointerReleased.
        System.Threading.Thread.Sleep(DragSettleBeforeUpMs);
        return Run($"du:{Pt(toX)},{Pt(toY)}");
    }

    /// <summary>
    /// Presses AND HOLDS a key (letter/digit/modifier/named special key - anything
    /// KeyBaseAction.SupportedKeycodes or ModifierKeycodes in the bundled CliclickSharp knows),
    /// mirroring TryPressDown's mouse-button-hold trick: the CGEvent-level key-down state persists
    /// system-wide once posted, independent of the process that posted it, so a long-lived "holder"
    /// process is only a safety net (auto-release after 10 minutes if TryKeyUp is never called) -
    /// not the actual holding mechanism.
    /// </summary>
    public static bool TryKeyDown(string key)
    {
        lock (_keyLock)
        {
            StopKeyHoldProcess();
            var exe = _path.Value;
            if (exe == null || string.IsNullOrWhiteSpace(key))
                return false;

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = exe,
                    UseShellExecute = false,
                };
                psi.ArgumentList.Add($"kd:{key}");
                psi.ArgumentList.Add("w:600000");
                // Safety release if this holder process outlives the caller, so a stuck key cannot
                // be left down system-wide.
                psi.ArgumentList.Add($"ku:{key}");
                _keyHoldProcess = Process.Start(psi);
                if (_keyHoldProcess == null)
                    return false;

                System.Threading.Thread.Sleep(50);
                return !_keyHoldProcess.HasExited;
            }
            catch
            {
                StopKeyHoldProcess();
                return false;
            }
        }
    }

    /// <summary>Releases a key previously held via <see cref="TryKeyDown"/>.</summary>
    public static bool TryKeyUp(string key)
    {
        lock (_keyLock)
        {
            if (_keyHoldProcess == null || _keyHoldProcess.HasExited)
                return false;

            var released = Run($"ku:{key}");
            StopKeyHoldProcess();
            return released;
        }
    }

    private static void StopKeyHoldProcess()
    {
        if (_keyHoldProcess == null)
            return;

        try
        {
            if (!_keyHoldProcess.HasExited)
                _keyHoldProcess.Kill();
        }
        catch { }
        _keyHoldProcess.Dispose();
        _keyHoldProcess = null;
    }

    /// <summary>Presses and releases a key in one call - for a single keystroke, not a held key.</summary>
    public static bool TryKeyPress(string key) => Run($"kp:{key}");

    private static bool Run(params string[] arguments)
    {
        var exe = _path.Value;
        if (exe == null)
            return false;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (var arg in arguments)
                psi.ArgumentList.Add(arg);

            using var process = Process.Start(psi);
            if (process == null)
                return false;

            // Drain stdout/stderr so the child never blocks on a full pipe.
            _ = process.StandardOutput.ReadToEnd();
            _ = process.StandardError.ReadToEnd();
            if (!process.WaitForExit(10_000))
            {
                try { process.Kill(); } catch { }
                return false;
            }

            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
