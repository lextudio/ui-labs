using System.Windows.Forms;
using LeXtudio.DevFlow.Agent.Core;
using Microsoft.Maui.DevFlow.Agent.Core;

namespace LibreWinFormsDevFlowTestApp;

public class MainForm : Form
{
    private readonly Label _response;

    public MainForm()
    {
        Name = "MainForm";
        Text = "LibreWinForms DevFlow Test";
        Width = 500;
        Height = 400;

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

        Controls.Add(button);
        Controls.Add(input);
        Controls.Add(_response);
        Controls.Add(panel);
    }

    [DevFlowAction("librewinforms.echo", Description = "Echoes an input string for invoke API tests.")]
    public static string Echo(string value) => $"echo:{value}";
}