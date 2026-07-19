using CliclickSharp.Native;

namespace CliclickSharp.Actions;

public class RightClickAction : MouseBaseAction, IAction
{
    public static string CommandShortcut => "rc";
    public static string CommandDescription => "Right-click";

    protected override void PerformActionAtPoint(CGPoint point)
    {
        IntPtr downEvent = CoreGraphics.CGEventCreateMouseEvent(
            IntPtr.Zero,
            CGEventType.kCGEventRightMouseDown,
            point,
            CGMouseButton.kCGMouseButtonRight);
        CoreGraphics.CGEventPost(CGEventTapLocation.kCGHIDEventTap, downEvent);
        CoreGraphics.CFRelease(downEvent);

        Thread.Sleep(15);

        IntPtr upEvent = CoreGraphics.CGEventCreateMouseEvent(
            IntPtr.Zero,
            CGEventType.kCGEventRightMouseUp,
            point,
            CGMouseButton.kCGMouseButtonRight);
        CoreGraphics.CGEventPost(CGEventTapLocation.kCGHIDEventTap, upEvent);
        CoreGraphics.CFRelease(upEvent);
    }
}
