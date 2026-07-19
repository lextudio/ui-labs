namespace CliclickSharp.Actions;

public interface IAction
{
    static abstract string CommandShortcut { get; }
    static abstract string CommandDescription { get; }
    bool PerformAction(string data, ExecutionOptions options);
}
