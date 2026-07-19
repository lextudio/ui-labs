namespace CliclickSharp.Actions;

public abstract class KeyDownUpBaseAction
{
    protected static readonly Dictionary<string, ushort> ModifierKeycodes = new()
    {
        {"ctrl", 59},
        {"cmd", 55},
        {"alt", 58},
        {"shift", 56},
        {"fn", 63},
    };
}
