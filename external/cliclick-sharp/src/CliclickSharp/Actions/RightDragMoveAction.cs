using CliclickSharp.Native;

namespace CliclickSharp.Actions;

public class RightDragMoveAction : MouseBaseAction, IAction
{
    public static string CommandShortcut => "rdm";
    public static string CommandDescription => "Right-drag to coordinates";

    protected override CGMouseButton GetMouseButton() => CGMouseButton.kCGMouseButtonRight;

    protected override CGEventType GetMoveEventConstant() => CGEventType.kCGEventRightMouseDragged;

    // The move itself is the whole action; the humanized move loop in the base class posts it.
    protected override void PerformActionAtPoint(CGPoint point)
    {
    }
}
