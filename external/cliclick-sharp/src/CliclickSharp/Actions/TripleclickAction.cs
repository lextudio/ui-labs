using CliclickSharp.Native;

namespace CliclickSharp.Actions;

public class TripleclickAction : MouseBaseAction, IAction
{
    public static string CommandShortcut => "tc";
    public static string CommandDescription => "Triple-click";

    protected override void PerformActionAtPoint(CGPoint point)
    {
        for (int i = 0; i < 3; i++)
        {
            IntPtr downEvent = CoreGraphics.CGEventCreateMouseEvent(
                IntPtr.Zero,
                CGEventType.kCGEventLeftMouseDown,
                point,
                CGMouseButton.kCGMouseButtonLeft);
            CoreGraphics.CGEventSetIntegerValueField(downEvent, CGEventField.kCGMouseEventClickState, 3);
            CoreGraphics.CGEventPost(CGEventTapLocation.kCGHIDEventTap, downEvent);
            CoreGraphics.CFRelease(downEvent);

            IntPtr upEvent = CoreGraphics.CGEventCreateMouseEvent(
                IntPtr.Zero,
                CGEventType.kCGEventLeftMouseUp,
                point,
                CGMouseButton.kCGMouseButtonLeft);
            CoreGraphics.CGEventSetIntegerValueField(upEvent, CGEventField.kCGMouseEventClickState, 3);
            CoreGraphics.CGEventPost(CGEventTapLocation.kCGHIDEventTap, upEvent);
            CoreGraphics.CFRelease(upEvent);

            if (i < 2)
                Thread.Sleep(200);
        }
    }
}
