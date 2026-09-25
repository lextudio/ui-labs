using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using LeXtudio.DevFlow.Agent.Core;
using Microsoft.Maui.DevFlow.Agent.Core;
using Microsoft.UI.Xaml;
using ProGPU.Scene;
using ProGPU.WinUI;

namespace LeXtudio.DevFlow.Agent.ProGPU;

/// <summary>
/// DevFlow agent service for ProGPU.WinUI applications.
/// Provides visual tree inspection and diagnostics over an HTTP API on localhost.
/// </summary>
public sealed class ProGpuAgentService : DevFlowAgentServiceBase
{
    private readonly ProGpuVisualTreeWalker _treeWalker = new();

    public ProGpuAgentService(AgentOptions? options = null) : base(options)
    {
    }

    protected override string AgentId => $"progpu-{Environment.ProcessId}";
    protected override string AgentName => "ProGPU DevFlow Agent";
    protected override string FrameworkName => "progpu";

    protected override object GetCapabilities() => new
    {
        screenshots = false,
        elementScreenshots = false,
        selectorScreenshots = false,
        tap = true,
        rightTap = false,
        scroll = false,
        fill = false,
        clear = false,
        focus = false,
        key = false,
        back = false,
        structuredErrors = true,
        appTheme = false,
        webview = false,
        webviewCdp = false,
        multiWindow = true,
        windowContentOrigin = true,
        invokeActions = true,
    };

    protected override Task<List<ElementInfo>> BuildTreeAsync()
    {
        return Task.FromResult(_treeWalker.WalkTree());
    }

    protected override Task<ElementInfo?> FindElementAsync(string id)
    {
        return Task.FromResult(_treeWalker.FindElementById(id));
    }

    protected override Task<List<ElementInfo>> QueryElementsAsync(string? type, string? automationId, string? text, int maxResults, int maxDepth)
    {
        return Task.FromResult(_treeWalker.QueryElements(type, automationId, text, maxResults, maxDepth));
    }

    protected override Task<byte[]?> CaptureScreenshotAsync(string? elementId, string? selector)
    {
        return Task.FromResult<byte[]?>(null);
    }

    protected override Task<bool> TryTapAsync(string elementId)
    {
        if (_treeWalker.FindVisualById(elementId) is not FrameworkElement target || !target.IsEnabled)
        {
            return Task.FromResult(false);
        }

		if (target is Microsoft.UI.Xaml.Controls.Primitives.ButtonBase button)
		{
			button.PerformClick();
			return Task.FromResult(true);
		}

        var pointerEvent = new PointerRoutedEventArgs
        {
            Position = target.Size / 2f,
            ScreenPosition = target.Size / 2f,
            IsLeftButtonPressed = true,
        };
        target.OnPointerEntered(pointerEvent);
        target.OnPointerPressed(pointerEvent);
        pointerEvent.IsLeftButtonPressed = false;
        target.OnPointerReleased(pointerEvent);
        return Task.FromResult(true);
    }

    protected override Task<bool> TryScrollAsync(string elementId, double deltaX, double deltaY)
    {
        return Task.FromResult(false);
    }

    protected override Task<bool> TryFillAsync(string elementId, string text)
    {
        return Task.FromResult(false);
    }

    protected override Task<bool> TryClearAsync(string elementId)
    {
        return Task.FromResult(false);
    }

    protected override Task<bool> TryFocusAsync(string elementId)
    {
        return Task.FromResult(false);
    }

    protected override Task<object?> TryKeyAsync(string? elementId, string? key, string? text)
    {
        return Task.FromResult<object?>(null);
    }

    protected override Task<bool> TryBackAsync()
    {
        return Task.FromResult(false);
    }

    protected override Task<object?> GetThemeAsync()
    {
        return Task.FromResult<object?>("light");
    }

    protected override Task<object?> SetThemeAsync(string theme)
    {
        return Task.FromResult<object?>(false);
    }

    protected override Task<string?> GetApplicationNameAsync()
    {
        var entryAssembly = Assembly.GetEntryAssembly();
        return Task.FromResult(entryAssembly?.GetName().Name);
    }
}
