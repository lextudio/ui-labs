using System.Runtime.InteropServices;
using CliclickSharp.Native;

namespace CliclickSharp.Actions;

public class ColorPickerAction : IAction
{
    public static string CommandShortcut => "cp";
    public static string CommandDescription => "Get color at screen coordinates";

    public bool PerformAction(string data, ExecutionOptions options)
    {
        if (!MouseBaseAction.GetCoordinate(data, out double x, out double y))
            return false;

        CGRect rect = new(new CGPoint(x, y), new CGSize(1, 1));
        IntPtr imageRef = CoreGraphics.CGWindowListCreateImage(
            rect,
            CGWindowListOption.kCGWindowListOptionOnScreenOnly,
            0,
            CGWindowImageOption.kCGWindowImageDefault);

        if (imageRef == IntPtr.Zero)
        {
            Console.Error.WriteLine("Failed to capture screen");
            return false;
        }

        try
        {
            int width = CGImageGetWidth(imageRef);
            int height = CGImageGetHeight(imageRef);

            if (width == 0 || height == 0)
            {
                Console.Error.WriteLine("Failed to capture screen region");
                return false;
            }

            IntPtr colorSpace = CGColorSpaceCreateDeviceRGB();
            IntPtr context = CGBitmapContextCreate(
                IntPtr.Zero, 1, 1, 8, 4,
                colorSpace,
                1 << 14 | 1 << 12); // kCGImageAlphaPremultipliedFirst | kCGBitmapByteOrder32Little

            if (context != IntPtr.Zero)
            {
                CGRect drawRect = new(new CGPoint(0, 0), new CGSize(1, 1));
                CGContextDrawImage(context, drawRect, imageRef);

                IntPtr pixelData = CGBitmapContextGetData(context);
                if (pixelData != IntPtr.Zero)
                {
                    byte r = Marshal.ReadByte(pixelData, 1);
                    byte g = Marshal.ReadByte(pixelData, 2);
                    byte b = Marshal.ReadByte(pixelData, 3);
                    options.CommandOutputHandler?.WriteLine($"{r} {g} {b}");
                }

                CGContextRelease(context);
            }

            CGColorSpaceRelease(colorSpace);
        }
        finally
        {
            CoreGraphics.CFRelease(imageRef);
        }

        return true;
    }

    [DllImport(CoreGraphicsLib)]
    private static extern int CGImageGetWidth(IntPtr image);

    [DllImport(CoreGraphicsLib)]
    private static extern int CGImageGetHeight(IntPtr image);

    [DllImport(CoreGraphicsLib)]
    private static extern IntPtr CGColorSpaceCreateDeviceRGB();

    [DllImport(CoreGraphicsLib)]
    private static extern void CGColorSpaceRelease(IntPtr space);

    [DllImport(CoreGraphicsLib)]
    private static extern IntPtr CGBitmapContextCreate(IntPtr data, int width, int height, int bitsPerComponent, int bytesPerRow, IntPtr space, uint bitmapInfo);

    [DllImport(CoreGraphicsLib)]
    private static extern void CGContextDrawImage(IntPtr context, CGRect rect, IntPtr image);

    [DllImport(CoreGraphicsLib)]
    private static extern IntPtr CGBitmapContextGetData(IntPtr context);

    [DllImport(CoreGraphicsLib)]
    private static extern void CGContextRelease(IntPtr context);

    private const string CoreGraphicsLib = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
}
