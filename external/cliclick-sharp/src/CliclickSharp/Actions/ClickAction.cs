using CliclickSharp.Native;

namespace CliclickSharp.Actions;

public class ClickAction : MouseBaseAction, IAction
{
    public static string CommandShortcut => "c";
    public static string CommandDescription => "Click";

    protected override void PerformActionAtPoint(CGPoint point)
    {
        IntPtr downEvent = CoreGraphics.CGEventCreateMouseEvent(
            IntPtr.Zero,
            CGEventType.kCGEventLeftMouseDown,
            point,
            CGMouseButton.kCGMouseButtonLeft);
        CoreGraphics.CGEventPost(CGEventTapLocation.kCGHIDEventTap, downEvent);
        CoreGraphics.CFRelease(downEvent);

        Thread.Sleep(15);

        IntPtr upEvent = CoreGraphics.CGEventCreateMouseEvent(
            IntPtr.Zero,
            CGEventType.kCGEventLeftMouseUp,
            point,
            CGMouseButton.kCGMouseButtonLeft);
        CoreGraphics.CGEventPost(CGEventTapLocation.kCGHIDEventTap, upEvent);
        CoreGraphics.CFRelease(upEvent);
    }
}
