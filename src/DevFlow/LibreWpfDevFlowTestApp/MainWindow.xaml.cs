using System.Windows;
using LeXtudio.DevFlow.Agent.Core;
using Microsoft.Maui.DevFlow.Agent.Core;
using Microsoft.Web.WebView2.Wpf;

namespace LibreWpfDevFlowTestApp;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Loaded += MainWindow_Loaded;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await TryInitializeWebViewAsync();
    }

    /// <summary>
    /// Creates the WebView2 surface on Windows only.
    /// </summary>
    /// <remarks>
    /// Microsoft.Web.WebView2 ships a Windows-only runtime, and LibreWPF runs this same project on macOS
    /// and Linux. Declaring the control in XAML would make the non-Windows builds depend on an
    /// unavailable runtime, so the host element stays a plain <c>Border</c> and the WebView2 control is
    /// attached at runtime when the operating system can support it. Elsewhere the host stays empty and
    /// DevFlow reports no WebView contexts.
    /// </remarks>
    private async Task TryInitializeWebViewAsync()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            var webView = new WebView2();
            WebViewHost.Child = webView;

            await webView.EnsureCoreWebView2Async();
            webView.CoreWebView2.NavigateToString("""
<!doctype html>
<html><body style="font-family:Segoe UI;padding:12px">
<h3 id="title">DevFlow LibreWPF WebView Test</h3>
<p id="content">Deterministic inline HTML for screenshot validation.</p>
</body></html>
""");
        }
        catch
        {
            // Keep the sample app resilient when the WebView2 runtime is unavailable.
        }
    }

    private void ActionButton_Click(object sender, RoutedEventArgs e)
    {
        ResponseText.Text = "Button clicked at " + System.DateTime.Now.ToLongTimeString();
    }

    [DevFlowAction("wpf.echo", Description = "Echoes an input string for invoke API tests.")]
    public static string Echo(string value) => $"echo:{value}";
}