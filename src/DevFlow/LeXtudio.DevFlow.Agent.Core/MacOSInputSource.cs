using System;
using System.Runtime.InteropServices;
using System.Text;

namespace LeXtudio.DevFlow.Agent.Core;

/// <summary>
/// Reads and selects this application's macOS keyboard input source (keyboard layout or input
/// method). A Pinyin source composes real key events into Chinese characters before the app sees
/// them, so a driver that types must select a plain layout first. The selection has to run inside
/// the application: <c>TISSelectInputSource</c> from another process only changes the source of
/// whichever app is frontmost at that moment, while this app's input context keeps its own.
/// </summary>
public static class MacOSInputSource
{
    private const string CoreFoundation =
        "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const string Carbon = "/System/Library/Frameworks/Carbon.framework/Carbon";
    private const uint Utf8 = 0x08000100;

    [DllImport(CoreFoundation)]
    private static extern IntPtr CFStringCreateWithCString(IntPtr allocator, string value, uint encoding);

    [DllImport(CoreFoundation)]
    private static extern bool CFStringGetCString(IntPtr value, byte[] buffer, nint size, uint encoding);

    [DllImport(CoreFoundation)]
    private static extern IntPtr CFDictionaryCreate(IntPtr allocator, IntPtr[] keys, IntPtr[] values,
        nint count, IntPtr keyCallbacks, IntPtr valueCallbacks);

    [DllImport(CoreFoundation)]
    private static extern nint CFArrayGetCount(IntPtr array);

    [DllImport(CoreFoundation)]
    private static extern IntPtr CFArrayGetValueAtIndex(IntPtr array, nint index);

    [DllImport(CoreFoundation)]
    private static extern void CFRelease(IntPtr value);

    [DllImport(Carbon)]
    private static extern IntPtr TISCopyCurrentKeyboardInputSource();

    [DllImport(Carbon)]
    private static extern IntPtr TISCreateInputSourceList(IntPtr properties, bool includeAllInstalled);

    [DllImport(Carbon)]
    private static extern IntPtr TISGetInputSourceProperty(IntPtr source, IntPtr key);

    [DllImport(Carbon)]
    private static extern int TISSelectInputSource(IntPtr source);

    private static IntPtr idKey;

    public static bool IsAvailable => OperatingSystem.IsMacOS();

    /// <summary>The current input source id, e.g. <c>com.apple.keylayout.ABC</c>, or null.</summary>
    public static string? Current()
    {
        if (!IsAvailable)
            return null;
        var source = TISCopyCurrentKeyboardInputSource();
        if (source == IntPtr.Zero)
            return null;
        try
        {
            var value = TISGetInputSourceProperty(source, IdKey());
            if (value == IntPtr.Zero)
                return null;
            var buffer = new byte[256];
            return CFStringGetCString(value, buffer, buffer.Length, Utf8)
                ? Encoding.UTF8.GetString(buffer, 0, Array.IndexOf(buffer, (byte)0))
                : null;
        }
        finally
        {
            CFRelease(source);
        }
    }

    /// <summary>Selects the enabled input source with <paramref name="id"/>.</summary>
    public static bool Select(string id)
    {
        if (!IsAvailable || string.IsNullOrEmpty(id))
            return false;
        var value = CFStringCreateWithCString(IntPtr.Zero, id, Utf8);
        if (value == IntPtr.Zero)
            return false;
        var filter = CFDictionaryCreate(IntPtr.Zero, new[] { IdKey() }, new[] { value }, 1, IntPtr.Zero, IntPtr.Zero);
        var list = filter == IntPtr.Zero ? IntPtr.Zero : TISCreateInputSourceList(filter, false);
        try
        {
            if (list == IntPtr.Zero || CFArrayGetCount(list) == 0)
                return false;
            return TISSelectInputSource(CFArrayGetValueAtIndex(list, 0)) == 0;
        }
        finally
        {
            if (list != IntPtr.Zero) CFRelease(list);
            if (filter != IntPtr.Zero) CFRelease(filter);
            CFRelease(value);
        }
    }

    private static IntPtr IdKey()
    {
        if (idKey != IntPtr.Zero)
            return idKey;
        // kTISPropertyInputSourceID is an exported data symbol holding the CFStringRef.
        var handle = NativeLibrary.Load(Carbon);
        idKey = Marshal.ReadIntPtr(NativeLibrary.GetExport(handle, "kTISPropertyInputSourceID"));
        return idKey;
    }
}
