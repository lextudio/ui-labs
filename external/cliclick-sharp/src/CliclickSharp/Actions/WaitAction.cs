namespace CliclickSharp.Actions;

public class WaitAction : IAction
{
    public static string CommandShortcut => "w";
    public static string CommandDescription => "Wait for specified duration";

    public bool PerformAction(string data, ExecutionOptions options)
    {
        if (!uint.TryParse(data, out uint ms))
        {
            Console.Error.WriteLine($"Invalid wait duration: {data}");
            return false;
        }

        Thread.Sleep((int)ms);
        return true;
    }
}
