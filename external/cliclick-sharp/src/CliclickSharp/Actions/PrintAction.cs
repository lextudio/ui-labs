using CliclickSharp.Native;

namespace CliclickSharp.Actions;

public class PrintAction : IAction
{
    public static string CommandShortcut => "p";
    public static string CommandDescription => "Print text or mouse position";

    public bool PerformAction(string data, ExecutionOptions options)
    {
        string output;

        if (string.IsNullOrEmpty(data) || data == ".")
        {
            CGPoint pos = CoreGraphics.CGEventGetLocation(IntPtr.Zero);
            output = $"{(int)pos.X},{(int)pos.Y}";
        }
        else
        {
            output = data;
        }

        options.CommandOutputHandler?.WriteLine(output);
        return true;
    }
}
