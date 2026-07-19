namespace CliclickSharp;

public enum OutputMode
{
    Regular,
    Verbose,
    Test,
}

public class ExecutionOptions
{
    public OutputMode Mode { get; set; } = OutputMode.Regular;
    public uint Easing { get; set; }
    public uint WaitTime { get; set; }
    public bool IsFirstAction { get; set; } = true;
    public bool IsLastAction { get; set; }
    public OutputHandler? VerbosityOutputHandler { get; set; }
    public OutputHandler? CommandOutputHandler { get; set; }
}
