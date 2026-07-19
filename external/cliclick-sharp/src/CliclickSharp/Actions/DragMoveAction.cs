using CliclickSharp.Native;

namespace CliclickSharp.Actions;

public class DragMoveAction : MouseBaseAction, IAction
{
    public static string CommandShortcut => "dm";
    public static string CommandDescription => "Drag to coordinates";

    protected override CGEventType GetMoveEventConstant() => CGEventType.kCGEventLeftMouseDragged;

    protected override void PerformActionAtPoint(CGPoint point)
    {
    }
}
