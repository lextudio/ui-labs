namespace LeXtudio.Wpf.Cli
{
    public sealed record OutputOptions
    {
        public static OutputOptions Default { get; } = new OutputOptions();

        public bool Json { get; init; }
        public bool Verbose { get; init; }
        public bool DryRun { get; init; }
        public bool Ci { get; init; }
        public bool ShowHelp { get; init; }
    }
}
