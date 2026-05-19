using Microsoft.Maui.DevFlow.Agent.Core;

namespace LeXtudio.Wpf.DevFlow.Agent.Core;

public interface IVisualTreeWalker
{
    List<ElementInfo> WalkTree();
    ElementInfo? FindElementById(string id);
}
