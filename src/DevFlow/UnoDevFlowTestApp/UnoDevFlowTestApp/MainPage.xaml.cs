using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using LeXtudio.DevFlow.Agent.Core;
using Microsoft.Maui.DevFlow.Agent.Core;

namespace UnoDevFlowTestApp;

public sealed partial class MainPage : Page
{
    private readonly List<string> _eventLog = [];

    // Drag-probe state: PointerPressed/Moved/Released on DragProbeSurface append here so a
    // DevFlow probe can read the exact gesture DevFlow's injected drag produced.
    private static MainPage? _current;
    private readonly List<string> _dragEvents = [];
    private readonly object _dragLock = new();

    public MainPage()
    {
        _current = this;
        this.InitializeComponent();
        Loaded += MainPage_Loaded;
        HookDragProbe();
    }

    private void HookDragProbe()
    {
        // handledEventsToo:true so we still see events even if an inner element handles them.
        DragProbeSurface.AddHandler(UIElement.PointerPressedEvent,
            new PointerEventHandler((_, e) => RecordDrag("down", e)), true);
        DragProbeSurface.AddHandler(UIElement.PointerMovedEvent,
            new PointerEventHandler((_, e) => RecordDrag("move", e)), true);
        DragProbeSurface.AddHandler(UIElement.PointerReleasedEvent,
            new PointerEventHandler((_, e) => RecordDrag("up", e)), true);
    }

    private void RecordDrag(string kind, PointerRoutedEventArgs e)
    {
        var p = e.GetCurrentPoint(DragProbeSurface).Position;
        lock (_dragLock)
            _dragEvents.Add($"{kind}@{p.X:F0},{p.Y:F0}");
        DragProbeLogText.Text = "drag:" + DragLogSnapshot();
    }

    private string DragLogSnapshot()
    {
        lock (_dragLock)
            return string.Join("|", _dragEvents);
    }

    // Run fn on the UI thread and wait (up to 10s), mirroring DockDiagnostics.RunOnUI.
    private static T RunOnUI<T>(Func<MainPage, T> fn)
    {
        var page = _current ?? throw new InvalidOperationException("MainPage not created yet");
        T result = default!;
        Exception? ex = null;
        using var ready = new ManualResetEventSlim(false);
        var dq = page.DispatcherQueue ?? throw new InvalidOperationException("no DispatcherQueue");
        dq.TryEnqueue(() =>
        {
            try { result = fn(page); }
            catch (Exception e) { ex = e; }
            finally { ready.Set(); }
        });
        ready.Wait(TimeSpan.FromSeconds(10));
        if (ex != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex).Throw();
        return result;
    }

    [DevFlowAction("uno.drag-log-reset", Description = "Clears the drag-probe event log. Call before injecting a drag.")]
    public static string DragLogReset() => RunOnUI(page =>
    {
        lock (page._dragLock)
            page._dragEvents.Clear();
        page.DragProbeLogText.Text = "drag:";
        return "reset";
    });

    [DevFlowAction("uno.drag-log", Description = "Returns the recorded drag-probe events as 'kind@x,y' entries joined by '|' (down/move/up).")]
    public static string DragLog() => RunOnUI(page => page.DragLogSnapshot());

    [DevFlowAction("uno.drag-surface-rect", Description = "Returns the DragProbeSurface window-local rect (points) as 'x,y,w,h' via TransformToVisual(null).")]
    public static string DragSurfaceRect() => RunOnUI(page =>
    {
        var origin = page.DragProbeSurface.TransformToVisual(null)
            .TransformPoint(new Windows.Foundation.Point(0, 0));
        var w = page.DragProbeSurface.ActualWidth;
        var h = page.DragProbeSurface.ActualHeight;
        return string.Format(CultureInfo.InvariantCulture, "{0:F1},{1:F1},{2:F1},{3:F1}", origin.X, origin.Y, w, h);
    });

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
