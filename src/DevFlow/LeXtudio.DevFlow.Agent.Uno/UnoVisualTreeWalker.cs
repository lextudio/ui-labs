using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using LeXtudio.DevFlow.Agent.Core;
using Microsoft.Maui.DevFlow.Agent.Core;

namespace LeXtudio.DevFlow.Agent.Uno;

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

        var windowRoots = new List<object>();
        foreach (var root in GetWindows(app).Select(GetWindowRoot).Where(root => root != null).Cast<object>())
        {
            if (!windowRoots.Any(existing => ReferenceEquals(existing, root)))
                windowRoots.Add(root);
        }

        if (windowRoots.Count == 0)
            return new List<ElementInfo>();

        var elements = new List<ElementInfo>();
        var idCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var windowRoot in windowRoots)
        {
            var windowInfo = CreateElementInfo(windowRoot, null, windowRoot, idCounts);
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

        var requestedId = SplitOccurrenceId(id, out var requestedOccurrence);
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var roots = new List<object>();
        foreach (var window in GetWindows(app))
        {
            var root = GetWindowRoot(window);
            if (root == null)
                continue;
            if (roots.Any(existing => ReferenceEquals(existing, root)))
                continue;
            roots.Add(root);

            var found = FindElementObjectById(root, requestedId, requestedOccurrence, counts);
            if (found != null)
                return found;
        }

        return null;
    }

    public object? FindRootElementObject()
    {
        var app = GetCurrentApplication();
        if (app == null)
            return null;

        return GetWindows(app)
            .Select(GetWindowRoot)
            .FirstOrDefault(root => root != null);
    }

    private object? FindElementObjectById(
        object element,
        string id,
        int requestedOccurrence,
        Dictionary<string, int> counts)
    {
        var elementId = GetElementId(element);
        if (string.Equals(elementId, id, StringComparison.OrdinalIgnoreCase))
        {
            counts.TryGetValue(id, out var count);
            count++;
            counts[id] = count;
            if (count == requestedOccurrence)
                return element;
        }

        foreach (var child in GetChildren(element))
        {
            var found = FindElementObjectById(child, id, requestedOccurrence, counts);
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

    // All distinct open windows (main + floating child windows), de-duplicated.
    public IReadOnlyList<object> GetAllWindows()
    {
        var app = GetCurrentApplication();
        if (app == null)
            return Array.Empty<object>();

        var list = new List<object>();
        foreach (var window in GetWindows(app))
        {
            if (window != null && !list.Any(w => ReferenceEquals(w, window)))
                list.Add(window);
        }
        return list;
    }

    // The content root of a window (its Content, or the window itself as a fallback).
    public object? GetWindowContentRoot(object window) => GetWindowRoot(window);

    private IEnumerable<object> GetWindows(object app)
    {
        // Primary source on Uno: ApplicationHelper.Windows (static) tracks EVERY open window,
        // including floating child windows. WinUI3 dropped UWP's Application.Windows, so the
        // per-app instance property below usually finds nothing — this static list is what
        // surfaces floating windows for multi-window screenshots.
        foreach (var window in GetWindowsFromApplicationHelper())
            yield return window;

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

    private IEnumerable<object> GetWindowsFromApplicationHelper()
    {
        var helperType = FindType(
            "Uno.UI.ApplicationHelper",
            "Microsoft.UI.Xaml.ApplicationHelper",
            "Windows.UI.Xaml.ApplicationHelper");
        var windowsValue = helperType?
            .GetProperty("Windows", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)?
            .GetValue(null);
        if (windowsValue is IEnumerable enumerable)
        {
            foreach (var window in enumerable)
            {
                if (window != null)
                    yield return window;
            }
        }
    }

    private object? GetWindowRoot(object window)
    {
        var content = GetPropertyValue(window, "Content");
        if (content != null)
            return content;

        return window;
    }

    private ElementInfo? CreateElementInfo(
        object element,
        string? parentId,
        object? windowRoot,
        Dictionary<string, int> idCounts)
    {
        if (element == null)
            return null;

        // Build the element info defensively. Uno's partially-implemented controls (most notably
        // WebView2 on net10.0-desktop) throw NotImplementedException from many properties; a
        // single bad property must not abort the entire walk. Each helper below already
        // tolerates exceptions, but we still wrap the whole thing in case a future getter throws
        // from somewhere we don't expect.
        ElementInfo elementInfo;
        try
        {
            var rawId = SafeGet(() => GetElementId(element)) ?? string.Empty;
            var elementId = MakeOccurrenceId(rawId, idCounts);
            elementInfo = new ElementInfo
            {
                Id = elementId,
                ParentId = parentId,
                Type = element.GetType().Name,
                FullType = element.GetType().FullName ?? string.Empty,
                Framework = "uno",
                AutomationId = rawId,
                Text = SafeGet(() => GetElementText(element)),
                IsVisible = SafeGet(() => GetBoolProperty(element, "Visibility", true) && GetBoolProperty(element, "IsVisible", true), true),
                IsEnabled = SafeGet(() => GetBoolProperty(element, "IsEnabled", true), true),
                IsFocused = SafeGet(() => GetBoolProperty(element, "IsFocused", false), false),
                Opacity = SafeGet(() => GetDoubleProperty(element, "Opacity", 1.0), 1.0),
                NativeType = element.GetType().FullName,
                FrameworkProperties = SafeGet(() => GetFrameworkProperties(element)) ?? new Dictionary<string, string?>(),
                Bounds = SafeGet(() => GetElementBounds(element, windowRoot)),
            };
        }
        catch
        {
            // Element couldn't be described — emit a stub entry so the parent's child list stays
            // consistent and the walker continues.
            elementInfo = new ElementInfo
            {
                Id = string.Empty,
                ParentId = parentId,
                Type = SafeGet(() => element.GetType().Name) ?? "Unknown",
                FullType = SafeGet(() => element.GetType().FullName) ?? string.Empty,
                Framework = "uno",
                IsVisible = true,
                IsEnabled = true,
                Opacity = 1.0,
                FrameworkProperties = new Dictionary<string, string?>()
            };
        }

        List<object> children;
        try { children = GetChildren(element); }
        catch { children = new List<object>(); }

        if (children.Count > 0)
        {
            elementInfo.Children = new List<ElementInfo>();
            foreach (var child in children)
            {
                ElementInfo? childInfo = null;
                try { childInfo = CreateElementInfo(child, elementInfo.Id, windowRoot, idCounts); }
                catch { /* skip the child, keep walking siblings */ }
                if (childInfo != null)
                    elementInfo.Children.Add(childInfo);
            }
        }

        return elementInfo;
    }

    private static string MakeOccurrenceId(string rawId, Dictionary<string, int> idCounts)
    {
        if (string.IsNullOrWhiteSpace(rawId))
            return string.Empty;

        idCounts.TryGetValue(rawId, out var count);
        count++;
        idCounts[rawId] = count;
        return count == 1 ? rawId : $"{rawId}#{count}";
    }

    private static string SplitOccurrenceId(string id, out int occurrence)
    {
        occurrence = 1;
        var hashIndex = id.LastIndexOf('#');
        if (hashIndex <= 0 || hashIndex == id.Length - 1)
            return id;

        if (int.TryParse(id[(hashIndex + 1)..], out var parsed) && parsed > 0)
        {
            occurrence = parsed;
            return id[..hashIndex];
        }

        return id;
    }

    private static T? SafeGet<T>(Func<T?> getter) where T : class
    {
        try { return getter(); }
        catch { return null; }
    }

    private static T SafeGet<T>(Func<T> getter, T fallback) where T : struct
    {
        try { return getter(); }
        catch { return fallback; }
    }

    // Bounds in window-content DIP coordinates (top-left origin, matches
    // screenshot pixel coordinates at scale 1). Uses TransformToVisual(root)
    // so every element reports its origin relative to the content root.
    private static BoundsInfo? GetElementBounds(object element, object? root)
    {
        if (root == null) return null;

        var transformToVisual = element.GetType()
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(m => m.Name == "TransformToVisual" && m.GetParameters().Length == 1);
        if (transformToVisual == null) return null;

        var transform = transformToVisual.Invoke(element, new[] { root });
        if (transform == null) return null;

        var w = GetDoubleProperty(element, "ActualWidth");
        var h = GetDoubleProperty(element, "ActualHeight");
        if (w is null or <= 0 || h is null or <= 0) return null;

        var pointType = FindType2("Windows.Foundation.Point");
        if (pointType == null) return null;

        var origin = Activator.CreateInstance(pointType, 0.0, 0.0);
        var transformPoint = transform.GetType().GetMethod("TransformPoint", BindingFlags.Public | BindingFlags.Instance);
        var transformed = transformPoint?.Invoke(transform, new[] { origin });
        if (transformed == null) return null;

        var x = GetDoubleProperty(transformed, "X");
        var y = GetDoubleProperty(transformed, "Y");
        if (x is null || y is null) return null;

        return new BoundsInfo { X = x.Value, Y = y.Value, Width = w.Value, Height = h.Value };
    }

    private static double? GetDoubleProperty(object? target, string name)
    {
        if (target == null) return null;
        var val = target.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(target);
        return val switch { double d => d, float f => f, int i => i, long l => l, _ => null };
    }

    private static Type? FindType2(params string[] names)
    {
        foreach (var n in names)
        {
            var t = System.AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType(n)).FirstOrDefault(x => x != null);
            if (t != null) return t;
        }
        return null;
    }

    private List<object> GetChildren(object element)
    {
        // Collect from all discovery paths and deduplicate by reference.
        // Previously short-circuited after VisualTreeHelper — this missed
        // extra Panel.Children that the visual tree helper didn't expose yet
        // (e.g. Uno Grid children that are added dynamically and haven't
        // completed a layout pass when the tree is captured).
        var seen = new System.Collections.Generic.HashSet<object>(ReferenceEqualityComparer.Instance);
        var children = new List<object>();

        foreach (var c in GetChildrenFromVisualTreeHelper(element))
        {
            if (seen.Add(c)) children.Add(c);
        }

        // For Panel types: also walk the Children collection directly.
        // This catches grid children that VisualTreeHelper may return fewer
        // of (e.g. when column/row definitions collapse some of them to 0×0).
        var panelChildren = GetPropertyValue(element, "Children") as IEnumerable;
        if (panelChildren != null)
        {
            foreach (var child in panelChildren)
            {
                if (child != null && seen.Add(child))
                    children.Add(child);
            }
        }

        // If still empty, fall back to Content / Items.
        if (children.Count == 0)
        {
            var content = GetPropertyValue(element, "Content");
            if (content != null && content is not string && seen.Add(content))
                children.Add(content);

            var items = GetPropertyValue(element, "Items") as IEnumerable;
            if (items != null)
            {
                foreach (var item in items)
                {
                    if (item != null && seen.Add(item))
                        children.Add(item);
                }
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

        try
        {
            var value = getAutomationId.Invoke(null, new[] { element });
            return value as string;
        }
        catch
        {
            return null;
        }
    }

    private string? GetElementText(object element)
    {
        var text = GetPropertyValue(element, "Text") as string;
        if (text != null)
            return text;

        var content = GetPropertyValue(element, "Content");
        if (content is string contentString)
            return contentString;

        var header = GetPropertyValue(element, "Header") as string;
        if (header != null)
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

        foreach (var brushProp in s_brushPropertyNames)
        {
            var brush = GetPropertyValue(element, brushProp);
            if (brush != null)
                properties[char.ToLowerInvariant(brushProp[0]) + brushProp[1..]] = BrushToString(brush);
        }

        var requestedTheme = GetPropertyValue(element, "RequestedTheme");
        if (requestedTheme != null)
            properties["requestedTheme"] = requestedTheme.ToString();

        var actualTheme = GetPropertyValue(element, "ActualTheme");
        if (actualTheme != null)
            properties["actualTheme"] = actualTheme.ToString();

        return properties;
    }

    private static readonly string[] s_brushPropertyNames =
    [
        "Background",
        "Foreground",
        "BorderBrush",
        "Fill",
        "Stroke",
    ];

    private static string? BrushToString(object brush)
    {
        // SolidColorBrush: read the Color property and format as #AARRGGBB / #RRGGBB
        var colorProp = brush.GetType().GetProperty("Color", BindingFlags.Public | BindingFlags.Instance);
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
        var prop = color.GetType().GetProperty(channel, BindingFlags.Public | BindingFlags.Instance);
        if (prop == null) return null;
        var val = prop.GetValue(color);
        return val is byte b ? b : null;
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
        if (property == null)
            return null;

        try
        {
            return property.GetValue(target);
        }
        catch
        {
            // Uno's WebView2 (and other partially-implemented controls) throw NotImplementedException
            // for many properties. Swallow so the visual-tree walk can continue past these elements.
            return null;
        }
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
