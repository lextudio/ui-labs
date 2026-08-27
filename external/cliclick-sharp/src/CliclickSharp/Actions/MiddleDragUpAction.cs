using CliclickSharp.Native;

namespace CliclickSharp.Actions;

public class MiddleDragUpAction : MouseBaseAction, IAction
{
    public static string CommandShortcut => "mdu";
    public static string CommandDescription => "Release middle mouse button (end middle-drag)";

    protected override CGMouseButton GetMouseButton() => CGMouseButton.kCGMouseButtonCenter;

    protected override CGEventType GetMoveEventConstant() => CGEventType.kCGEventOtherMouseDragged;

    protected override void PerformActionAtPoint(CGPoint point)
        => PostMouseEvent(CGEventType.kCGEventOtherMouseUp, point);
}
