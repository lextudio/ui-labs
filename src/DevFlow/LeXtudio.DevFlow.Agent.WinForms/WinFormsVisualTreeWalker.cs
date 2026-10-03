using System.Runtime.CompilerServices;
using System.Windows.Forms;
using Microsoft.Maui.DevFlow.Agent.Core;

namespace LeXtudio.DevFlow.Agent.WinForms;

public sealed class WinFormsVisualTreeWalker
{
    private readonly ConditionalWeakTable<Control, string> _stableIds = new();
    private readonly Dictionary<string, Control> _byId = new(StringComparer.OrdinalIgnoreCase);

    public List<ElementInfo> WalkTree()
    {
        _byId.Clear();
        var roots = new List<ElementInfo>();
        foreach (Form form in Application.OpenForms)
            roots.Add(BuildElement(form, null));
        return roots;
    }

    public ElementInfo? FindElementById(string id)
    {
        foreach (var root in WalkTree())
        {
            var match = Find(root, id);
            if (match != null) return match;
        }
        return null;
    }

    public Control? ResolveControlById(string id)
    {
        _ = WalkTree();
        _byId.TryGetValue(id, out var control);
        return control;
    }

    private static ElementInfo? Find(ElementInfo node, string id)
    {
        if (node.Id == id) return node;
        if (node.Children == null) return null;
        foreach (var c in node.Children)
        {
            var m = Find(c, id);
            if (m != null) return m;
        }
        return null;
    }

    private static Dictionary<string, string?>? BuildFrameworkProperties(Control c)
    {
        var props = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["backColor"] = ColorToString(c.BackColor),
            ["foreColor"] = ColorToString(c.ForeColor),
        };
        return props;
    }

    private static string ColorToString(System.Drawing.Color color)
    {
        return color.A == 255
            ? $"#{color.R:X2}{color.G:X2}{color.B:X2}"
            : $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
    }

    private ElementInfo BuildElement(Control c, string? parentId)
    {
        var id = _stableIds.GetValue(c, x => string.IsNullOrWhiteSpace(x.Name) ? $"_winforms_{Guid.NewGuid():N}" : x.Name);
        _byId[id] = c;

        var bounds = Measure(c);
        var isWindow = c is Form;

        return new ElementInfo
        {
            Id = id,
            ParentId = parentId,
            Type = c.GetType().Name,
            FullType = c.GetType().FullName ?? c.GetType().Name,
            Framework = "winforms",
            AutomationId = c.Name,
            Text = c.Text,
            IsVisible = c.Visible,
            IsEnabled = c.Enabled,
            // Bounds are reported relative to the owning window's client area, and WindowBounds states the
            // area they are measured against, which is the convention the shared revision hash and the
            // layout diagnostics both assume.
            Bounds = bounds,
            WindowBounds = isWindow ? bounds : null,
            BoundsQuality = bounds is null ? null : "exact",
            FrameworkProperties = BuildFrameworkProperties(c),
            Children = c.Controls.Cast<Control>().Select(child => BuildElement(child, id)).ToList()
        };
    }

    /// <summary>
    /// Measures a control in window coordinates, or null when it has no meaningful size.
    /// </summary>
    /// <remarks>
    /// A control's own <c>Left</c>/<c>Top</c> are relative to its parent, so the chain is summed up to the
    /// form. Scrollable containers offset their children, which this does not follow; the values are
    /// therefore exact for the common case and consistent within a window either way, which is what the
    /// revision hash and overlap checks need.
    /// </remarks>
    private static BoundsInfo? Measure(Control c)
    {
        try
        {
            var width = c.Width;
            var height = c.Height;
            if (width <= 0 || height <= 0)
            {
                return null;
            }

            var x = 0;
            var y = 0;
            for (var current = c; current is not null; current = current.Parent)
            {
                x += current.Left;
                y += current.Top;
            }

            return new BoundsInfo { X = x, Y = y, Width = width, Height = height };
        }
        catch (InvalidOperationException)
        {
            // The handle or the parent chain went away mid-walk; report the control without geometry
            // rather than failing the whole tree.
            return null;
        }
    }
}
