using System.Runtime.InteropServices;

namespace CliclickSharp.Native;

public static class Carbon
{
    private const string CarbonLibrary = "/System/Library/Frameworks/Carbon.framework/Carbon";

    [DllImport(CarbonLibrary)]
    public static extern IntPtr TISCopyCurrentKeyboardInputSource();

    [DllImport(CarbonLibrary)]
    public static extern IntPtr TISGetInputSourceProperty(IntPtr inputSource, IntPtr propertyKey);

    [DllImport(CarbonLibrary)]
    public static extern IntPtr CFDataGetBytePtr(IntPtr cfData);

    [DllImport(CarbonLibrary)]
    public static extern int CFDataGetLength(IntPtr cfData);

    [DllImport(CarbonLibrary)]
    public static extern void CFRelease(IntPtr cf);

    [DllImport(CarbonLibrary)]
    public static extern uint UCKeyTranslate(
        IntPtr keyLayoutPtr,
        ushort virtualKeyCode,
        ushort modifierState,
        uint keyboardType,
        uint keyTranslateOptions,
        ref uint deadKeyState,
        uint maxStringLength,
        out uint actualStringLength,
        [MarshalAs(UnmanagedType.LPArray)] ushort[] outputString);

    [DllImport(CarbonLibrary)]
    public static extern uint LMGetKbdType();

    public static readonly IntPtr kTISPropertyUnicodeKeyLayoutData;

    static Carbon()
    {
        kTISPropertyUnicodeKeyLayoutData = CFStringCreateWithCString("TISPropertyUnicodeKeyLayoutData");
    }

    private static IntPtr CFStringCreateWithCString(string str)
    {
        IntPtr lib = dlopen(CarbonLibrary, 0);
        IntPtr cfstr = dlsym(lib, "CFStringCreateWithCString");
        dlclose(lib);

        if (cfstr == IntPtr.Zero)
            return IntPtr.Zero;

        var del = Marshal.GetDelegateForFunctionPointer<
            Func<IntPtr, IntPtr, uint, IntPtr>>(cfstr);
        return del(IntPtr.Zero, Marshal.StringToHGlobalAuto(str), 0x08000100);
    }

    [DllImport("libSystem.dylib")]
    private static extern IntPtr dlopen(string path, int mode);

    [DllImport("libSystem.dylib")]
    private static extern IntPtr dlsym(IntPtr handle, string symbol);

    [DllImport("libSystem.dylib")]
    private static extern int dlclose(IntPtr handle);
}
