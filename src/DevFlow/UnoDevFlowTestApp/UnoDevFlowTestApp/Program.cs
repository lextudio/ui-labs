using Microsoft.UI.Xaml;

namespace UnoDevFlowTestApp;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        App.InitializeLogging();
        Application.Start(_ => new App());
    }
}
