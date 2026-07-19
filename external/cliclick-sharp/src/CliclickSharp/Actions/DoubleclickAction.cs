using CliclickSharp.Native;

namespace CliclickSharp.Actions;

public class DoubleclickAction : MouseBaseAction, IAction
{
    public static string CommandShortcut => "dc";
    public static string CommandDescription => "Double-click";

    protected override void PerformActionAtPoint(CGPoint point)
    {
        for (int i = 0; i < 2; i++)
        {
            IntPtr downEvent = CoreGraphics.CGEventCreateMouseEvent(
                IntPtr.Zero,
                CGEventType.kCGEventLeftMouseDown,
                point,
                CGMouseButton.kCGMouseButtonLeft);
            CoreGraphics.CGEventSetIntegerValueField(downEvent, CGEventField.kCGMouseEventClickState, i + 1);
            CoreGraphics.CGEventPost(CGEventTapLocation.kCGHIDEventTap, downEvent);
            CoreGraphics.CFRelease(downEvent);

            IntPtr upEvent = CoreGraphics.CGEventCreateMouseEvent(
                IntPtr.Zero,
                CGEventType.kCGEventLeftMouseUp,
                point,
                CGMouseButton.kCGMouseButtonLeft);
            CoreGraphics.CGEventSetIntegerValueField(upEvent, CGEventField.kCGMouseEventClickState, i + 1);
            CoreGraphics.CGEventPost(CGEventTapLocation.kCGHIDEventTap, upEvent);
            CoreGraphics.CFRelease(upEvent);

            if (i == 0)
                Thread.Sleep(200);
        }
    }
}
