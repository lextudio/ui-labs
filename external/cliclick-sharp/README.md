# CliclickSharp

C# reimplementation of [cliclick](https://github.com/BlueM/cliclick), a command line tool for macOS that can simulate mouse and keyboard events.

## Build

```
dotnet build
```

## Usage

```
cliclick [options] <commands>
```

All commands and options from the original cliclick v5.1 are supported:

| Option | Description |
|--------|-------------|
| `-r` | Restore mouse position after execution |
| `-m mode` | Set mode: `verbose` or `test`, with optional `:destination` |
| `-d dest` | Output destination (`stdout`, `stderr`, `clipboard`, or file path) |
| `-e num` | Easing factor for mouse movements |
| `-f file` | Read commands from file (`-` for stdin) |
| `-w num` | Wait N ms between commands |
| `-V` | Show version |
| `-o` | Open version history in browser |
| `-n` | Open donations page |
| `-h` | Show help |

| Command | Description |
|---------|-------------|
| `m:x,y` | Move mouse |
| `c:x,y` | Click |
| `dc:x,y` | Double-click |
| `tc:x,y` | Triple-click |
| `rc:x,y` | Right-click |
| `dd:x,y` | Drag down (press button) |
| `dm:x,y` | Drag move |
| `du:x,y` | Drag up (release button) |
| `kd:key` | Modifier key down (`cmd`,`ctrl`,`alt`,`shift`,`fn`) |
| `ku:key` | Modifier key up |
| `kp:key` | Press a key (`return`,`tab`,`space`,`delete`,`f1`-`f16`, arrows, etc.) |
| `t:text` | Type text |
| `p` | Print mouse position |
| `p:text` | Print text |
| `w:ms` | Wait |
| `cp:x,y` | Get RGB color at coordinates |

## Implementation

- .NET 8 console app using P/Invoke to CoreGraphics, Carbon, and Objective-C runtime
- Architecture mirrors the original: command pattern with action classes
- Mouse events via CGEventCreateMouseEvent with cubic easing
- Keyboard events via CGEventCreateKeyboardEvent
- Media/brightness keys via NSSystemDefined events
- Keyboard layout lookup via TIS/UCKeyTranslate
- Clipboard via NSPasteboard ObjC interop
