using CliclickSharp.Native;

namespace CliclickSharp.Actions;

public class RightDragUpAction : MouseBaseAction, IAction
{
    public static string CommandShortcut => "rdu";
    public static string CommandDescription => "Release right mouse button (end right-drag)";

    protected override CGMouseButton GetMouseButton() => CGMouseButton.kCGMouseButtonRight;

    protected override CGEventType GetMoveEventConstant() => CGEventType.kCGEventRightMouseDragged;

    protected override void PerformActionAtPoint(CGPoint point)
        => PostMouseEvent(CGEventType.kCGEventRightMouseUp, point);
}
