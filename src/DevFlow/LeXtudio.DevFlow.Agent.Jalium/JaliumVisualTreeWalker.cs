using System;
using System.Collections.Generic;
using Jalium.UI;
using Jalium.UI.Automation;
using Jalium.UI.Controls;
using LeXtudio.DevFlow.Agent.Core;
using Microsoft.Maui.DevFlow.Agent.Core;

namespace LeXtudio.DevFlow.Agent.Jalium;

public sealed class JaliumVisualTreeWalker : IVisualTreeWalker
{
    public List<ElementInfo> WalkTree()
    {
        if (Application.Current == null)
            return new List<ElementInfo>();

        var result = new List<ElementInfo>();
        var window = Application.Current.MainWindow;
        if (window != null)
        {
            var info = CreateElementInfo(window, null, 0);
            if (info != null)
                result.Add(info);
        }

        return result;
    }

    public ElementInfo? FindElementById(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return null;

        foreach (var root in WalkTree())
        {
            var found = FindElementById(root, id);
            if (found != null)
                return found;
        }

        return null;
    }

    public object? FindElementObjectById(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || Application.Current == null)
            return null;

        var window = Application.Current.MainWindow;
        if (window == null)
            return null;

        return FindElementObjectById(window, id);
    }

    private ElementInfo? CreateElementInfo(object element, string? parentId, int siblingIndex)
    {
        if (element == null)
            return null;

        var id = GetElementId(element) ?? CreateGeneratedId(parentId, element.GetType().Name, siblingIndex);
        var isVisible = element is UIElement ui ? ui.Visibility == Visibility.Visible : true;
        var isEnabled = element is UIElement uie ? uie.IsEnabled : true;
        var isFocused = element is UIElement uif ? uif.IsFocused : false;
        var opacity = element is UIElement uio ? uio.Opacity : 1.0;

        var elementInfo = new ElementInfo
        {
            Id = id,
            ParentId = parentId,
            Type = element.GetType().Name,
            FullType = element.GetType().FullName ?? string.Empty,
            Framework = "jalium",
            AutomationId = element is DependencyObject dep ? AutomationProperties.GetAutomationId(dep) : null,
            Text = GetElementText(element),
            IsVisible = isVisible,
            IsEnabled = isEnabled,
            IsFocused = isFocused,
            Opacity = opacity,
            NativeType = element.GetType().FullName,
            Bounds = ResolveBounds(element),
            BoundsQuality = "exact",
            FrameworkProperties = GetFrameworkProperties(element)
        };

        var childCount = VisualTreeHelper.GetChildrenCount(element is DependencyObject d ? d : null!);
        if (childCount > 0)
        {
            elementInfo.Children = new List<ElementInfo>();
            for (var i = 0; i < childCount; i++)
            {
                var child = VisualTreeHelper.GetChild(element is DependencyObject dc ? dc : null!, i);
                if (child == null) continue;
                var childInfo = CreateElementInfo(child, elementInfo.Id, i);
                if (childInfo != null)
                    elementInfo.Children.Add(childInfo);
            }
        }

        return elementInfo;
    }

    private static object? FindElementObjectById(object element, string id)
    {
        if (string.Equals(GetElementId(element), id, StringComparison.OrdinalIgnoreCase))
            return element;

        if (element is not DependencyObject dep)
            return null;

        var count = VisualTreeHelper.GetChildrenCount(dep);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(dep, i);
            if (child == null) continue;
            var found = FindElementObjectById(child, id);
            if (found != null)
                return found;
        }

        return null;
    }

    private static ElementInfo? FindElementById(ElementInfo element, string id)
    {
        if (string.Equals(element.Id, id, StringComparison.OrdinalIgnoreCase))
            return element;

        if (element.Children != null)
        {
            foreach (var child in element.Children)
            {
                var found = FindElementById(child, id);
                if (found != null)
                    return found;
            }
        }

        return null;
    }

    private static string? GetElementId(object element)
    {
        if (element is DependencyObject dep)
        {
            var automationId = AutomationProperties.GetAutomationId(dep);
            if (!string.IsNullOrWhiteSpace(automationId))
                return automationId;
        }

        if (element is FrameworkElement fe)
        {
            if (!string.IsNullOrWhiteSpace(fe.Name))
                return fe.Name;

            var tag = fe.Tag as string;
            if (!string.IsNullOrWhiteSpace(tag))
                return tag;
        }

        return null;
    }

    /// <summary>
    /// Measures an element against the window that contains it.
    /// </summary>
    /// <remarks>
    /// A framework element's own origin is relative to its parent, so the transform to the window is what
    /// places it in the frame the shared revision hash and the layout diagnostics compare against. Null when
    /// the element is not laid out yet or has been detached.
    /// </remarks>
    private static BoundsInfo? ResolveBounds(object element)
    {
        try
        {
            if (element is not FrameworkElement fe || fe.ActualWidth <= 0 || fe.ActualHeight <= 0)
            {
                return null;
            }

            // Jalium has no IsVisible on FrameworkElement; UIElement carries Visibility instead, and
            // the walker's own IsVisible already reads it.
            var window = Application.Current?.MainWindow;
            if (window is null || (fe is UIElement ui && ui.Visibility != Visibility.Visible))
            {
                return null;
            }

            // TransformToVisual, not TransformToAncestor: Jalium exposes the former, and it is what the
            // tap and screenshot paths already use to place an element, so the reported bounds and the
            // point a synthetic click lands on cannot drift apart.
            var transform = fe.TransformToVisual(window);
            if (transform is null)
            {
                return null;
            }

            var origin = transform.Transform(new Point(0, 0));
            if (!double.IsFinite(origin.X) || !double.IsFinite(origin.Y))
            {
                return null;
            }

            return new BoundsInfo
            {
                X = origin.X,
                Y = origin.Y,
                Width = fe.ActualWidth,
                Height = fe.ActualHeight,
            };
        }
        catch (InvalidOperationException)
        {
            // The element was detached mid-walk; report it without geometry rather than failing the tree.
            return null;
        }
    }

    private static string GetElementText(object element)
    {
        var text = GetStringProperty(element, "Text");
        if (!string.IsNullOrWhiteSpace(text)) return text;

        var content = GetPropertyValue(element, "Content");
        if (content is string s && !string.IsNullOrWhiteSpace(s)) return s;

        var header = GetStringProperty(element, "Header");
        if (!string.IsNullOrWhiteSpace(header)) return header;

        return string.Empty;
    }

    private static readonly string[] s_brushPropertyNames =
    [
        "Background", "Foreground", "BorderBrush", "Fill", "Stroke"
    ];

    private static Dictionary<string, string?> GetFrameworkProperties(object element)
    {
        var props = new Dictionary<string, string?>();

        if (element is FrameworkElement fe)
        {
            props["name"] = string.IsNullOrWhiteSpace(fe.Name) ? null : fe.Name;
            props["tag"] = fe.Tag as string;
        }

        if (element is DependencyObject dep)
        {
            var aid = AutomationProperties.GetAutomationId(dep);
            if (!string.IsNullOrWhiteSpace(aid))
                props["automationId"] = aid;
        }

        foreach (var brushProp in s_brushPropertyNames)
        {
            var brush = GetPropertyValue(element, brushProp);
            if (brush != null)
                props[char.ToLowerInvariant(brushProp[0]) + brushProp[1..]] = BrushToString(brush);
        }

        return props;
    }

    private static string? BrushToString(object brush)
    {
        var colorProp = brush.GetType().GetProperty("Color", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        if (colorProp != null)
        {
            var color = colorProp.GetValue(brush);
            if (color != null)
            {
                var a = GetColorChannel(color, "A");
                var r = GetColorChannel(color, "R");
                var g = GetColorChannel(color, "G");
                var b = GetColorChannel(color, "B");
                if (a.HasValue && r.HasValue && g.HasValue && b.HasValue)
                {
                    return a.Value == 255
                        ? $"#{r.Value:X2}{g.Value:X2}{b.Value:X2}"
                        : $"#{a.Value:X2}{r.Value:X2}{g.Value:X2}{b.Value:X2}";
                }
            }
        }

        return brush.GetType().Name;
    }

    private static byte? GetColorChannel(object color, string channel)
    {
        var prop = color.GetType().GetProperty(channel, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        if (prop == null) return null;
        var val = prop.GetValue(color);
        return val is byte b ? b : null;
    }

    private static string CreateGeneratedId(string? parentId, string typeName, int siblingIndex)
        => parentId == null ? $"{typeName}[{siblingIndex}]" : $"{parentId}/{typeName}[{siblingIndex}]";

    private static string? GetStringProperty(object target, string name)
        => GetPropertyValue(target, name) as string;

    private static object? GetPropertyValue(object target, string name)
    {
        var prop = target.GetType().GetProperty(name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        return prop?.GetValue(target);
    }
}
