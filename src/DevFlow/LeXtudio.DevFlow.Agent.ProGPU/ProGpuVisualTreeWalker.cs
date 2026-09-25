using LeXtudio.DevFlow.Agent.Core;
using Microsoft.Maui.DevFlow.Agent.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ProGPU.Scene;
using ProGPU.WinUI;

namespace LeXtudio.DevFlow.Agent.ProGPU;

/// <summary>
/// Visual tree walker for ProGPU.WinUI applications.
/// Uses ProGPU's typed API directly (no reflection needed).
/// </summary>
public sealed class ProGpuVisualTreeWalker : IVisualTreeWalker
{
    private static int _nextId;

    public List<ElementInfo> WalkTree()
    {
        var results = new List<ElementInfo>();

        foreach (var window in WindowManager.ActiveWindows)
        {
            var root = window.Content;
            if (root is null) continue;

            var rootInfo = BuildElementInfo(root, null, root);
            results.Add(rootInfo);
        }

        return results;
    }

    public ElementInfo? FindElementById(string id)
    {
        foreach (var window in WindowManager.ActiveWindows)
        {
            var root = window.Content;
            if (root is null) continue;

            var result = FindByIdRecursive(root, id, root);
            if (result is not null) return result;
        }

        return null;
    }

    public Visual? FindVisualById(string id)
    {
        foreach (var window in WindowManager.ActiveWindows)
        {
            var root = window.Content;
            if (root is null) continue;

            var result = FindVisualByIdRecursive(root, id);
            if (result is not null) return result;
        }

        return null;
    }

    public List<ElementInfo> QueryElements(string? type, string? automationId, string? text, int maxResults = 50, int maxDepth = 20)
    {
        var results = new List<ElementInfo>();

        foreach (var window in WindowManager.ActiveWindows)
        {
            var root = window.Content;
            if (root is null) continue;

            QueryRecursive(root, type, automationId, text, results, maxResults, maxDepth, 0, root);
            if (results.Count >= maxResults) break;
        }

        return results;
    }

    private static ElementInfo BuildElementInfo(Visual visual, string? parentId, Visual root)
    {
        var id = Interlocked.Increment(ref _nextId).ToString();
        var typeName = visual.GetType().Name;
        var fullType = visual.GetType().FullName ?? typeName;

        string? name = null;
        string? text = null;
        bool isVisible = true;
        double opacity = 1.0;
        var frameworkProps = new Dictionary<string, string?>();

        if (visual is FrameworkElement fe)
        {
            name = fe.Name;
			if (string.IsNullOrEmpty(name))
			{
				name = Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(fe);
			}
            isVisible = fe.Visibility == Visibility.Visible;
        }

        if (visual is TextBlock tb)
        {
            text = tb.Text;
            frameworkProps["Text"] = tb.Text;
            frameworkProps["FontSize"] = tb.FontSize.ToString();
        }
        else if (visual is Button btn)
        {
            if (btn.Content is TextBlock btnText)
                text = btnText.Text;
            else if (btn.Content is string btnStr)
                text = btnStr;
            frameworkProps["IsEnabled"] = btn.IsEnabled.ToString();
        }
        else if (visual is TextBox textBox)
        {
            text = textBox.Text;
            frameworkProps["Text"] = textBox.Text;
        }

        opacity = visual.Opacity;

        // Calculate bounds
        BoundsInfo? bounds = null;
        try
        {
            if (visual.Size.X > 0 && visual.Size.Y > 0)
            {
                var transform = visual.TransformToVisual(root);
                var localRect = new Rect(0, 0, visual.Size.X, visual.Size.Y);
                var boundsRect = transform.TransformBounds(localRect);
                bounds = new BoundsInfo
                {
                    X = boundsRect.X,
                    Y = boundsRect.Y,
                    Width = boundsRect.Width,
                    Height = boundsRect.Height
                };
            }
        }
        catch
        {
            // TransformToVisual can throw if elements are not in same tree
        }

        var info = new ElementInfo
        {
            Id = id,
            ParentId = parentId,
            Type = typeName,
            FullType = fullType,
            Framework = "progpu",
            AutomationId = name,
            Text = text,
            IsVisible = isVisible,
            IsEnabled = true,
            Opacity = opacity,
            Bounds = bounds,
            FrameworkProperties = frameworkProps.Count > 0 ? frameworkProps : null,
        };

        // Recurse into children
        if (visual is ContainerVisual container)
        {
            var children = container.Children;
            if (children.Count > 0)
            {
                info.Children = new List<ElementInfo>(children.Count);
                foreach (var child in children)
                {
                    info.Children.Add(BuildElementInfo(child, id, root));
                }
            }
        }

        return info;
    }

    private static ElementInfo? FindByIdRecursive(Visual visual, string id, Visual root)
    {
		if (visual is FrameworkElement fe && MatchesId(fe, id))
            return BuildElementInfo(visual, null, root);

        if (visual is ContainerVisual container)
        {
            foreach (var child in container.Children)
            {
                var result = FindByIdRecursive(child, id, root);
                if (result is not null) return result;
            }
        }

        return null;
    }

    private static Visual? FindVisualByIdRecursive(Visual visual, string id)
    {
		if (visual is FrameworkElement frameworkElement && MatchesId(frameworkElement, id))
            return visual;

        if (visual is ContainerVisual container)
        {
            foreach (var child in container.Children)
            {
                var result = FindVisualByIdRecursive(child, id);
                if (result is not null) return result;
            }
        }

        return null;
    }

	private static bool MatchesId(FrameworkElement element, string id) =>
		element.Name == id ||
		Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(element) == id;

    private static void QueryRecursive(Visual visual, string? type, string? automationId, string? text,
        List<ElementInfo> results, int maxResults, int maxDepth, int currentDepth, Visual root)
    {
        if (currentDepth > maxDepth || results.Count >= maxResults)
            return;

        bool matches = true;
        if (type is not null && !visual.GetType().Name.Contains(type, StringComparison.OrdinalIgnoreCase))
            matches = false;
		if (automationId is not null && visual is FrameworkElement fe && !MatchesId(fe, automationId))
            matches = false;
        if (text is not null)
        {
            string? elemText = null;
            if (visual is TextBlock tb) elemText = tb.Text;
            else if (visual is Button btn && btn.Content is TextBlock bt) elemText = bt.Text;
            else if (visual is TextBox tx) elemText = tx.Text;

            if (elemText is null || !elemText.Contains(text, StringComparison.OrdinalIgnoreCase))
                matches = false;
        }

        if (matches)
            results.Add(BuildElementInfo(visual, null, root));

        if (visual is ContainerVisual container)
        {
            foreach (var child in container.Children)
            {
                QueryRecursive(child, type, automationId, text, results, maxResults, maxDepth, currentDepth + 1, root);
                if (results.Count >= maxResults) break;
            }
        }
    }
}
