using System;
using System.Collections.Generic;
using System.Linq;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using LeXtudio.DevFlow.Agent.Core;
using Microsoft.Maui.DevFlow.Agent.Core;

namespace LeXtudio.DevFlow.Agent.MewUI;

public sealed class MewUIVisualTreeWalker : IVisualTreeWalker
{
    public List<ElementInfo> WalkTree()
    {
        if (!Application.IsRunning)
            return new List<ElementInfo>();

        var roots = Application.Current.AllWindows
            .Select(GetWindowRoot)
            .Where(root => root != null)
            .ToList();

        var elements = new List<ElementInfo>();
        for (var index = 0; index < roots.Count; index++)
        {
            var root = roots[index]!;
            var rootInfo = CreateElementInfo(root, null, index);
            if (rootInfo != null)
                elements.Add(rootInfo);
        }

        return elements;
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
        if (string.IsNullOrWhiteSpace(id) || !Application.IsRunning)
            return null;

        foreach (var window in Application.Current.AllWindows)
        {
            var root = GetWindowRoot(window);
            if (root == null)
                continue;

            var found = FindElementObjectById(root, id);
            if (found != null)
                return found;
        }

        return null;
    }

    private static object? GetWindowRoot(Window window)
    {
        return window.Content ?? window;
    }

    private ElementInfo? CreateElementInfo(object element, string? parentId, int siblingIndex)
    {
        if (element == null)
            return null;

        var id = GetElementId(element) ?? CreateGeneratedId(parentId, element.GetType().Name, siblingIndex);
        var elementInfo = new ElementInfo
        {
            Id = id,
            ParentId = parentId,
            Type = element.GetType().Name,
            FullType = element.GetType().FullName ?? string.Empty,
            Framework = "mewui",
            AutomationId = GetElementId(element),
            Text = GetElementText(element),
            IsVisible = GetBoolProperty(element, "IsVisible", true),
            IsEnabled = GetBoolProperty(element, "IsEnabled", true),
            IsFocused = GetBoolProperty(element, "IsFocused", false),
            Opacity = GetDoubleProperty(element, "Opacity", 1.0),
            NativeType = element.GetType().FullName,
            FrameworkProperties = GetFrameworkProperties(element)
        };

        var children = GetChildren(element);
        if (children.Count > 0)
        {
            elementInfo.Children = new List<ElementInfo>();
            for (var index = 0; index < children.Count; index++)
            {
                var child = children[index];
                var childInfo = CreateElementInfo(child, elementInfo.Id, index);
                if (childInfo != null)
                    elementInfo.Children.Add(childInfo);
            }
        }

        return elementInfo;
    }

    private static object? FindElementObjectById(object element, string id)
    {
        if (string.Equals(GetElementId(element) ?? string.Empty, id, StringComparison.OrdinalIgnoreCase))
            return element;

        foreach (var child in GetChildren(element))
        {
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

    private static IList<object> GetChildren(object element)
    {
        if (element is IVisualTreeHost host)
        {
            var list = new List<object>();
            host.VisitChildren(child =>
            {
                if (child != null)
                    list.Add(child);
                return true;
            });
            return list;
        }

        return Array.Empty<object>();
    }

    private static string? GetElementId(object element)
    {
        var tag = GetPropertyValue(element, "Tag") as string;
        if (!string.IsNullOrWhiteSpace(tag))
            return tag;

        var name = GetPropertyValue(element, "Name") as string;
        if (!string.IsNullOrWhiteSpace(name))
            return name;

        return null;
    }

    private static string GetElementText(object element)
    {
        var text = GetPropertyValue(element, "Text") as string;
        if (!string.IsNullOrWhiteSpace(text))
            return text;

        var content = GetPropertyValue(element, "Content");
        if (content is string contentString && !string.IsNullOrWhiteSpace(contentString))
            return contentString;

        var header = GetPropertyValue(element, "Header") as string;
        if (!string.IsNullOrWhiteSpace(header))
            return header;

        return string.Empty;
    }

    private static bool GetBoolProperty(object element, string propertyName, bool defaultValue)
    {
        var value = GetPropertyValue(element, propertyName);
        return value is bool boolValue ? boolValue : defaultValue;
    }

    private static double GetDoubleProperty(object element, string propertyName, double defaultValue)
    {
        var value = GetPropertyValue(element, propertyName);
        return value is double doubleValue ? doubleValue : defaultValue;
    }

    private static Dictionary<string, string?> GetFrameworkProperties(object element)
    {
        return new Dictionary<string, string?>
        {
            ["tag"] = GetPropertyValue(element, "Tag") as string,
            ["name"] = GetPropertyValue(element, "Name") as string,
        };
    }

    private static string CreateGeneratedId(string? parentId, string typeName, int siblingIndex)
    {
        return parentId == null ? $"{typeName}[{siblingIndex}]" : $"{parentId}/{typeName}[{siblingIndex}]";
    }

    private static object? GetPropertyValue(object target, string propertyName)
    {
        if (target == null)
            return null;

        var property = target.GetType().GetProperty(propertyName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        return property?.GetValue(target);
    }
}
