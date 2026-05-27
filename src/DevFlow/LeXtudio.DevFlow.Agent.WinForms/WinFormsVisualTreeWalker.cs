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
            FrameworkProperties = BuildFrameworkProperties(c),
            Children = c.Controls.Cast<Control>().Select(child => BuildElement(child, id)).ToList()
        };
    }
}
