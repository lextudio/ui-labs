using System.Text.RegularExpressions;
using CliclickSharp.Native;

namespace CliclickSharp.Actions;

public enum CoordinateAxis
{
    XAxis,
    YAxis,
}

public abstract partial class MouseBaseAction
{
    protected virtual CGEventType GetMoveEventConstant() => CGEventType.kCGEventMouseMoved;

    public virtual bool PerformAction(string data, ExecutionOptions options)
    {
        double toX, toY;
        bool hasValidCoordinates = GetCoordinate(data, out toX, out toY);

        if (!hasValidCoordinates)
        {
            return false;
        }

        CGPoint currentPos = CoreGraphics.CGEventGetLocation(IntPtr.Zero);

        if (!string.Equals(data, ".", StringComparison.Ordinal) && !string.IsNullOrEmpty(data))
        {
            PostHumanizedMouseEvents(currentPos, toX, toY, options.Easing);
        }
        else
        {
            toX = currentPos.X;
            toY = currentPos.Y;
        }

        PerformActionAtPoint(new CGPoint(toX, toY));
        return true;
    }

    protected abstract void PerformActionAtPoint(CGPoint point);

    private void PostHumanizedMouseEvents(CGPoint fromPoint, double toX, double toY, uint easing)
    {
        double deltaX = toX - fromPoint.X;
        double deltaY = toY - fromPoint.Y;
        double distance = Math.Sqrt(deltaX * deltaX + deltaY * deltaY);

        int steps = Math.Max(1, (int)(distance * easing / 100.0 + 1));

        for (int i = 1; i <= steps; i++)
        {
            double t = (double)i / steps;
            double easedT = EaseInOutCubic(t);
            double x = fromPoint.X + deltaX * easedT;
            double y = fromPoint.Y + deltaY * easedT;

            IntPtr moveEvent = CoreGraphics.CGEventCreateMouseEvent(
                IntPtr.Zero,
                GetMoveEventConstant(),
                new CGPoint(x, y),
                CGMouseButton.kCGMouseButtonLeft);
            CoreGraphics.CGEventPost(CGEventTapLocation.kCGHIDEventTap, moveEvent);
            CoreGraphics.CFRelease(moveEvent);

            Thread.Sleep(TimeSpan.FromTicks(2200));
        }
    }

    private static double EaseInOutCubic(double t)
    {
        if (t < 0.5)
            return 4 * t * t * t;
        return 0.5 * Math.Pow(2 * t - 2, 3) + 1;
    }

    public static bool GetCoordinate(string data, out double toX, out double toY)
    {
        toX = 0;
        toY = 0;

        if (string.Equals(data, ".", StringComparison.Ordinal) || string.IsNullOrEmpty(data))
        {
            return true;
        }

        string[] parts = data.Split(',');
        if (parts.Length != 2)
        {
            Console.Error.WriteLine("Invalid coordinate format. Use x,y");
            return false;
        }

        double? x = ParseAxis(parts[0], CoordinateAxis.XAxis);
        double? y = ParseAxis(parts[1], CoordinateAxis.YAxis);

        if (x == null || y == null)
            return false;

        toX = x.Value;
        toY = y.Value;
        return true;
    }

    private static double? ParseAxis(string value, CoordinateAxis axis)
    {
        string trimmed = value.Trim();

        if (!Regex.IsMatch(trimmed, @"^=?[+-]?\d+$"))
        {
            Console.Error.WriteLine($"Invalid coordinate value: {trimmed}");
            return null;
        }

        bool forceAbsolute = trimmed.StartsWith('=');
        if (forceAbsolute)
            trimmed = trimmed[1..];

        bool isRelative = trimmed.StartsWith('+') || trimmed.StartsWith('-');
        double relativeOffset = 0;

        if (isRelative && !forceAbsolute)
        {
            relativeOffset = double.Parse(trimmed);
        }
        else
        {
            double absolute = double.Parse(trimmed);

            if (!forceAbsolute)
            {
                CGPoint currentPos = CoreGraphics.CGEventGetLocation(IntPtr.Zero);
                relativeOffset = axis == CoordinateAxis.XAxis
                    ? absolute - currentPos.X
                    : absolute - currentPos.Y;
            }
            else
            {
                relativeOffset = absolute;
            }
        }

        return relativeOffset + (axis == CoordinateAxis.XAxis
            ? CoreGraphics.CGEventGetLocation(IntPtr.Zero).X
            : CoreGraphics.CGEventGetLocation(IntPtr.Zero).Y);
    }

    [GeneratedRegex(@"^=?[+-]?\d+$")]
    private static partial Regex CoordinateRegex();
}
