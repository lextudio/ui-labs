using System.Runtime.CompilerServices;

// Upstream keeps the broker daemon and the browser inspector inside one Microsoft.Maui.Cli assembly, so the
// types they share (LayoutDiagnosticsDelta, InspectorServer.InspectorDiagnosticRequest,
// DevFlowCliJsonContext, InspectorAlertController) are internal and both sides can see them.
//
// This repository publishes them as two packages, which would leave the broker unable to reach those
// types. Granting access here keeps the upstream sources unmodified and compiles them only once, in the
// inspector assembly; compiling them into the broker as well would define the same types twice and make
// every consumer that references both packages fail with CS0433.
[assembly: InternalsVisibleTo("LeXtudio.DevFlow.Broker")]
[assembly: InternalsVisibleTo("LeXtudio.DevFlow.Agent.Core.Tests")]
[assembly: InternalsVisibleTo("LeXtudio.DevFlow.Agent.LibreWinForms.Tests")]