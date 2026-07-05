using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using Microsoft.Maui.DevFlow.Agent.Core;
using LeXtudio.DevFlow.Agent.Core;

namespace LeXtudio.DevFlow.Agent.WPF;

public class WpfVisualTreeWalker : IVisualTreeWalker
{
    private const int MaxDepth = 48;
    private const int MaxNodes = 1000;
    private readonly ConditionalWeakTable<DependencyObject, string> _stableIds = new();
    private readonly Dictionary<string, DependencyObject> _elementsByStableId = new(StringComparer.Ordinal);
    private readonly HashSet<DependencyObject> _visited = new(ReferenceEqualityComparer.Instance);
    private int _nodeCount;

    public List<ElementInfo> WalkTree()
    {
        _elementsByStableId.Clear();
        _visited.Clear();
        _nodeCount = 0;

        var app = Application.Current;
        if (app == null)
            return new List<ElementInfo>();

        var roots = new List<ElementInfo>();
        foreach (Window window in app.Windows.OfType<Window>())
        {
            var info = BuildElementInfo(window, null, depth: 0);
            if (info != null)
                roots.Add(info);
        }
        return roots;
    }

    public List<ElementInfo> QueryElements(string? type = null, string? automationId = null, string? text = null, int maxResults = 50, int maxDepth = 24)
    {
        _elementsByStableId.Clear();
        _visited.Clear();
        _nodeCount = 0;

        var app = Application.Current;
        if (app == null)
            return new List<ElementInfo>();

        var results = new List<ElementInfo>();
        foreach (Window window in app.Windows.OfType<Window>())
        {
            QueryElement(window, parentId: null, depth: 0, type, automationId, text, results, maxResults, maxDepth);
            if (results.Count >= maxResults || _nodeCount >= MaxNodes)
                break;
        }

        return results;
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

    private ElementInfo? BuildElementInfo(DependencyObject element, string? parentId, int depth)
    {
        if (depth > MaxDepth || _nodeCount >= MaxNodes || !_visited.Add(element))
            return null;

        _nodeCount++;
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
                .Select(child => BuildElementInfo(child, id, depth + 1))
                .Where(child => child != null)
                .Cast<ElementInfo>()
                .ToList()
        };

        return info;
    }

    private void QueryElement(
        DependencyObject element,
        string? parentId,
        int depth,
        string? type,
        string? automationId,
        string? text,
        List<ElementInfo> results,
        int maxResults,
        int maxDepth)
    {
        if (depth > Math.Min(MaxDepth, maxDepth) || _nodeCount >= MaxNodes || results.Count >= maxResults || !_visited.Add(element))
            return;

        _nodeCount++;
        var id = GetStableId(element);
        _elementsByStableId[id] = element;

        var elementType = element.GetType();
        var textValue = GetText(element);
        var automationIdValue = GetAutomationId(element);
        if (MatchesQuery(elementType, automationIdValue, textValue, type, automationId, text))
        {
            results.Add(new ElementInfo
            {
                Id = id,
                ParentId = parentId,
                Type = elementType.Name,
                FullType = elementType.FullName ?? elementType.Name,
                Framework = "wpf",
                AutomationId = automationIdValue,
                Text = textValue,
                IsVisible = IsElementVisible(element),
                IsEnabled = GetIsEnabled(element),
                Bounds = ResolveBounds(element),
                NativeProperties = BuildNativeProperties(element, id),
                Children = new List<ElementInfo>()
            });

            if (results.Count >= maxResults)
                return;
        }

        foreach (var child in GetChildren(element))
            QueryElement(child, id, depth + 1, type, automationId, text, results, maxResults, maxDepth);
    }

    private static bool MatchesQuery(
        Type elementType,
        string? automationIdValue,
        string? textValue,
        string? type,
        string? automationId,
        string? text)
    {
        return (string.IsNullOrWhiteSpace(type) || TypeMatches(elementType, type))
            && (string.IsNullOrWhiteSpace(automationId) || string.Equals(automationIdValue, automationId, StringComparison.OrdinalIgnoreCase))
            && (string.IsNullOrWhiteSpace(text) || textValue?.Contains(text, StringComparison.OrdinalIgnoreCase) == true);
    }

    private static bool TypeMatches(Type elementType, string type)
    {
        return string.Equals(elementType.Name, type, StringComparison.OrdinalIgnoreCase)
            || string.Equals(elementType.FullName, type, StringComparison.OrdinalIgnoreCase)
            || (elementType.FullName?.EndsWith("." + type, StringComparison.OrdinalIgnoreCase) == true);
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
                // An unshown/unpositioned WPF Window reports Left/Top as NaN, which
                // System.Text.Json cannot serialize; Finite() maps those to 0.
                return new BoundsInfo
                {
                    X = Finite(wind.Left),
                    Y = Finite(wind.Top),
                    Width = Finite(wind.ActualWidth),
                    Height = Finite(wind.ActualHeight)
                };
            }

            if (element is FrameworkElement fe && fe.IsVisible)
            {
                var window = Window.GetWindow(fe);
                if (window != null)
                {
                    var transform = fe.TransformToAncestor(window);
                    var point = transform.Transform(new System.Windows.Point(0, 0));
                    return new BoundsInfo
                    {
                        X = Finite(point.X),
                        Y = Finite(point.Y),
                        Width = Finite(fe.ActualWidth),
                        Height = Finite(fe.ActualHeight)
                    };
                }
            }
        }
        catch
        {
        }

        return null;
    }

    // WPF layout doubles are routinely NaN (unset) or Infinity (unconstrained Max*),
    // neither of which is valid JSON under the default serializer options.
    private static double Finite(double value) => double.IsFinite(value) ? value : 0d;

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

        // Remove null/absent entries to keep the payload lean
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

        if (IsAutomationLeaf(element))
            yield break;

        var seen = new HashSet<DependencyObject>(ReferenceEqualityComparer.Instance);

        if (element is Visual || element is Visual3D)
        {
            var visualChildrenCount = VisualTreeHelper.GetChildrenCount(element);
            for (var i = 0; i < visualChildrenCount; i++)
            {
                var child = VisualTreeHelper.GetChild(element, i);
                if (seen.Add(child))
                    yield return child;
            }
        }

        if (element is ContentControl contentControl && contentControl.Content is DependencyObject content)
        {
            if (seen.Add(content))
                yield return content;
        }

        if (element is ItemsControl itemsControl)
        {
            foreach (var item in itemsControl.Items)
            {
                if (item is DependencyObject itemElement && seen.Add(itemElement))
                    yield return itemElement;
            }
        }

        foreach (var child in LogicalTreeHelper.GetChildren(element).OfType<DependencyObject>())
        {
            if (seen.Add(child))
                yield return child;
        }
    }

    private static bool IsAutomationLeaf(DependencyObject element)
    {
        return element is TextBlock
            or TextBox
            or PasswordBox
            or Image
            or System.Windows.Shapes.Shape
            or Adorner;
    }

    private sealed class ReferenceEqualityComparer : IEqualityComparer<DependencyObject>
    {
        public static ReferenceEqualityComparer Instance { get; } = new();

        public bool Equals(DependencyObject? x, DependencyObject? y) => ReferenceEquals(x, y);

        public int GetHashCode(DependencyObject obj) => RuntimeHelpers.GetHashCode(obj);
    }
}
