# LeXtudio.DevFlow.Agent.Avalonia

Avalonia runtime package for LeXtudio DevFlow support in Avalonia desktop applications.

This package builds on `LeXtudio.DevFlow.Agent.Core` and adds runtime integration for [Avalonia](https://avaloniaui.net/) applications on Windows, macOS and Linux.

## Install

```powershell
dotnet add package LeXtudio.DevFlow.Agent.Avalonia
```

## What is included

- Avalonia runtime agent registration
- visual and logical tree inspection of every open window, with element ids taken from `Name`
- window and element screenshots (PNG)
- tap, fill, clear, focus, key, scroll and back actions
- application theme get/set (`RequestedThemeVariant`)
- `[DevFlowAction]` methods on the application, its windows and their data contexts
- OS-level pointer input for click, move, drag and the decomposed press / drag-move / release (XTest on Linux, SendInput on Windows, cliclick on macOS)
- shared DevFlow HTTP API integration

## Usage

Register the Avalonia DevFlow agent once the application is initialized:

```csharp
public override void OnFrameworkInitializationCompleted()
{
    if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        desktop.MainWindow = new MainWindow();

    this.AddAvaloniaDevFlowAgent();
    base.OnFrameworkInitializationCompleted();
}
```

## Coordinates

Element bounds are device independent units: a window reports its screen position, every other element its position relative to its window. The global input actions take screen pixels, which is what `Visual.PointToScreen` returns; `nativeProperties.screenX`/`screenY` give a window's position in pixels.

## Getting Started

For detailed setup and configuration instructions, see the [Avalonia DevFlow Guide](https://github.com/lextudio/wpf-labs/blob/master/docs/devflow/howto-avalonia.md).

For common questions about port configuration and troubleshooting, see the [DevFlow FAQ](https://github.com/lextudio/wpf-labs/blob/master/docs/devflow/faq.md).

## Related Packages

- [LeXtudio.DevFlow.Agent.Core](https://www.nuget.org/packages/LeXtudio.DevFlow.Agent.Core)
- [LeXtudio.DevFlow.Driver](https://www.nuget.org/packages/LeXtudio.DevFlow.Driver)
- [LeXtudio.DevFlow.Agent.Uno](https://www.nuget.org/packages/LeXtudio.DevFlow.Agent.Uno)
- [LeXtudio.DevFlow.Agent.MewUI](https://www.nuget.org/packages/LeXtudio.DevFlow.Agent.MewUI)

## Compatibility

- .NET 10.0+
- Avalonia 12 desktop applications (`IClassicDesktopStyleApplicationLifetime`)
