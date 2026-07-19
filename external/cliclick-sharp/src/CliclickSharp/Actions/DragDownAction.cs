using CliclickSharp.Native;

namespace CliclickSharp.Actions;

public class DragDownAction : MouseBaseAction, IAction
{
    public static string CommandShortcut => "dd";
    public static string CommandDescription => "Press mouse button (begin drag)";

    protected override void PerformActionAtPoint(CGPoint point)
    {
        IntPtr downEvent = CoreGraphics.CGEventCreateMouseEvent(
            IntPtr.Zero,
            CGEventType.kCGEventLeftMouseDown,
            point,
            CGMouseButton.kCGMouseButtonLeft);
        CoreGraphics.CGEventPost(CGEventTapLocation.kCGHIDEventTap, downEvent);
        CoreGraphics.CFRelease(downEvent);
    }
}
