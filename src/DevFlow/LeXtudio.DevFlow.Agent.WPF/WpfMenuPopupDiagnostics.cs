using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using LeXtudio.DevFlow.Agent.Core;

namespace LeXtudio.DevFlow.Agent.WPF;

[DevFlowUIThread]
public static class WpfMenuPopupDiagnostics
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        MaxDepth = 128,
        // WPF layout values (DesiredSize, RenderSize, etc.) are legitimately Infinity/NaN before an
        // element is measured/arranged - which is common for the overlay-hosted popup content this
        // diagnostic exists to inspect. Without this, serializing any such value throws and the whole
        // diagnostic call fails instead of reporting the (informative) unmeasured state.
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals
    };

    [DevFlowAction("wpf.menu-popup-diagnostics", Description = "Capture WPF menu, popup, DPI, monitor, mouse hit-test, and LibreWPF popup log diagnostics.")]
    public static string Capture(int maxLogLines = 300, int maxVisualDepth = 32)
    {
        maxLogLines = Math.Clamp(maxLogLines, 0, 5000);
        maxVisualDepth = Math.Clamp(maxVisualDepth, 1, 96);

        var app = Application.Current;
        var payload = new
        {
            capturedAtLocal = DateTimeOffset.Now.ToString("O"),
            capturedAtUtc = DateTimeOffset.UtcNow.ToString("O"),
            process = DescribeProcess(),
            application = app == null ? null : DescribeApplication(app),
            environment = DescribeEnvironment(),
            progpuMonitors = TryDescribeProGpuMonitors(),
            mouse = DescribeMouse(app),
            windows = DescribeWindows(app, maxVisualDepth),
            logs = DescribeLogs(maxLogLines)
        };

        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    private static object DescribeProcess()
    {
        using var process = Process.GetCurrentProcess();
        return new
        {
            id = process.Id,
            name = process.ProcessName,
            framework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            os = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            baseDirectory = AppContext.BaseDirectory
        };
    }

    private static object DescribeApplication(Application app)
        => new
        {
            type = DescribeType(app),
            mainWindow = app.MainWindow == null ? null : DescribeWindowIdentity(app.MainWindow),
            windowCount = app.Windows.Count
        };

    private static object DescribeEnvironment()
    {
        string? Env(string name) => Environment.GetEnvironmentVariable(name);

        return new
        {
            DEVFLOW_DISABLE = Env("DEVFLOW_DISABLE"),
            LIBREWPF_MENU_INPUT_LOG = Env("LIBREWPF_MENU_INPUT_LOG"),
            LIBREWPF_WINDOW_MOVE_LOG = Env("LIBREWPF_WINDOW_MOVE_LOG"),
            PROGPU_WPF_TRACE_POPUP_POSITION = Env("PROGPU_WPF_TRACE_POPUP_POSITION"),
            PROGPU_WPF_TRACE_HIT_TEST = Env("PROGPU_WPF_TRACE_HIT_TEST"),
            TMPDIR = Env("TMPDIR")
        };
    }

    private static object DescribeMouse(Application? app)
    {
        var directlyOver = Mouse.DirectlyOver;
        var captured = Mouse.Captured;
        var perWindow = new List<object>();

        if (app != null)
        {
            foreach (Window window in app.Windows)
            {
                var root = PresentationSource.FromVisual(window)?.RootVisual as UIElement ?? window.Content as UIElement;
                if (root == null)
                    continue;

                var position = Mouse.GetPosition(root);
                object? hit = null;
                try
                {
                    hit = root.InputHitTest(position);
                }
                catch (Exception ex)
                {
                    hit = new { error = ex.GetType().Name, ex.Message };
                }

                perWindow.Add(new
                {
                    window = DescribeWindowIdentity(window),
                    positionInRoot = DescribePoint(position),
                    hit = DescribeObject(hit),
                    root = DescribeObject(root)
                });
            }
        }

        return new
        {
            directlyOver = DescribeObject(directlyOver),
            captured = DescribeObject(captured),
            capturedMode = TryReadStaticProperty(typeof(Mouse), "CapturedMode")?.ToString(),
            perWindow
        };
    }

    private static List<object> DescribeWindows(Application? app, int maxVisualDepth)
    {
        var windows = new List<object>();
        if (app == null)
            return windows;

        foreach (Window window in app.Windows)
        {
            windows.Add(DescribeWindow(window, maxVisualDepth));
        }

        return windows;
    }

    private static object DescribeWindow(Window window, int maxVisualDepth)
    {
        var source = PresentationSource.FromVisual(window);
        var target = source?.CompositionTarget;
        var root = source?.RootVisual as DependencyObject ?? window.Content as DependencyObject;
        var mouseRoot = root as IInputElement;

        return new
        {
            identity = DescribeWindowIdentity(window),
            title = window.Title,
            state = window.WindowState.ToString(),
            isActive = window.IsActive,
            isVisible = window.IsVisible,
            isEnabled = window.IsEnabled,
            isFocused = window.IsFocused,
            left = window.Left,
            top = window.Top,
            width = window.Width,
            height = window.Height,
            actualWidth = window.ActualWidth,
            actualHeight = window.ActualHeight,
            restoreBounds = DescribeRect(window.RestoreBounds),
            source = source == null ? null : new
            {
                type = DescribeType(source),
                rootVisual = DescribeObject(source.RootVisual),
                transformToDevice = target == null ? null : DescribeMatrix(target.TransformToDevice),
                transformFromDevice = target == null ? null : DescribeMatrix(target.TransformFromDevice)
            },
            mousePosition = mouseRoot == null ? null : DescribePoint(Mouse.GetPosition(mouseRoot)),
            focusedElement = DescribeObject(FocusManager.GetFocusedElement(window)),
            keyboardFocus = DescribeObject(Keyboard.FocusedElement),
            hitAtMouse = DescribeHitAtMouse(mouseRoot),
            menuAndPopupElements = root == null ? Array.Empty<object>() : FindMenuAndPopupElements(root, maxVisualDepth).ToArray()
        };
    }

    private static object? DescribeHitAtMouse(IInputElement? root)
    {
        if (root is not UIElement element)
            return null;

        try
        {
            var position = Mouse.GetPosition(root);
            return new
            {
                position = DescribePoint(position),
                hit = DescribeObject(element.InputHitTest(position))
            };
        }
        catch (Exception ex)
        {
            return new { error = ex.GetType().Name, ex.Message };
        }
    }

    private static List<object> FindMenuAndPopupElements(DependencyObject root, int maxDepth)
    {
        var results = new List<object>();
        var visited = new HashSet<DependencyObject>(ReferenceEqualityComparer.Instance);
        Walk(root, 0);
        return results;

        void Walk(DependencyObject current, int depth)
        {
            if (depth > maxDepth || !visited.Add(current))
                return;

            if (IsMenuOrPopupRelevant(current))
                results.Add(DescribeMenuOrPopupElement(current, depth));

            foreach (var child in EnumerateChildren(current))
            {
                Walk(child, depth + 1);
            }
        }
    }

    private static bool IsMenuOrPopupRelevant(DependencyObject value)
        => value is Menu
           || value is MenuItem
           || value is ContextMenu
           || value is Popup
           || value is ToolTip
           || value.GetType().FullName == "System.Windows.Controls.Primitives.PopupRoot";

    private static object DescribeMenuOrPopupElement(DependencyObject value, int depth)
    {
        var typeName = DescribeType(value);
        var fe = value as FrameworkElement;
        var control = value as Control;
        var popup = value as Popup;
        var menuItem = value as MenuItem;
        var contextMenu = value as ContextMenu;
        var tooltip = value as ToolTip;

        return new
        {
            depth,
            type = typeName,
            name = fe?.Name,
            automationId = TryGetAutomationId(value),
            text = TryDescribeText(value),
            isVisible = fe?.IsVisible,
            visibility = fe?.Visibility.ToString(),
            isEnabled = control?.IsEnabled ?? fe?.IsEnabled,
            isMouseOver = fe?.IsMouseOver,
            isKeyboardFocusWithin = fe?.IsKeyboardFocusWithin,
            bounds = fe == null ? null : DescribeElementBounds(fe),
            menuItem = menuItem == null ? null : new
            {
                header = menuItem.Header?.ToString(),
                role = TryReadProperty(menuItem, "Role")?.ToString(),
                isSubmenuOpen = menuItem.IsSubmenuOpen,
                isHighlighted = menuItem.IsHighlighted,
                staysOpenOnClick = menuItem.StaysOpenOnClick
            },
            popup = popup == null ? null : new
            {
                isOpen = popup.IsOpen,
                placement = popup.Placement.ToString(),
                placementTarget = DescribeObject(popup.PlacementTarget),
                placementRectangle = DescribeRect(popup.PlacementRectangle),
                horizontalOffset = popup.HorizontalOffset,
                verticalOffset = popup.VerticalOffset,
                child = DescribeObject(popup.Child)
            },
            contextMenu = contextMenu == null ? null : new
            {
                isOpen = contextMenu.IsOpen,
                placement = contextMenu.Placement.ToString(),
                placementTarget = DescribeObject(contextMenu.PlacementTarget),
                horizontalOffset = contextMenu.HorizontalOffset,
                verticalOffset = contextMenu.VerticalOffset
            },
            toolTip = tooltip == null ? null : new
            {
                isOpen = tooltip.IsOpen,
                placement = tooltip.Placement.ToString(),
                placementTarget = DescribeObject(tooltip.PlacementTarget),
                horizontalOffset = tooltip.HorizontalOffset,
                verticalOffset = tooltip.VerticalOffset
            }
        };
    }

    private static object? DescribeElementBounds(FrameworkElement element)
    {
        try
        {
            var source = PresentationSource.FromVisual(element);
            var originInSource = element.TransformToAncestor(source?.RootVisual ?? element).Transform(new Point(0, 0));
            var size = new Size(element.ActualWidth, element.ActualHeight);
            Point? originInDevice = null;
            if (source?.CompositionTarget != null)
                originInDevice = source.CompositionTarget.TransformToDevice.Transform(originInSource);

            return new
            {
                actualWidth = element.ActualWidth,
                actualHeight = element.ActualHeight,
                renderSize = DescribeSize(element.RenderSize),
                originInSource = DescribePoint(originInSource),
                originInDevice = originInDevice == null ? null : DescribePoint(originInDevice.Value),
                descendantBounds = DescribeRect(VisualTreeHelper.GetDescendantBounds(element)),
                layoutSlot = DescribeRect(LayoutInformation.GetLayoutSlot(element)),
                screen = TryDescribeScreenBounds(element, size)
            };
        }
        catch (Exception ex)
        {
            return new { error = ex.GetType().Name, ex.Message };
        }
    }

    private static object? TryDescribeScreenBounds(FrameworkElement element, Size size)
    {
        try
        {
            var topLeft = element.PointToScreen(new Point(0, 0));
            var bottomRight = element.PointToScreen(new Point(size.Width, size.Height));
            return new
            {
                topLeft = DescribePoint(topLeft),
                bottomRight = DescribePoint(bottomRight),
                width = bottomRight.X - topLeft.X,
                height = bottomRight.Y - topLeft.Y
            };
        }
        catch (Exception ex)
        {
            return new { error = ex.GetType().Name, ex.Message };
        }
    }

    private static IEnumerable<DependencyObject> EnumerateChildren(DependencyObject value)
    {
        var visualCount = 0;
        try
        {
            visualCount = VisualTreeHelper.GetChildrenCount(value);
        }
        catch
        {
            visualCount = 0;
        }

        for (var i = 0; i < visualCount; i++)
        {
            DependencyObject? child = null;
            try
            {
                child = VisualTreeHelper.GetChild(value, i);
            }
            catch
            {
                child = null;
            }

            if (child != null)
                yield return child;
        }

        if (visualCount > 0)
            yield break;

        IEnumerable? logicalChildren = null;
        try
        {
            logicalChildren = LogicalTreeHelper.GetChildren(value);
        }
        catch
        {
            logicalChildren = null;
        }

        if (logicalChildren == null)
            yield break;

        foreach (var child in logicalChildren)
        {
            if (child is DependencyObject dependencyObject)
                yield return dependencyObject;
        }
    }

    private static object TryDescribeProGpuMonitors()
    {
        try
        {
            var serviceType = FindType("System.Windows.Media.ProGPU.Platform.CrossPlatformWpfPlatformServices");
            var instance = serviceType?.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            var monitors = instance?.GetType().GetProperty("Monitors", BindingFlags.Public | BindingFlags.Instance)?.GetValue(instance);
            var monitorItems = InvokeMethod(monitors, "GetMonitors") as IEnumerable;
            if (monitorItems == null)
                return new { available = false, reason = "ProGPU monitor service not found" };

            var result = new List<object>();
            foreach (var monitor in monitorItems)
            {
                result.Add(new
                {
                    type = DescribeType(monitor),
                    name = TryReadProperty(monitor, "Name")?.ToString(),
                    x = TryReadProperty(monitor, "X"),
                    y = TryReadProperty(monitor, "Y"),
                    width = TryReadProperty(monitor, "Width"),
                    height = TryReadProperty(monitor, "Height"),
                    dpiScale = TryReadProperty(monitor, "DpiScale"),
                    isPrimary = TryReadProperty(monitor, "IsPrimary")
                });
            }

            return new { available = true, monitors = result };
        }
        catch (Exception ex)
        {
            return new { available = false, error = ex.GetType().Name, ex.Message };
        }
    }

    private static Dictionary<string, object?> DescribeLogs(int maxLogLines)
    {
        var logs = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in new[]
                 {
                     "/tmp/librewpf-menu-input.log",
                     "/tmp/librewpf-window-move.log",
                     "/tmp/tooltiptest_debug.log"
                 })
        {
            logs[path] = DescribeLog(path, maxLogLines);
        }

        return logs;
    }

    private static object DescribeLog(string path, int maxLogLines)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
                return new { exists = false };

            var lines = maxLogLines == 0 ? Array.Empty<string>() : TailLines(path, maxLogLines);
            return new
            {
                exists = true,
                length = info.Length,
                lastWriteUtc = info.LastWriteTimeUtc.ToString("O"),
                lineCount = lines.Length,
                tail = lines
            };
        }
        catch (Exception ex)
        {
            return new { exists = false, error = ex.GetType().Name, ex.Message };
        }
    }

    private static string[] TailLines(string path, int maxLines)
    {
        var queue = new Queue<string>(maxLines);
        foreach (var line in File.ReadLines(path))
        {
            if (queue.Count == maxLines)
                queue.Dequeue();
            queue.Enqueue(line);
        }

        return queue.ToArray();
    }

    private static Type? FindType(string fullName)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type? type = null;
            try
            {
                type = assembly.GetType(fullName, throwOnError: false);
            }
            catch
            {
                type = null;
            }

            if (type != null)
                return type;
        }

        return null;
    }

    private static object? InvokeMethod(object? target, string name)
        => target?.GetType().GetMethod(name, BindingFlags.Public | BindingFlags.Instance)?.Invoke(target, null);

    private static object? TryReadProperty(object? target, string propertyName)
    {
        if (target == null)
            return null;

        try
        {
            return target.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(target);
        }
        catch
        {
            return null;
        }
    }

    private static object? TryReadStaticProperty(Type targetType, string propertyName)
    {
        try
        {
            return targetType.GetProperty(propertyName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null);
        }
        catch
        {
            return null;
        }
    }

    private static string? TryDescribeAutomationId(DependencyObject value)
        => TryGetAutomationId(value);

    private static string? TryGetAutomationId(DependencyObject value)
    {
        try
        {
            return System.Windows.Automation.AutomationProperties.GetAutomationId(value);
        }
        catch
        {
            return null;
        }
    }

    private static string? TryDescribeText(object value)
        => value switch
        {
            HeaderedItemsControl headered => headered.Header?.ToString(),
            HeaderedContentControl headered => headered.Header?.ToString(),
            ContentControl content => content.Content?.ToString(),
            TextBlock textBlock => textBlock.Text,
            TextBox textBox => textBox.Text,
            _ => null
        };

    private static object? DescribeObject(object? value)
        => value == null
            ? null
            : new
            {
                type = DescribeType(value),
                hash = value.GetHashCode(),
                name = value is FrameworkElement fe ? fe.Name : null,
                automationId = value is DependencyObject dependencyObject ? TryDescribeAutomationId(dependencyObject) : null,
                text = TryDescribeText(value)
            };

    private static object DescribeWindowIdentity(Window window)
        => new
        {
            type = DescribeType(window),
            hash = window.GetHashCode(),
            title = window.Title,
            name = window.Name
        };

    private static string DescribeType(object value)
        => value.GetType().FullName ?? value.GetType().Name;

    private static object DescribePoint(Point point)
        => new { x = point.X, y = point.Y };

    private static object DescribeSize(Size size)
        => new { width = size.Width, height = size.Height };

    private static object DescribeRect(Rect rect)
        => new { x = rect.X, y = rect.Y, width = rect.Width, height = rect.Height, isEmpty = rect.IsEmpty };

    private static object DescribeMatrix(Matrix matrix)
        => new
        {
            m11 = matrix.M11,
            m12 = matrix.M12,
            m21 = matrix.M21,
            m22 = matrix.M22,
            offsetX = matrix.OffsetX,
            offsetY = matrix.OffsetY
        };

    private sealed class ReferenceEqualityComparer : IEqualityComparer<DependencyObject>
    {
        public static ReferenceEqualityComparer Instance { get; } = new();

        public bool Equals(DependencyObject? x, DependencyObject? y)
            => ReferenceEquals(x, y);

        public int GetHashCode(DependencyObject obj)
            => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }
}
