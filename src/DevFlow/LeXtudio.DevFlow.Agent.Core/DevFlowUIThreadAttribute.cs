namespace LeXtudio.DevFlow.Agent.Core;

/// <summary>
/// Apply to a class containing <see cref="DevFlowActionAttribute"/>-annotated static methods
/// to indicate that all methods in that class must be dispatched to the main UI thread before
/// invocation. The InvokeAction handler will use DispatcherQueue.TryEnqueue to marshal the call.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class DevFlowUIThreadAttribute : Attribute { }
