using System.Runtime.CompilerServices;

// The contract and serialization tests exercise internal helpers such as DevFlowJson, which carries the
// LeXtudio-specific JSON behavior that the linked upstream sources no longer provide.
[assembly: InternalsVisibleTo("LeXtudio.DevFlow.Agent.Core.Tests")]