using CliclickSharp.Native;

namespace CliclickSharp.Actions;

public class RightDragDownAction : MouseBaseAction, IAction
{
    public static string CommandShortcut => "rdd";
    public static string CommandDescription => "Press right mouse button (begin right-drag)";

    protected override CGMouseButton GetMouseButton() => CGMouseButton.kCGMouseButtonRight;

    protected override void PerformActionAtPoint(CGPoint point)
        => PostMouseEvent(CGEventType.kCGEventRightMouseDown, point);
}
