using System.IO;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Maui.DevFlow.Agent.Core;
using LeXtudio.Wpf.DevFlow.Agent.Core;

namespace LeXtudio.Wpf.DevFlow.Agent.WPF;

public sealed class WpfAgentService : DevFlowAgentServiceBase
{
    private readonly WpfVisualTreeWalker _treeWalker = new();

    public WpfAgentService(AgentOptions? options = null)
        : base(options)
    {
    }

    protected override string AgentId => "LeXtudio.Wpf.DevFlow.Agent";
    protected override string AgentName => "LeXtudio.Wpf.DevFlow.Agent";
    protected override string FrameworkName => "wpf";

    protected override Task<string?> GetApplicationNameAsync()
    {
        return Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            var app = Application.Current;
            return app?.GetType().Name;
        }).Task ?? Task.FromResult<string?>(null);
    }

    protected override Task<List<Microsoft.Maui.DevFlow.Agent.Core.ElementInfo>> BuildTreeAsync()
    {
        return Application.Current?.Dispatcher.InvokeAsync(() => _treeWalker.WalkTree()).Task
               ?? Task.FromResult(new List<Microsoft.Maui.DevFlow.Agent.Core.ElementInfo>());
    }

    protected override Task<Microsoft.Maui.DevFlow.Agent.Core.ElementInfo?> FindElementAsync(string id)
    {
        return Application.Current?.Dispatcher.InvokeAsync(() => _treeWalker.FindElementById(id)).Task
               ?? Task.FromResult<Microsoft.Maui.DevFlow.Agent.Core.ElementInfo?>(null);
    }

    protected override Task<byte[]?> CaptureScreenshotAsync()
    {
        return Application.Current?.Dispatcher.InvokeAsync(CapturePrimaryWindowScreenshot).Task
               ?? Task.FromResult<byte[]?>(null);
    }

    protected override Task<bool> TryTapAsync(string elementId)
    {
        return Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            var element = _treeWalker.FindElementById(elementId);
            if (element == null) return false;

            var target = _treeWalker.ResolveElementByStableId(element.Id);
            if (target is null) return false;

            return TryInvokeOnElement(target);
        }).Task ?? Task.FromResult(false);
    }

    private bool TryInvokeOnElement(DependencyObject target)
    {
        try
        {
            if (target is ButtonBase buttonBase)
            {
                buttonBase.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, buttonBase));
                return true;
            }

            if (target is UIElement ui)
            {
                ui.Focus();
                return true;
            }
        }
        catch
        {
        }

        return false;
    }

    private static byte[]? CapturePrimaryWindowScreenshot()
    {
        var app = Application.Current;
        var window = app?.MainWindow ?? app?.Windows.OfType<Window>().FirstOrDefault();
        if (window == null)
            return null;

        var width = (int)Math.Ceiling(window.ActualWidth);
        var height = (int)Math.Ceiling(window.ActualHeight);
        if (width <= 0 || height <= 0)
            return null;

        var source = PresentationSource.FromVisual(window);
        var dpi = 96.0;
        if (source?.CompositionTarget != null)
            dpi = 96.0 * source.CompositionTarget.TransformToDevice.M11;

        var rtb = new RenderTargetBitmap(width, height, dpi, dpi, PixelFormats.Pbgra32);
        rtb.Render(window);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }
}
