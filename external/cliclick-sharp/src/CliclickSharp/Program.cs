using System.Runtime.InteropServices;
using CliclickSharp.Native;

namespace CliclickSharp;

public class Program
{
    private const int MinimumWaitTime = 100;
    private const string Version = "5.1";
    private const string ReleaseDate = "2022-08-14";

    public static int Main(string[] args)
    {
        if (!CoreGraphics.AXIsProcessTrusted())
        {
            Console.Error.WriteLine(
                "cliclick requires accessibility permissions. " +
                "Please grant accessibility access in System Preferences > " +
                "Security & Privacy > Privacy > Accessibility.");
        }

        if (args.Length == 0)
        {
            PrintHelp();
            return 0;
        }

        var options = new ExecutionOptions();
        options.CommandOutputHandler = new OutputHandler("stdout");
        options.VerbosityOutputHandler = new OutputHandler("stdout");

        bool restorePosition = false;
        CGPoint? initialPosition = null;
        List<string> commands = new();

        int i = 0;
        while (i < args.Length)
        {
            string arg = args[i];
            switch (arg)
            {
                case "-r":
                    restorePosition = true;
                    break;
                case "-m":
                {
                    i++;
                    if (i >= args.Length)
                    {
                        Console.Error.WriteLine("-m requires an argument (verbose|test or verbose:dest|test:dest)");
                        return 1;
                    }
                    string modeArg = args[i];
                    int colonIdx = modeArg.IndexOf(':');
                    string modeStr = colonIdx == -1 ? modeArg : modeArg[..colonIdx];
                    string destStr = colonIdx == -1 ? "stdout" : modeArg[(colonIdx + 1)..];

                    options.Mode = modeStr.ToLowerInvariant() switch
                    {
                        "verbose" => OutputMode.Verbose,
                        "test" => OutputMode.Test,
                        _ => OutputMode.Regular,
                    };
                    options.VerbosityOutputHandler = new OutputHandler(destStr);
                    break;
                }
                case "-d":
                {
                    i++;
                    if (i >= args.Length)
                    {
                        Console.Error.WriteLine("-d requires an argument (stdout|stderr|clipboard|<path>)");
                        return 1;
                    }
                    options.CommandOutputHandler = new OutputHandler(args[i]);
                    break;
                }
                case "-e":
                {
                    i++;
                    if (i >= args.Length || !uint.TryParse(args[i], out uint easing))
                    {
                        Console.Error.WriteLine("-e requires a numeric argument");
                        return 1;
                    }
                    options.Easing = easing;
                    break;
                }
                case "-f":
                {
                    i++;
                    if (i >= args.Length)
                    {
                        Console.Error.WriteLine("-f requires a file path argument (or '-' for stdin)");
                        return 1;
                    }
                    string fileArg = args[i];
                    IEnumerable<string> fileCommands;
                    if (fileArg == "-")
                    {
                        fileCommands = ReadCommandsFromStdin();
                    }
                    else
                    {
                        if (!File.Exists(fileArg))
                        {
                            Console.Error.WriteLine($"File not found: {fileArg}");
                            return 1;
                        }
                        fileCommands = ReadCommandsFromFile(fileArg);
                    }
                    commands.AddRange(fileCommands);
                    options.IsFirstAction = true;
                    break;
                }
                case "-w":
                {
                    i++;
                    if (i >= args.Length || !uint.TryParse(args[i], out uint waitTime))
                    {
                        Console.Error.WriteLine("-w requires a numeric argument");
                        return 1;
                    }
                    options.WaitTime = waitTime;
                    break;
                }
                case "-V":
                    Console.Error.WriteLine($"cliclick {Version} ({ReleaseDate})");
                    return 0;
                case "-o":
                    OpenUrl("https://github.com/BlueM/cliclick/releases");
                    return 0;
                case "-n":
                    OpenUrl("https://www.bluem.net/jump/donate/");
                    return 0;
                case "-h":
                case "--help":
                    PrintHelp();
                    return 0;
                default:
                    commands.Add(arg);
                    break;
            }
            i++;
        }

        if (commands.Count == 0)
        {
            Console.Error.WriteLine("No commands specified");
            return 1;
        }

        if (restorePosition)
        {
            initialPosition = CoreGraphics.GetCurrentMouseLocation();
        }

        // Mark first and last
        for (int j = 0; j < commands.Count; j++)
        {
            options.IsFirstAction = j == 0;
            options.IsLastAction = j == commands.Count - 1;

            if (!ActionExecutor.ExecuteActionString(commands[j], options))
                return 1;

            if (options.WaitTime > 0 && j < commands.Count - 1)
            {
                Thread.Sleep(Math.Max((int)options.WaitTime, MinimumWaitTime));
            }
        }

        if (restorePosition && initialPosition.HasValue)
        {
            string posCmd = $"m:={(int)initialPosition.Value.X},={(int)initialPosition.Value.Y}";
            var restoreOptions = new ExecutionOptions();
            ActionExecutor.ExecuteActionString(posCmd, restoreOptions);
        }

        options.CommandOutputHandler?.Flush();
        options.VerbosityOutputHandler?.Flush();
        options.CommandOutputHandler?.Dispose();
        options.VerbosityOutputHandler?.Dispose();

        return 0;
    }

