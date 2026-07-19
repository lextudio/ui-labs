using CliclickSharp.Native;

namespace CliclickSharp.Actions;

public class MoveAction : MouseBaseAction, IAction
{
    public static string CommandShortcut => "m";
    public static string CommandDescription => "Move mouse cursor";

    protected override void PerformActionAtPoint(CGPoint point)
    {
    }
}
