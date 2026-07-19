using CliclickSharp.Native;

namespace CliclickSharp.Actions;

public class DragUpAction : MouseBaseAction, IAction
{
    public static string CommandShortcut => "du";
    public static string CommandDescription => "Release mouse button (end drag)";

    protected override CGEventType GetMoveEventConstant() => CGEventType.kCGEventLeftMouseDragged;

    protected override void PerformActionAtPoint(CGPoint point)
    {
        IntPtr upEvent = CoreGraphics.CGEventCreateMouseEvent(
            IntPtr.Zero,
            CGEventType.kCGEventLeftMouseUp,
            point,
            CGMouseButton.kCGMouseButtonLeft);
        CoreGraphics.CGEventPost(CGEventTapLocation.kCGHIDEventTap, upEvent);
        CoreGraphics.CFRelease(upEvent);
    }
}
