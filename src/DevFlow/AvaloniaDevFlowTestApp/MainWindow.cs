using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Microsoft.Maui.DevFlow.Agent.Core;

namespace AvaloniaDevFlowTestApp;

/// <summary>
/// The window the agent integration tests drive. Elements the tests address carry a Name, which the
/// agent reports as their element id.
/// </summary>
public sealed class MainWindow : Window
{
    private readonly TextBlock _responseText;
    private int _pointerPresses;
    private int _pointerReleases;

    public MainWindow()
    {
        Title = "Avalonia DevFlow Test";
        Width = 900;
        Height = 700;
        Position = new PixelPoint(40, 40);
        WindowStartupLocation = WindowStartupLocation.Manual;

        _responseText = new TextBlock { Name = "ResponseText", Text = "Starting DevFlow..." };

        var actionButton = new Button { Name = "ActionButton", Content = "Press me" };
        actionButton.Click += (_, _) => SetStatus("Button pressed.");

        var inputBox = new TextBox { Name = "InputBox", PlaceholderText = "Type here" };
        inputBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
                SetStatus($"Enter pressed: {inputBox.Text}");
        };

        var openWindowButton = new Button { Name = "OpenWindowButton", Content = "Open second window" };
        openWindowButton.Click += (_, _) => new Window
        {
            Name = "SecondWindow",
            Title = "Second Window",
            Width = 320,
            Height = 200,
            Content = new TextBlock { Name = "SecondWindowText", Text = "Second window" }
        }.Show(this);

        var pointerTarget = new Border
        {
            Name = "PointerTarget",
            Height = 80,
            Background = Brushes.SteelBlue,
            Child = new TextBlock { Text = "Pointer target", Foreground = Brushes.White, Margin = new Thickness(8) }
        };
        pointerTarget.PointerPressed += (_, _) => SetStatus($"Pointer pressed {++_pointerPresses}");
        pointerTarget.PointerReleased += (_, _) => SetStatus($"Pointer released {++_pointerReleases}");

        var items = new StackPanel { Name = "ScrollItems" };
        for (var i = 0; i < 60; i++)
            items.Children.Add(new TextBlock { Text = $"Item {i}", Margin = new Thickness(4) });

        var scrollArea = new ScrollViewer { Name = "ScrollArea", Height = 160, Content = items };

        Content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 12,
            Children =
            {
                new TextBlock { Text = "Avalonia DevFlow Sample", FontSize = 24 },
                actionButton,
                _responseText,
                inputBox,
                openWindowButton,
                pointerTarget,
                scrollArea
            }
        };
    }

    public void SetStatus(string text) => _responseText.Text = text;

    [DevFlowAction("avalonia.echo", Description = "Echoes an input string for invoke API tests.")]
    public string Echo(string text) => text;

    [DevFlowAction("avalonia.set-status", Description = "Sets the status text; runs on the UI thread.")]
    public string SetStatusAction(string text)
    {
        SetStatus(text);
        return _responseText.Text ?? string.Empty;
    }
}
