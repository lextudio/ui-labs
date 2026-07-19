using System.Reflection;
using CliclickSharp.Actions;

namespace CliclickSharp;

public class ActionExecutor
{
    private static readonly Dictionary<string, Type> ActionClasses;

    static ActionExecutor()
    {
        ActionClasses = new Dictionary<string, Type>();

        Type[] actionTypes =
        [
            typeof(MoveAction),
            typeof(ClickAction),
            typeof(DoubleclickAction),
            typeof(TripleclickAction),
            typeof(RightClickAction),
            typeof(DragDownAction),
            typeof(DragMoveAction),
            typeof(DragUpAction),
            typeof(KeyDownAction),
            typeof(KeyUpAction),
            typeof(KeyPressAction),
            typeof(TypeAction),
            typeof(PrintAction),
            typeof(WaitAction),
            typeof(ColorPickerAction),
        ];

        foreach (var type in actionTypes)
        {
            var shortcutProp = type.GetProperty("CommandShortcut", BindingFlags.Public | BindingFlags.Static);
            if (shortcutProp?.GetValue(null) is string shortcut)
            {
                ActionClasses[shortcut] = type;
            }
        }
    }

    public static bool ExecuteActionString(string actionString, ExecutionOptions options)
    {
        if (string.IsNullOrWhiteSpace(actionString))
            return true;

        actionString = actionString.Trim();

        int colonIndex = actionString.IndexOf(':');
        string shortcut;
        string data;

        if (colonIndex == -1)
        {
            shortcut = actionString;
            data = string.Empty;
        }
        else
        {
            shortcut = actionString[..colonIndex];
            data = actionString[(colonIndex + 1)..];
        }

        shortcut = shortcut.ToLowerInvariant();

        if (!ActionClasses.TryGetValue(shortcut, out Type? actionType))
        {
            Console.Error.WriteLine($"Unknown command: {shortcut}");
            return false;
        }

        if (options.Mode == OutputMode.Test)
        {
            var descProp = actionType.GetProperty("CommandDescription", BindingFlags.Public | BindingFlags.Static);
            string description = descProp?.GetValue(null) as string ?? shortcut;
            if (!string.IsNullOrEmpty(data))
                options.VerbosityOutputHandler?.WriteLine($"{description} ({data})");
            else
                options.VerbosityOutputHandler?.WriteLine(description);
            return true;
        }

        if (options.Mode == OutputMode.Verbose)
        {
            var descProp = actionType.GetProperty("CommandDescription", BindingFlags.Public | BindingFlags.Static);
            string description = descProp?.GetValue(null) as string ?? shortcut;
            if (!string.IsNullOrEmpty(data))
                options.VerbosityOutputHandler?.WriteLine($"{description} ({data})");
            else
                options.VerbosityOutputHandler?.WriteLine(description);
        }

        var instance = Activator.CreateInstance(actionType) as IAction;
        if (instance == null)
        {
            Console.Error.WriteLine($"Failed to create action: {shortcut}");
            return false;
        }

        return instance.PerformAction(data, options);
    }
}
