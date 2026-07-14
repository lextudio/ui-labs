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

    public List<ElementInfo> WalkTree()
    {
        _elementsByStableId.Clear();

        var app = Application.Current;
        if (app == null)
            return new List<ElementInfo>();

        var roots = new List<ElementInfo>();
        foreach (Window window in app.Windows.OfType<Window>())
        {
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
            Children = GetChildren(element).Select(child => BuildElementInfo(child, id)).ToList()
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
                return new BoundsInfo
                {
                    X = wind.Left,
                    Y = wind.Top,
                    Width = wind.ActualWidth,
                    Height = wind.ActualHeight
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
                        X = point.X,
                        Y = point.Y,
                        Width = fe.ActualWidth,
                        Height = fe.ActualHeight
                    };
                }
            }
        }
        catch
        {
        }

        return null;
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
