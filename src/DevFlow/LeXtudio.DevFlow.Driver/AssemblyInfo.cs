using System.Runtime.CompilerServices;

// Upstream keeps the CLI's InspectorServer and the Driver helpers (ScreenshotImage, HostProcess) in
// projects that can see each other's internals: the Driver grants "maui" access. This repository
// publishes the inspector as its own package, so the same upstream sources need the matching grant
// here rather than a second, duplicate copy of the helper compiled into the inspector assembly.
[assembly: InternalsVisibleTo("LeXtudio.DevFlow.Inspector")]
