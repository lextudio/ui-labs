using CliclickSharp.Actions;

namespace CliclickSharp;

public class ActionExecutor
{
    // A named delegate is required because IAction has static abstract members, which
    // disqualifies it as a generic type argument (Func<IAction> won't compile). A delegate
    // return type is not a type argument, so this is allowed.
    private delegate IAction ActionFactory();

    // Direct-`new` factories keyed by shortcut. Native AOT (PublishAot=true) cannot
    // reflection-activate action types via Activator.CreateInstance(Type) — the parameterless
    // ctor is not rooted, so activation throws MissingMethodException. Reading the shortcut via
    // reflection (GetProperty on a static-abstract member) is equally unreliable under AOT and
    // silently returned null, leaving the table empty ("Unknown command"). Both are replaced with
    // reflection-free registration: the shortcut/description come from the static-abstract members
    // accessed through a generic constraint, and the instance from a compiled `new`.
    private static readonly Dictionary<string, ActionFactory> ActionFactories;
    private static readonly Dictionary<string, string> ActionDescriptions;

    static ActionExecutor()
    {
        ActionFactories = new Dictionary<string, ActionFactory>();
        ActionDescriptions = new Dictionary<string, string>();

        Register<MoveAction>(() => new MoveAction());
        Register<ClickAction>(() => new ClickAction());
        Register<DoubleclickAction>(() => new DoubleclickAction());
        Register<TripleclickAction>(() => new TripleclickAction());
        Register<RightClickAction>(() => new RightClickAction());
        Register<DragDownAction>(() => new DragDownAction());
        Register<DragMoveAction>(() => new DragMoveAction());
        Register<DragUpAction>(() => new DragUpAction());
        Register<KeyDownAction>(() => new KeyDownAction());
        Register<KeyUpAction>(() => new KeyUpAction());
        Register<KeyPressAction>(() => new KeyPressAction());
        Register<TypeAction>(() => new TypeAction());
        Register<PrintAction>(() => new PrintAction());
        Register<WaitAction>(() => new WaitAction());
        Register<ColorPickerAction>(() => new ColorPickerAction());
    }

    private static void Register<T>(ActionFactory factory) where T : IAction
    {
        ActionFactories[T.CommandShortcut] = factory;
        ActionDescriptions[T.CommandShortcut] = T.CommandDescription;
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

        if (!ActionFactories.TryGetValue(shortcut, out var factory))
        {
            Console.Error.WriteLine($"Unknown command: {shortcut}");
            return false;
        }

        if (options.Mode is OutputMode.Test or OutputMode.Verbose)
        {
            string description = ActionDescriptions.TryGetValue(shortcut, out var d) ? d : shortcut;
            if (!string.IsNullOrEmpty(data))
                options.VerbosityOutputHandler?.WriteLine($"{description} ({data})");
            else
                options.VerbosityOutputHandler?.WriteLine(description);
            if (options.Mode == OutputMode.Test)
                return true;
        }

        var instance = factory();
        if (instance == null)
        {
            Console.Error.WriteLine($"Failed to create action: {shortcut}");
            return false;
        }

        return instance.PerformAction(data, options);
    }
}
