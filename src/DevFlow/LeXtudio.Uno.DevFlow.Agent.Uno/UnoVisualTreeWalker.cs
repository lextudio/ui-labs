using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using LeXtudio.Wpf.DevFlow.Agent.Core;
using Microsoft.Maui.DevFlow.Agent.Core;

namespace LeXtudio.Uno.DevFlow.Agent.Uno;

public sealed class UnoVisualTreeWalker : IVisualTreeWalker
{
    private readonly Type? _applicationType;
    private readonly Type? _visualTreeHelperType;

    public UnoVisualTreeWalker()
    {
        _applicationType = FindType(
            "Microsoft.UI.Xaml.Application",
            "Windows.UI.Xaml.Application");

        _visualTreeHelperType = FindType(
            "Microsoft.UI.Xaml.Media.VisualTreeHelper",
            "Windows.UI.Xaml.Media.VisualTreeHelper");
    }

    public List<ElementInfo> WalkTree()
    {
        var app = GetCurrentApplication();
        if (app == null)
            return new List<ElementInfo>();

        var windowRoots = GetWindows(app)
            .Select(GetWindowRoot)
            .Where(root => root != null)
            .Cast<object>()
            .ToList();

        if (windowRoots.Count == 0)
            return new List<ElementInfo>();

        var elements = new List<ElementInfo>();
        foreach (var windowRoot in windowRoots)
        {
            var windowInfo = CreateElementInfo(windowRoot, null);
            if (windowInfo != null)
                elements.Add(windowInfo);
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
        if (string.IsNullOrWhiteSpace(id))
            return null;

        var app = GetCurrentApplication();
        if (app == null)
            return null;

        foreach (var window in GetWindows(app))
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

    private object? FindElementObjectById(object element, string id)
    {
        if (string.Equals(GetElementId(element), id, StringComparison.OrdinalIgnoreCase))
            return element;

        foreach (var child in GetChildren(element))
        {
            var found = FindElementObjectById(child, id);
            if (found != null)
                return found;
        }

        return null;
    }

    private ElementInfo? FindElementById(ElementInfo element, string id)
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

    private object? GetCurrentApplication()
    {
        if (_applicationType == null)
            return null;

        return _applicationType.GetProperty("Current", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
    }

    private IEnumerable<object> GetWindows(object app)
    {
        var windowsProperty = app.GetType().GetProperty("Windows", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (windowsProperty != null)
        {
            var windowsValue = windowsProperty.GetValue(app);
            if (windowsValue is IEnumerable enumerable)
            {
                foreach (var window in enumerable)
                {
                    if (window != null)
                        yield return window;
                }
            }
        }

        var mainWindow = GetPropertyValue(app, "MainWindow")
            ?? GetPropertyValue(app, "CurrentWindow")
            ?? GetCurrentWindow();

        if (mainWindow != null)
            yield return mainWindow;
    }

    private object? GetWindowRoot(object window)
    {
        var content = GetPropertyValue(window, "Content");
        if (content != null)
            return content;

        return window;
    }

    private ElementInfo? CreateElementInfo(object element, string? parentId)
    {
        if (element == null)
            return null;

        var elementInfo = new ElementInfo
        {
            Id = GetElementId(element) ?? string.Empty,
            ParentId = parentId,
            Type = element.GetType().Name,
            FullType = element.GetType().FullName ?? string.Empty,
            Framework = "uno",
            AutomationId = GetElementId(element),
            Text = GetElementText(element),
            IsVisible = GetBoolProperty(element, "Visibility", true) && GetBoolProperty(element, "IsVisible", true),
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
            foreach (var child in children)
            {
                var childInfo = CreateElementInfo(child, elementInfo.Id);
                if (childInfo != null)
                    elementInfo.Children.Add(childInfo);
            }
        }

        return elementInfo;
    }

    private List<object> GetChildren(object element)
    {
        var children = GetChildrenFromVisualTreeHelper(element).ToList();
        if (children.Count > 0)
            return children;

        var content = GetPropertyValue(element, "Content");
        if (content != null && content is not string)
            children.Add(content);

        var items = GetPropertyValue(element, "Items") as IEnumerable;
        if (items != null)
        {
            foreach (var item in items)
            {
                if (item != null)
                    children.Add(item);
            }
        }

        var panelChildren = GetPropertyValue(element, "Children") as IEnumerable;
        if (panelChildren != null)
        {
            foreach (var child in panelChildren)
            {
                if (child != null)
                    children.Add(child);
            }
        }

        return children;
    }

    private IEnumerable<object> GetChildrenFromVisualTreeHelper(object element)
    {
        var children = new List<object>();
        if (_visualTreeHelperType == null)
            return children;

        var getChildrenCount = _visualTreeHelperType.GetMethod("GetChildrenCount", BindingFlags.Public | BindingFlags.Static);
        var getChild = _visualTreeHelperType.GetMethod("GetChild", BindingFlags.Public | BindingFlags.Static);
        if (getChildrenCount == null || getChild == null)
            return children;

        try
        {
            var count = (int)getChildrenCount.Invoke(null, new[] { element })!;
            for (var i = 0; i < count; i++)
            {
                var child = getChild.Invoke(null, new object[] { element, i });
                if (child != null)
                    children.Add(child);
            }
        }
        catch
        {
        }

        return children;
    }

    private string? GetElementId(object element)
    {
        var automationId = GetPropertyValue(element, "AutomationId") as string;
        if (string.IsNullOrWhiteSpace(automationId))
            automationId = GetAttachedAutomationId(element);

        if (!string.IsNullOrWhiteSpace(automationId))
            return automationId;

        var name = GetPropertyValue(element, "Name") as string;
        if (!string.IsNullOrWhiteSpace(name))
            return name;

        return null;
    }

    private string? GetAttachedAutomationId(object element)
    {
        var automationPropsType = FindType(
            "Microsoft.UI.Xaml.Automation.AutomationProperties",
            "Windows.UI.Xaml.Automation.AutomationProperties");

        if (automationPropsType == null)
            return null;

        var getAutomationId = automationPropsType.GetMethod("GetAutomationId", BindingFlags.Public | BindingFlags.Static);
        if (getAutomationId == null)
            return null;

        var value = getAutomationId.Invoke(null, new[] { element });
        return value as string;
    }

    private string? GetElementText(object element)
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

        return null;
    }

    private bool GetBoolProperty(object element, string propertyName, bool defaultValue)
    {
        var value = GetPropertyValue(element, propertyName);
        return value is bool boolValue ? boolValue : defaultValue;
    }

    private double GetDoubleProperty(object element, string propertyName, double defaultValue)
    {
        var value = GetPropertyValue(element, propertyName);
        return value is double doubleValue ? doubleValue : defaultValue;
    }

    private Dictionary<string, string?> GetFrameworkProperties(object element)
    {
        var properties = new Dictionary<string, string?>
        {
            ["automationId"] = GetPropertyValue(element, "AutomationId") as string,
            ["name"] = GetPropertyValue(element, "Name") as string,
        };

        if (IsScrollViewer(element))
        {
            properties["horizontalOffset"] = GetPropertyValue(element, "HorizontalOffset")?.ToString();
            properties["verticalOffset"] = GetPropertyValue(element, "VerticalOffset")?.ToString();
            properties["extentWidth"] = GetPropertyValue(element, "ExtentWidth")?.ToString();
            properties["extentHeight"] = GetPropertyValue(element, "ExtentHeight")?.ToString();
        }

        return properties;
    }

    private static bool IsScrollViewer(object element)
    {
        var type = element.GetType();
        return string.Equals(type.Name, "ScrollViewer", StringComparison.OrdinalIgnoreCase)
            || (type.FullName?.EndsWith("ScrollViewer", StringComparison.OrdinalIgnoreCase) ?? false);
    }

    private object? GetPropertyValue(object target, string propertyName)
    {
        if (target == null)
            return null;

        var property = target.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        return property?.GetValue(target);
    }

    private object? GetCurrentWindow()
    {
        var windowType = FindType(
            "Microsoft.UI.Xaml.Window",
            "Windows.UI.Xaml.Window");

        if (windowType == null)
            return null;

        var currentWindowProperty = windowType.GetProperty("Current", BindingFlags.Public | BindingFlags.Static);
        return currentWindowProperty?.GetValue(null);
    }

    private Type? FindType(params string[] typeNames)
    {
        foreach (var typeName in typeNames)
        {
            var type = Type.GetType(typeName, false, true);
            if (type != null)
                return type;

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                type = assembly.GetType(typeName, false, true);
                if (type != null)
                    return type;
            }
        }

        return null;
    }
}
