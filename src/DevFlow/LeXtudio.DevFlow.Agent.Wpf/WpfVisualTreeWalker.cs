using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using Microsoft.Maui.DevFlow.Agent.Core;
using LeXtudio.DevFlow.Agent.Core;

namespace LeXtudio.DevFlow.Agent.Wpf;

public class WpfVisualTreeWalker : IVisualTreeWalker
{
    private readonly ConditionalWeakTable<DependencyObject, string> _stableIds = new();
    private readonly Dictionary<string, DependencyObject> _elementsByStableId = new(StringComparer.Ordinal);

    // GetChildren below merges four overlapping child sources (visual tree, ContentControl.Content,
    // ItemsControl.Items, LogicalTreeHelper) because no single source is complete on its own - but for
    // most controls those sources aren't disjoint: a ContentControl's Content is normally reachable
    // BOTH as a visual descendant (through its template's ContentPresenter) AND directly via
    // ContentControl.Content/LogicalTreeHelper. Without deduping, that one shared node - and
    // everything under it - gets walked and materialized again as a sibling subtree at every level,
    // so the node count multiplies with tree depth instead of adding, which OOMs on any reasonably
    // deep real UI (AvalonDock + theme templates easily run 20-40 visual levels deep). This set makes
    // each DependencyObject appear exactly once per walk, keyed by reference, and doubles as a guard
    // against genuine reference cycles.
    private readonly HashSet<DependencyObject> _visited = new(ReferenceEqualityComparer.Instance);

    public List<ElementInfo> WalkTree()
    {
        _elementsByStableId.Clear();
        _visited.Clear();

        var app = Application.Current;
        if (app == null)
            return new List<ElementInfo>();

        var roots = new List<ElementInfo>();
        foreach (Window window in app.Windows.OfType<Window>())
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

    public DependencyObject? ResolveElementByStableId(string stableId)
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

    private ElementInfo BuildElementInfo(DependencyObject element, string? parentId)
    {
        var id = GetStableId(element);
        _elementsByStableId[id] = element;

        var info = new ElementInfo
        {
            Id = id,
            ParentId = parentId,
            Type = element.GetType().Name,
            FullType = element.GetType().FullName ?? element.GetType().Name,
            Framework = "wpf",
            AutomationId = GetAutomationId(element),
            Text = GetText(element),
            IsVisible = IsElementVisible(element),
            IsEnabled = GetIsEnabled(element),
            Bounds = ResolveBounds(element),
            NativeProperties = BuildNativeProperties(element, id),
            FrameworkProperties = BuildFrameworkProperties(element),
            Children = GetChildren(element)
                .Where(child => _visited.Add(child))
                .Select(child => BuildElementInfo(child, id))
                .ToList()
        };

        return info;
    }

    private string GetStableId(DependencyObject element)
    {
        return _stableIds.GetValue(element, static obj =>
        {
            if (obj is FrameworkElement fe && !string.IsNullOrEmpty(fe.Name))
                return fe.Name;
            return "_wpfdevflow_" + Guid.NewGuid().ToString("N").Substring(0, 12);
        });
    }

    private static string? GetAutomationId(DependencyObject element)
    {
        return element is FrameworkElement fe ? System.Windows.Automation.AutomationProperties.GetAutomationId(fe) : null;
    }

    private static string? GetText(DependencyObject element)
    {
        switch (element)
        {
            case TextBox textBox:
                return textBox.Text;
            case PasswordBox passwordBox:
                return passwordBox.Password;
            case TextBlock textBlock:
                return textBlock.Text;
            case ContentControl contentControl when contentControl.Content is string text:
                return text;
            case HeaderedItemsControl headered:
                return headered.Header?.ToString();
            default:
                return null;
        }
    }

    private static bool GetIsEnabled(DependencyObject element)
    {
        return element switch
        {
            UIElement ui => ui.IsEnabled,
            ContentElement ce => ce.IsEnabled,
            _ => true
        };
    }

    private static bool IsElementVisible(DependencyObject element)
    {
        return element switch
        {
            UIElement ui => ui.Visibility == Visibility.Visible,
            FrameworkContentElement fce => (Visibility)fce.GetValue(UIElement.VisibilityProperty) == Visibility.Visible,
            _ => true
        };
    }

    private static BoundsInfo? ResolveBounds(DependencyObject element)
    {
        try
        {
            if (element is Window wind)
            {
                return CreateIfFinite(wind.Left, wind.Top, wind.ActualWidth, wind.ActualHeight);
            }

            if (element is FrameworkElement fe && fe.IsVisible)
            {
                var window = Window.GetWindow(fe);
                if (window != null)
                {
                    var transform = fe.TransformToAncestor(window);
                    var point = transform.Transform(new System.Windows.Point(0, 0));
                    return CreateIfFinite(point.X, point.Y, fe.ActualWidth, fe.ActualHeight);
                }
            }
        }
        catch
        {
        }

        return null;
    }

    // A closing/mid-teardown window (e.g. an AvalonDock floating window mid-Close()) can
    // transiently report NaN/Infinity bounds (a degenerate zero-scale transform, or layout not
    // yet re-run after removal). System.Text.Json throws on those by default, which aborts the
    // whole ui/tree response mid-stream - so treat "not finite" the same as "no bounds available"
    // instead of ever serializing a non-finite number.
    private static BoundsInfo? CreateIfFinite(double x, double y, double width, double height)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(width) || !double.IsFinite(height))
            return null;

