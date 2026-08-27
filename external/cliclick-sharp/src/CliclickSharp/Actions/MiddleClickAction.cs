using CliclickSharp.Native;

namespace CliclickSharp.Actions;

public class MiddleClickAction : MouseBaseAction, IAction
{
    public static string CommandShortcut => "mc";
    public static string CommandDescription => "Middle-click";

    protected override CGMouseButton GetMouseButton() => CGMouseButton.kCGMouseButtonCenter;

    protected override void PerformActionAtPoint(CGPoint point)
    {
        PostMouseEvent(CGEventType.kCGEventOtherMouseDown, point);
        Thread.Sleep(15);
        PostMouseEvent(CGEventType.kCGEventOtherMouseUp, point);
    }
}
