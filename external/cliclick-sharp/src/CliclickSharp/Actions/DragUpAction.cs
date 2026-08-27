using CliclickSharp.Native;

namespace CliclickSharp.Actions;

public class DragUpAction : MouseBaseAction, IAction
{
    public static string CommandShortcut => "du";
    public static string CommandDescription => "Release mouse button (end drag)";

    protected override CGEventType GetMoveEventConstant() => CGEventType.kCGEventLeftMouseDragged;

    protected override void PerformActionAtPoint(CGPoint point)
        => PostMouseEvent(CGEventType.kCGEventLeftMouseUp, point);
}
