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

    public static bool TryPressDown(double x, double y)
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
                psi.ArgumentList.Add($"dd:{Pt(x)},{Pt(y)}");
                psi.ArgumentList.Add("w:600000");
                psi.ArgumentList.Add("du:.");
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

    public static bool TryDragMoveTo(double x, double y)
    {
        lock (_dragLock)
        {
            if (_dragHoldProcess == null || _dragHoldProcess.HasExited)
                return false;

            return Run($"m:{Pt(x)},{Pt(y)}");
        }
    }

    public static bool TryRelease(double x, double y)
    {
        lock (_dragLock)
        {
            if (_dragHoldProcess == null || _dragHoldProcess.HasExited)
                return false;

            var released = Run($"du:{Pt(x)},{Pt(y)}");
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
