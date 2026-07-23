using System.Runtime.InteropServices;

namespace CliclickSharp.Native;

public enum CGEventType : uint
{
    kCGEventNull = 0,
    kCGEventLeftMouseDown = 1,
    kCGEventLeftMouseUp = 2,
    kCGEventRightMouseDown = 3,
    kCGEventRightMouseUp = 4,
    kCGEventMouseMoved = 5,
    kCGEventLeftMouseDragged = 6,
    kCGEventRightMouseDragged = 7,
    kCGEventKeyDown = 10,
    kCGEventKeyUp = 11,
    kCGEventFlagsChanged = 12,
    kCGEventScrollWheel = 22,
    kCGEventTabletPointer = 23,
    kCGEventTabletProximity = 24,
    kCGEventOtherMouseDown = 25,
    kCGEventOtherMouseUp = 26,
    kCGEventOtherMouseDragged = 27,
}

public enum CGMouseButton : uint
{
    kCGMouseButtonLeft = 0,
    kCGMouseButtonRight = 1,
    kCGMouseButtonCenter = 2,
}

public enum CGEventTapLocation : uint
{
    kCGHIDEventTap = 0,
    kCGSessionEventTap = 1,
    kCGAnnotatedSessionEventTap = 2,
}

public enum CGWindowListOption : uint
{
    kCGWindowListOptionAll = 0,
    kCGWindowListOptionOnScreenOnly = 1,
    kCGWindowListOptionOnScreenAboveWindow = 2,
    kCGWindowListOptionOnScreenBelowWindow = 4,
    kCGWindowListOptionIncludingWindow = 8,
    kCGWindowListOptionExcludeDesktopElements = 16,
}

public enum CGWindowImageOption : uint
{
    kCGWindowImageDefault = 0,
    kCGWindowImageBoundsIgnoreFraming = 1,
    kCGWindowImageShouldBeOpaque = 2,
    kCGWindowImageOnlyShadows = 4,
    kCGWindowImageBestResolution = 8,
    kCGWindowImageNominalResolution = 16,
}

public enum CGEventField : uint
{
    kCGMouseEventNumber = 0,
    kCGMouseEventClickState = 1,
    kCGMouseEventPressure = 2,
    kCGMouseEventButtonNumber = 3,
    kCGMouseEventDeltaX = 4,
    kCGMouseEventDeltaY = 5,
    kCGMouseEventInstantMouser = 6,
    kCGMouseEventSubType = 7,
    kCGKeyboardEventAutorepeat = 8,
    kCGKeyboardEventKeycode = 9,
    kCGKeyboardEventKeyboardType = 10,
    kCGScrollWheelEventDeltaAxis1 = 11,
    kCGScrollWheelEventDeltaAxis2 = 12,
    kCGScrollWheelEventDeltaAxis3 = 13,
    kCGScrollWheelEventFixedDelta = 93,
    kCGScrollWheelEventPointDeltaAxis1 = 96,
    kCGScrollWheelEventPointDeltaAxis2 = 97,
    kCGScrollWheelEventPointDeltaAxis3 = 98,
    kCGScrollWheelEventInstantMouser = 14,
}

[StructLayout(LayoutKind.Sequential)]
public struct CGPoint
{
    public double X;
    public double Y;

    public CGPoint(double x, double y)
    {
        X = x;
        Y = y;
    }
}

[StructLayout(LayoutKind.Sequential)]
public struct CGSize
{
    public double Width;
    public double Height;

    public CGSize(double width, double height)
    {
        Width = width;
        Height = height;
    }
}

[StructLayout(LayoutKind.Sequential)]
public struct CGRect
{
    public CGPoint Origin;
    public CGSize Size;

    public CGRect(CGPoint origin, CGSize size)
    {
        Origin = origin;
        Size = size;
    }
}

public static class CoreGraphics
{
    private const string CoreGraphicsLibrary = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const string ApplicationServicesLibrary = "/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices";

    [DllImport(CoreGraphicsLibrary)]
    public static extern IntPtr CGEventCreate(IntPtr source);

    [DllImport(CoreGraphicsLibrary)]
    public static extern IntPtr CGEventCreateMouseEvent(IntPtr source, CGEventType type, CGPoint mousePoint, CGMouseButton button);

    [DllImport(CoreGraphicsLibrary)]
    public static extern IntPtr CGEventCreateKeyboardEvent(IntPtr source, ushort keyCode, bool keyDown);

    [DllImport(CoreGraphicsLibrary)]
    public static extern void CGEventPost(CGEventTapLocation tap, IntPtr eventRef);

    [DllImport(CoreGraphicsLibrary)]
    public static extern CGPoint CGEventGetLocation(IntPtr eventRef);

    [DllImport(CoreGraphicsLibrary)]
    public static extern void CGEventSetIntegerValueField(IntPtr eventRef, CGEventField field, long value);

    [DllImport(CoreGraphicsLibrary)]
    public static extern void CGEventSetType(IntPtr eventRef, CGEventType type);

    [DllImport(CoreGraphicsLibrary)]
    public static extern void CFRelease(IntPtr cf);

    [DllImport(CoreGraphicsLibrary)]
    public static extern IntPtr CGWindowListCreateImage(CGRect screenBounds, CGWindowListOption windowListOption, uint windowID, CGWindowImageOption imageOption);

    [DllImport(ApplicationServicesLibrary)]
    public static extern bool AXIsProcessTrusted();

    /// <summary>
    /// Returns the current mouse location in global screen points (top-left origin).
    /// <see cref="CGEventGetLocation"/> requires a real event: passing IntPtr.Zero returns
    /// (0,0), so — matching the original cliclick (Actions/MouseBaseAction.m) — we create a
    /// throwaway event with CGEventCreate(NULL) purely to read the cursor off it.
    /// </summary>
    public static CGPoint GetCurrentMouseLocation()
    {
        IntPtr evt = CGEventCreate(IntPtr.Zero);
        if (evt == IntPtr.Zero)
            return new CGPoint(0, 0);
        try { return CGEventGetLocation(evt); }
        finally { CFRelease(evt); }
    }
}
