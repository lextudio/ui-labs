using System.Globalization;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.VisualTree;
using LeXtudio.DevFlow.Agent.Core;
using Microsoft.Maui.DevFlow.Agent.Core;

namespace LeXtudio.DevFlow.Agent.Avalonia;

/// <summary>
/// Walks the visual trees of an Avalonia application's open windows. Must be called on the UI thread.
/// </summary>
public class AvaloniaVisualTreeWalker : IVisualTreeWalker
{
    private readonly ConditionalWeakTable<StyledElement, string> _stableIds = new();
    private readonly Dictionary<string, StyledElement> _elementsByStableId = new(StringComparer.Ordinal);

    // GetChildren merges the visual and the logical tree, because neither is complete on its own: content
    // that has not been templated yet is only logical, and template parts are only visual. Most nodes are
    // reachable both ways, so each element is materialized once per walk, keyed by reference. The set also
    // guards against reference cycles.
    private readonly HashSet<StyledElement> _visited = new(ReferenceEqualityComparer.Instance);

    /// <summary>The top-level windows of the application, in opening order.</summary>
    public static IReadOnlyList<Window> GetWindows()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            return desktop.Windows.ToList();

        return Array.Empty<Window>();
    }

    /// <summary>The application's main window, or the first open window.</summary>
    public static Window? GetMainWindow()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            return desktop.MainWindow ?? desktop.Windows.FirstOrDefault();

        return null;
    }

    public List<ElementInfo> WalkTree()
    {
        _elementsByStableId.Clear();
        _visited.Clear();

        var roots = new List<ElementInfo>();
        foreach (var window in GetWindows())
        {
            if (_visited.Add(window))
                roots.Add(BuildElementInfo(window, null));
        }

        return roots;
    }

    public ElementInfo? FindElementById(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return null;

        foreach (var root in WalkTree())
        {
            var found = FindByIdRecursive(root, id);
            if (found != null)
                return found;
        }

        return null;
    }

    /// <summary>
    /// Returns the element behind a stable id, as recorded by the most recent walk.
    /// </summary>
    public StyledElement? ResolveElementByStableId(string stableId)
    {
        if (string.IsNullOrWhiteSpace(stableId))
            return null;

        _elementsByStableId.TryGetValue(stableId, out var element);
        return element;
    }

    private static ElementInfo? FindByIdRecursive(ElementInfo node, string id)
    {
        if (node.Id == id)
            return node;

        if (node.Children != null)
        {
            foreach (var child in node.Children)
            {
                var candidate = FindByIdRecursive(child, id);
                if (candidate != null)
                    return candidate;
            }
        }

        return null;
    }

    private ElementInfo BuildElementInfo(StyledElement element, string? parentId)
    {
        var id = GetStableId(element);
        _elementsByStableId[id] = element;

        return new ElementInfo
        {
            Id = id,
            ParentId = parentId,
            Type = element.GetType().Name,
            FullType = element.GetType().FullName ?? element.GetType().Name,
            Framework = "avalonia",
            AutomationId = GetAutomationId(element),
            Text = GetText(element),
            IsVisible = element is not Visual visual || visual.IsVisible,
            IsEnabled = element is not InputElement input || input.IsEffectivelyEnabled,
            IsFocused = element is InputElement { IsFocused: true },
            Opacity = element is Visual v ? v.Opacity : 1d,
            Bounds = ResolveBounds(element),
            NativeType = element.GetType().FullName,
            NativeProperties = BuildNativeProperties(element, id),
            FrameworkProperties = BuildFrameworkProperties(element),
            Children = GetChildren(element)
                .Where(child => _visited.Add(child))
                .Select(child => BuildElementInfo(child, id))
                .ToList()
        };
    }

    private string GetStableId(StyledElement element)
    {
        return _stableIds.GetValue(element, static obj =>
        {
            if (!string.IsNullOrEmpty(obj.Name))
                return obj.Name;
            return "_avaloniadevflow_" + Guid.NewGuid().ToString("N").Substring(0, 12);
        });
    }

    private static string? GetAutomationId(StyledElement element)
    {
        var automationId = AutomationProperties.GetAutomationId(element);
        return string.IsNullOrEmpty(automationId) ? null : automationId;
    }

    internal static string? GetText(StyledElement element)
    {
        switch (element)
        {
            case TextBox textBox:
                return textBox.Text;
            case TextBlock textBlock:
                return textBlock.Text;
            case Window window:
                return window.Title;
            case HeaderedSelectingItemsControl headered:
                return headered.Header?.ToString();
            case HeaderedItemsControl headered:
                return headered.Header?.ToString();
            case HeaderedContentControl headered when headered.Header is string header:
                return header;
            case ContentControl contentControl when contentControl.Content is string text:
                return text;
            default:
                return null;
        }
    }

    private static BoundsInfo? ResolveBounds(StyledElement element)
    {
        try
        {
            // Device independent units, like the WPF agent: a window reports its screen position, every
            // other element its position relative to the window that contains it.
            if (element is Window window)
            {
                var scaling = window.RenderScaling > 0 ? window.RenderScaling : 1d;
                return CreateIfFinite(window.Position.X / scaling, window.Position.Y / scaling, window.Bounds.Width, window.Bounds.Height);
            }

            if (element is Visual visual && visual.IsEffectivelyVisible && TopLevel.GetTopLevel(visual) is { } topLevel)
            {
                var origin = visual.TranslatePoint(default, topLevel);
                if (origin is { } point)
                    return CreateIfFinite(point.X, point.Y, visual.Bounds.Width, visual.Bounds.Height);
            }
        }
        catch
        {
        }

        return null;
    }

    // A window that is closing can transiently report non-finite values, and System.Text.Json throws on
    // those, which would abort the whole tree response. Treat them as "no bounds".
    private static BoundsInfo? CreateIfFinite(double x, double y, double width, double height)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(width) || !double.IsFinite(height))
            return null;

        return new BoundsInfo { X = x, Y = y, Width = width, Height = height };
    }

    private static Dictionary<string, string?> BuildNativeProperties(StyledElement element, string stableId)
    {
        var props = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["nativeType"] = element.GetType().FullName,
            ["stableId"] = stableId
        };

        if (!string.IsNullOrEmpty(element.Name))
            props["name"] = element.Name;

        var automationId = GetAutomationId(element);
        if (automationId != null)
            props["automationId"] = automationId;

        if (element.Classes.Count > 0)
            props["classes"] = string.Join(" ", element.Classes);

        if (element is Window window)
        {
            // The window's position in screen pixels, the coordinate space of the global input actions.
            props["screenX"] = window.Position.X.ToString(CultureInfo.InvariantCulture);
            props["screenY"] = window.Position.Y.ToString(CultureInfo.InvariantCulture);
            props["renderScaling"] = window.RenderScaling.ToString(CultureInfo.InvariantCulture);
        }

        return props;
    }

    private static Dictionary<string, string?>? BuildFrameworkProperties(StyledElement element)
    {
        var props = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        if (element is ScrollViewer scroll)
        {
            props["horizontalOffset"] = scroll.Offset.X.ToString(CultureInfo.InvariantCulture);
            props["verticalOffset"] = scroll.Offset.Y.ToString(CultureInfo.InvariantCulture);
            props["extentWidth"] = scroll.Extent.Width.ToString(CultureInfo.InvariantCulture);
            props["extentHeight"] = scroll.Extent.Height.ToString(CultureInfo.InvariantCulture);
        }

        switch (element)
        {
            case TemplatedControl control:
                props["background"] = BrushToString(control.Background);
                props["foreground"] = BrushToString(control.Foreground);
                props["borderBrush"] = BrushToString(control.BorderBrush);
                break;
            case global::Avalonia.Controls.Shapes.Shape shape:
                props["fill"] = BrushToString(shape.Fill);
                props["stroke"] = BrushToString(shape.Stroke);
                break;
            case Panel panel:
                props["background"] = BrushToString(panel.Background);
                break;
            case Border border:
                props["background"] = BrushToString(border.Background);
                props["borderBrush"] = BrushToString(border.BorderBrush);
                break;
            case TextBlock textBlock:
                props["foreground"] = BrushToString(textBlock.Foreground);
                props["background"] = BrushToString(textBlock.Background);
                break;
        }

        foreach (var key in props.Keys.Where(k => props[k] == null).ToList())
            props.Remove(key);

        return props.Count > 0 ? props : null;
    }

    private static string? BrushToString(IBrush? brush)
    {
        if (brush is ISolidColorBrush solid)
        {
            var c = solid.Color;
            return c.A == 255
                ? $"#{c.R:X2}{c.G:X2}{c.B:X2}"
                : $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";
        }

        return brush?.GetType().Name;
    }

    private static IEnumerable<StyledElement> GetChildren(StyledElement element)
    {
        if (element is Visual visual)
        {
            foreach (var child in visual.GetVisualChildren())
            {
                if (child is StyledElement styled)
                    yield return styled;
            }
        }

        if (element is ILogical logical)
        {
            foreach (var child in logical.GetLogicalChildren())
            {
                if (child is StyledElement styled)
                    yield return styled;
            }
        }
    }
}
