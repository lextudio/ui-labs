using System.Runtime.InteropServices;

namespace CliclickSharp.Native;

public static class ObjCRuntime
{
    private const string ObjCLibrary = "/usr/lib/libobjc.dylib";

    [DllImport(ObjCLibrary)]
    public static extern IntPtr objc_getClass(string name);

    [DllImport(ObjCLibrary)]
    public static extern IntPtr sel_registerName(string name);

    [DllImport(ObjCLibrary)]
    public static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector);

    [DllImport(ObjCLibrary)]
    public static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector, IntPtr arg1);

    [DllImport(ObjCLibrary)]
    public static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector, IntPtr arg1, IntPtr arg2);

    [DllImport(ObjCLibrary)]
    public static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector, IntPtr arg1, IntPtr arg2, IntPtr arg3);

    [DllImport(ObjCLibrary)]
    public static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector, uint arg1, CGPoint arg2, ulong arg3, double arg4, nint arg5, IntPtr arg6, short arg7, nint arg8, nint arg9);

    [DllImport(ObjCLibrary)]
    public static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector, uint arg1, IntPtr arg2);

    [DllImport(ObjCLibrary)]
    public static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector, double arg1, IntPtr arg2, ulong arg3, double arg4, nint arg5, IntPtr arg6, short arg7, nint arg8, nint arg9);

    [DllImport(ObjCLibrary)]
    public static extern void objc_msgSend_stret(out IntPtr retVal, IntPtr receiver, IntPtr selector);

    [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
    public static extern ulong objc_msgSend_ulong(IntPtr receiver, IntPtr selector, IntPtr arg1);

    [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
    public static extern ulong objc_msgSend_ulong(IntPtr receiver, IntPtr selector);

    [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
    public static extern ulong objc_msgSend_ulong(IntPtr receiver, IntPtr selector, uint arg, IntPtr arg2);

    [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
    public static extern nint objc_msgSend_nint(IntPtr receiver, IntPtr selector, IntPtr arg1, IntPtr arg2);

    [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
    public static extern bool objc_msgSend_bool(IntPtr receiver, IntPtr selector);

    [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
    public static extern bool objc_msgSend_bool(IntPtr receiver, IntPtr selector, IntPtr arg1);

    [DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
    public static extern bool objc_msgSend_bool(IntPtr receiver, IntPtr selector, IntPtr arg1, uint arg2);

    [DllImport(ObjCLibrary)]
    public static extern IntPtr objc_getProtocol(string name);

    [DllImport(ObjCLibrary)]
    public static extern IntPtr object_getClass(IntPtr obj);

    [DllImport(ObjCLibrary)]
    public static extern IntPtr objc_autoreleasePoolPush();

    [DllImport(ObjCLibrary)]
    public static extern void objc_autoreleasePoolPop(IntPtr pool);
}
