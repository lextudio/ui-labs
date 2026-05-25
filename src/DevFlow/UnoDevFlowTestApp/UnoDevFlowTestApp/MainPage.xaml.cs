using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using LeXtudio.DevFlow.Agent.Core;

namespace UnoDevFlowTestApp;

public sealed partial class MainPage : Page
{
    private readonly List<string> _eventLog = [];

    public MainPage()
    {
        this.InitializeComponent();
        Loaded += MainPage_Loaded;
    }

    private void MainPage_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            WebViewHost.NavigateToString("""
<!doctype html>
<html><body style="font-family:Segoe UI;padding:12px">
<h3 id="title">DevFlow Uno WebView Test</h3>
<p id="content">Deterministic inline HTML for screenshot validation.</p>
</body></html>
""");
        }
        catch
        {
        }
    }

    private void ActionButton_Click(object sender, RoutedEventArgs e)
    {
        ResponseText.Text = $"Button clicked at {System.DateTime.Now:T}";
    }

    private void EventProbeButton_Click(object sender, RoutedEventArgs e)
    {
        AppendEvent("button.click");
        ResponseText.Text = "Probe button clicked";
    }

    private void DisabledActionButton_Click(object sender, RoutedEventArgs e)
    {
        DisabledButtonResultText.Text = "disabled button clicked";
    }

    private void EventProbeInput_GotFocus(object sender, RoutedEventArgs e)
    {
        AppendEvent("input.focus");
    }

    private void EventProbeInput_TextChanging(TextBox sender, TextBoxTextChangingEventArgs args)
    {
        AppendEvent("input.textChanging");
    }

    private void EventProbeInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        AppendEvent("input.textChanged");
    }

    private void EventProbeInput_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        var keyName = e.Key.ToString();
        AppendEvent($"input.keyDown:{keyName}");
        if (string.Equals(keyName, "Enter", StringComparison.OrdinalIgnoreCase))
        {
            ResponseText.Text = "Enter received";
        }
    }

    private void AppendEvent(string name)
    {
        _eventLog.Add(name);
        EventLogText.Text = $"events:{string.Join("|", _eventLog)}";
    }

    [DevFlowAction("uno.echo", Description = "Echoes an input string for invoke API tests.")]
    public static string Echo(string value) => $"echo:{value}";
}