    private static void PrintHelp()
    {
        Console.Error.WriteLine(
            "cliclick - Command-Line Interface Click\n" +
            $"Version {Version} ({ReleaseDate})\n" +
            "https://github.com/BlueM/cliclick\n" +
            "\n" +
            "Usage:\n" +
            "  cliclick [options] <commands>\n" +
            "\n" +
            "Options:\n" +
            "  -r            restore mouse position after executing commands\n" +
            "  -m <mode>     set mode: verbose|test, optionally with :destination\n" +
            "  -d <dest>     set output destination (stdout|stderr|clipboard|<path>)\n" +
            "  -e <num>      easing factor for mouse movements (0 = off)\n" +
            "  -f <file>     read commands from file ('-' for stdin)\n" +
            "  -w <num>      wait N milliseconds between commands\n" +
            "  -V            show version\n" +
            "  -o            open version history in browser\n" +
            "  -n            open donations page in browser\n" +
            "  -h            show this help\n" +
            "\n" +
            "Commands:\n" +
            "  m:x,y         move mouse to absolute or relative coordinates\n" +
            "  c:x,y         click at coordinates\n" +
            "  dc:x,y        double-click at coordinates\n" +
            "  tc:x,y        triple-click at coordinates\n" +
            "  rc:x,y        right-click at coordinates\n" +
            "  dd:x,y        press mouse button (begin drag)\n" +
            "  dm:x,y        drag to coordinates\n" +
            "  du:x,y        release mouse button (end drag)\n" +
            "  kd:key        press modifier key (cmd,ctrl,alt,shift,fn)\n" +
            "  ku:key        release modifier key\n" +
            "  kp:key        press a keyboard key (return, tab, space, etc.)\n" +
            "  t:text        type arbitrary text\n" +
            "  p             print current mouse position\n" +
            "  p:text        print text\n" +
            "  w:ms          wait for N milliseconds\n" +
            "  cp:x,y        get RGB color at screen coordinates");
    }

    private static IEnumerable<string> ReadCommandsFromFile(string filePath)
    {
        foreach (string line in File.ReadLines(filePath))
        {
            string trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
                continue;
            yield return trimmed;
        }
    }

    private static IEnumerable<string> ReadCommandsFromStdin()
    {
        string? line;
        while ((line = Console.In.ReadLine()) != null)
        {
            string trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
                continue;
            yield return trimmed;
        }
    }

    private static void OpenUrl(string url)
    {
        IntPtr pool = ObjCRuntime.objc_autoreleasePoolPush();
        try
        {
            IntPtr nsStringClass = ObjCRuntime.objc_getClass("NSString");
            IntPtr strSel = ObjCRuntime.sel_registerName("stringWithUTF8String:");
            IntPtr urlString = ObjCRuntime.objc_msgSend(nsStringClass, strSel, Marshal.StringToHGlobalAuto(url));

            IntPtr nsUrlClass = ObjCRuntime.objc_getClass("NSURL");
            IntPtr urlSel = ObjCRuntime.sel_registerName("URLWithString:");
            IntPtr nsUrl = ObjCRuntime.objc_msgSend(nsUrlClass, urlSel, urlString);

            IntPtr wsClass = ObjCRuntime.objc_getClass("NSWorkspace");
            IntPtr sharedSel = ObjCRuntime.sel_registerName("sharedWorkspace");
            IntPtr ws = ObjCRuntime.objc_msgSend(wsClass, sharedSel);

            IntPtr openSel = ObjCRuntime.sel_registerName("openURL:");
            ObjCRuntime.objc_msgSend(ws, openSel, nsUrl);
        }
        finally
        {
            ObjCRuntime.objc_autoreleasePoolPop(pool);
        }
    }
}
