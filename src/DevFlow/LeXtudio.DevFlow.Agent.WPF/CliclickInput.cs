using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace LeXtudio.DevFlow.Agent.WPF
{
    /// <summary>
    /// Mouse input backend that shells out to the `cliclick` CLI to post real OS-level mouse events.
    /// Unlike the portable ProcessInput reflection path (which injects synthetic events straight into
    /// WPF, bypassing the native window/input layer), cliclick moves the real cursor and generates
    /// genuine platform events - so DevFlow-driven tests exercise the same native input path a real
    /// user hits (cross-window capture, overlay windows, GLFW event routing, etc.). Used by default on
    /// macOS when cliclick is installed; falls back to the previous backends otherwise.
    /// </summary>
    internal static class CliclickInput
    {
        private static readonly Lazy<string?> _path = new(ResolvePath);

        /// <summary>Whether cliclick is available on this machine.</summary>
        public static bool IsAvailable => _path.Value != null;

        private static string? ResolvePath()
        {
            if (!OperatingSystem.IsMacOS())
                return null;

            foreach (var candidate in new[] { "/opt/homebrew/bin/cliclick", "/usr/local/bin/cliclick" })
            {
                if (File.Exists(candidate))
                    return candidate;
            }

            // Fall back to PATH lookup.
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
                    catch { /* ignore malformed PATH entries */ }
                }
            }

            return null;
        }

        // cliclick coordinates are integer screen points (top-left origin) - the same logical screen
        // space Visual.PointToScreen reports on the portable backend after the coordinate fix.
        private static string Pt(double v) => ((int)Math.Round(v)).ToString(CultureInfo.InvariantCulture);

        public static bool TryMove(double x, double y) => Run($"m:{Pt(x)},{Pt(y)}");

        public static bool TryClick(double x, double y, int clickCount)
        {
            var commands = new List<string> { $"m:{Pt(x)},{Pt(y)}" };
            for (var i = 0; i < Math.Max(1, clickCount); i++)
                commands.Add($"c:{Pt(x)},{Pt(y)}");
            return Run(commands.ToArray());
        }

        public static bool TryDrag(double fromX, double fromY, double toX, double toY, int steps)
        {
            if (steps < 1)
                steps = 1;

            var commands = new List<string>
            {
                $"m:{Pt(fromX)},{Pt(fromY)}",   // settle the cursor at the start
                $"dd:{Pt(fromX)},{Pt(fromY)}",  // press
            };
            for (var i = 1; i <= steps; i++)
            {
                var t = (double)i / steps;
                var x = fromX + (toX - fromX) * t;
                var y = fromY + (toY - fromY) * t;
                commands.Add($"dm:{Pt(x)},{Pt(y)}");   // drag-move
            }
            commands.Add($"du:{Pt(toX)},{Pt(toY)}");   // release
            return Run(commands.ToArray());
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
}