        return new BoundsInfo { X = x, Y = y, Width = width, Height = height };
    }

    private static Dictionary<string, string?> BuildNativeProperties(DependencyObject element, string stableId)
    {
        var props = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["nativeType"] = element.GetType().FullName,
            ["stableId"] = stableId
        };

        if (element is FrameworkElement fe)
        {
            if (!string.IsNullOrEmpty(fe.Name))
                props["name"] = fe.Name;

            var automationId = System.Windows.Automation.AutomationProperties.GetAutomationId(fe);
            if (!string.IsNullOrEmpty(automationId))
                props["automationId"] = automationId;
        }

        return props;
    }

    private static Dictionary<string, string?>? BuildFrameworkProperties(DependencyObject element)
    {
        var props = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        if (element is ScrollViewer scroll)
        {
            props["horizontalOffset"] = scroll.HorizontalOffset.ToString();
            props["verticalOffset"] = scroll.VerticalOffset.ToString();
            props["extentWidth"] = scroll.ExtentWidth.ToString();
            props["extentHeight"] = scroll.ExtentHeight.ToString();
        }

        if (element is Control control)
        {
            props["background"] = BrushToString(control.Background);
            props["foreground"] = BrushToString(control.Foreground);
            props["borderBrush"] = BrushToString(control.BorderBrush);
        }
        else if (element is System.Windows.Shapes.Shape shape)
        {
            props["fill"] = BrushToString(shape.Fill);
            props["stroke"] = BrushToString(shape.Stroke);
        }
        else if (element is Panel panel)
        {
            props["background"] = BrushToString(panel.Background);
        }
        else if (element is Border border)
        {
            props["background"] = BrushToString(border.Background);
            props["borderBrush"] = BrushToString(border.BorderBrush);
        }
        else if (element is TextBlock textBlock)
        {
            props["foreground"] = BrushToString(textBlock.Foreground);
            props["background"] = BrushToString(textBlock.Background);
        }

        foreach (var key in props.Keys.Where(k => props[k] == null).ToList())
            props.Remove(key);

        return props.Count > 0 ? props : null;
    }

    private static string? BrushToString(Brush? brush)
    {
        if (brush is SolidColorBrush solid)
        {
            var c = solid.Color;
            return c.A == 255
                ? $"#{c.R:X2}{c.G:X2}{c.B:X2}"
                : $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";
        }
        return brush?.GetType().Name;
    }

    private static IEnumerable<DependencyObject> GetChildren(DependencyObject element)
    {
        if (element == null)
            yield break;

        if (element is Visual || element is Visual3D)
        {
            var visualChildrenCount = VisualTreeHelper.GetChildrenCount(element);
            for (var i = 0; i < visualChildrenCount; i++)
            {
                yield return VisualTreeHelper.GetChild(element, i);
            }
        }

        if (element is ContentControl contentControl && contentControl.Content is DependencyObject content)
        {
            yield return content;
        }

        if (element is ItemsControl itemsControl)
        {
            foreach (var item in itemsControl.Items)
            {
                if (item is DependencyObject itemElement)
                    yield return itemElement;
            }
        }

        foreach (var child in LogicalTreeHelper.GetChildren(element).OfType<DependencyObject>())
        {
            yield return child;
        }
    }
}
