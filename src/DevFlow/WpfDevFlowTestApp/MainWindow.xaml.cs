using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using LeXtudio.DevFlow.Agent.Core;
using Microsoft.Maui.DevFlow.Agent.Core;
using Microsoft.Maui.DevFlow.Agent.Core.Network;
using Microsoft.Web.WebView2.Core;

namespace WpfDevFlowTestApp;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Loaded += MainWindow_Loaded;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await WebViewHost.EnsureCoreWebView2Async();
            WebViewHost.CoreWebView2.NavigateToString("""
<!doctype html>
<html><body style="font-family:Segoe UI;padding:12px">
<h3 id="title">DevFlow WPF WebView Test</h3>
<p id="content">Deterministic inline HTML for screenshot validation.</p>
</body></html>
""");
        }
        catch
        {
            // Keep sample app resilient even when WebView2 runtime is unavailable.
        }
    }

    private void ActionButton_Click(object sender, RoutedEventArgs e)
    {
        ResponseText.Text = "Button clicked at " + System.DateTime.Now.ToLongTimeString();
    }

    [DevFlowAction("wpf.echo", Description = "Echoes an input string for invoke API tests.")]
    public static string Echo(string value) => $"echo:{value}";

    [DevFlowAction("wpf.network-test", Description = "Issues an HTTP GET through DevFlowHttp for network-monitor validation.")]
    public static async Task<string> NetworkTest(string url = "https://example.com")
    {
        using var client = DevFlowHttp.CreateClient(new HttpClientHandler());
        var response = await client.GetAsync(url);
        return $"{(int)response.StatusCode} {response.ReasonPhrase}";
    }

    [DevFlowAction("wpf.show-alert", Description = "Shows a modal MessageBox for alert-detection validation.")]
    public static Task ShowAlert(string message = "Test alert message")
    {
        Application.Current.Dispatcher.BeginInvoke(() => MessageBox.Show(message, "Test Alert"));
        return Task.CompletedTask;
    }
}
