using System.Windows.Forms;
using LeXtudio.DevFlow.Agent.Core;
using Microsoft.Maui.DevFlow.Agent.Core;
using Microsoft.Web.WebView2.WinForms;

namespace WinFormsDevFlowTestApp;

public class MainForm : Form
{
    private readonly Label _response;

    public MainForm()
    {
        Name = "MainForm";
        Text = "WinForms DevFlow Test";
        Width = 500;
        Height = 520;

        _response = new Label
        {
            Name = "ResponseLabel",
            Left = 20,
            Top = 100,
            Width = 300,
            Text = "ready"
        };

        var button = new Button
        {
            Name = "ActionButton",
            Text = "Tap Me",
            Left = 20,
            Top = 20,
            Width = 120
        };
        button.Click += (_, _) => _response.Text = "Button clicked";

        var input = new TextBox
        {
            Name = "InputBox",
            Left = 20,
            Top = 60,
            Width = 200,
            Text = "initial"
        };

        var panel = new Panel
        {
            Name = "MainScrollPanel",
            Left = 20,
            Top = 140,
            Width = 300,
            Height = 120,
            AutoScroll = true
        };
        var spacer = new Label { Name = "ScrollSpacer", Top = 220, Left = 0, Width = 200, Text = "bottom" };
        panel.Controls.Add(spacer);

        var webView = new WebView2
        {
            Name = "WebViewHost",
            Left = 20,
            Top = 275,
            Width = 420,
            Height = 160
        };
        _ = InitializeWebViewAsync(webView);

        Controls.Add(button);
        Controls.Add(input);
        Controls.Add(_response);
        Controls.Add(panel);
        Controls.Add(webView);
    }

    private static async Task InitializeWebViewAsync(WebView2 webView)
    {
        try
        {
            await webView.EnsureCoreWebView2Async();
            webView.CoreWebView2.NavigateToString("""
<!doctype html>
<html><body style="font-family:Segoe UI;padding:12px">
<h3 id="title">DevFlow WinForms WebView Test</h3>
<p id="content">Deterministic inline HTML for screenshot validation.</p>
</body></html>
""");
        }
        catch
        {
            // Keep sample app resilient when the WebView2 runtime is unavailable.
        }
    }

    [DevFlowAction("winforms.echo", Description = "Echoes an input string for invoke API tests.")]
    public static string Echo(string value) => $"echo:{value}";
}
