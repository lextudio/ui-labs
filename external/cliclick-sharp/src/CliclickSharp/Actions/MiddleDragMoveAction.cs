using CliclickSharp.Native;

namespace CliclickSharp.Actions;

public class MiddleDragMoveAction : MouseBaseAction, IAction
{
    public static string CommandShortcut => "mdm";
    public static string CommandDescription => "Middle-drag to coordinates";

    protected override CGMouseButton GetMouseButton() => CGMouseButton.kCGMouseButtonCenter;

    protected override CGEventType GetMoveEventConstant() => CGEventType.kCGEventOtherMouseDragged;

    // The move itself is the whole action; the humanized move loop in the base class posts it.
    protected override void PerformActionAtPoint(CGPoint point)
    {
    }
}
