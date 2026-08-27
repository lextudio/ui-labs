using CliclickSharp.Native;

namespace CliclickSharp.Actions;

public class MiddleDragDownAction : MouseBaseAction, IAction
{
    public static string CommandShortcut => "mdd";
    public static string CommandDescription => "Press middle mouse button (begin middle-drag)";

    protected override CGMouseButton GetMouseButton() => CGMouseButton.kCGMouseButtonCenter;

    protected override void PerformActionAtPoint(CGPoint point)
        => PostMouseEvent(CGEventType.kCGEventOtherMouseDown, point);
}
