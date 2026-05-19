using System.Windows;

namespace WpfDevFlowTestApp;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void ActionButton_Click(object sender, RoutedEventArgs e)
    {
        ResponseText.Text = "Button clicked at " + System.DateTime.Now.ToLongTimeString();
    }
}
