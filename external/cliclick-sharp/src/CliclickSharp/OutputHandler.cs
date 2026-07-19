using System.Runtime.InteropServices;
using System.Text;
using CliclickSharp.Native;

namespace CliclickSharp;

public class OutputHandler : IDisposable
{
    private readonly StreamWriter? _writer;
    private readonly bool _useClipboard;
    private readonly bool _useStdout;
    private readonly bool _useStderr;
    private readonly StringBuilder? _clipboardContent;

    public OutputHandler(string destination)
    {
        switch (destination.ToLowerInvariant())
        {
            case "stdout":
                _useStdout = true;
                break;
            case "stderr":
                _useStderr = true;
                break;
            case "clipboard":
                _useClipboard = true;
                _clipboardContent = new StringBuilder();
                break;
            default:
                _writer = new StreamWriter(destination, true, Encoding.UTF8);
                break;
        }
    }

    public void WriteLine(string text)
    {
        if (_useStdout)
        {
            Console.Out.WriteLine(text);
        }
        else if (_useStderr)
        {
            Console.Error.WriteLine(text);
        }
        else if (_useClipboard)
        {
            _clipboardContent!.AppendLine(text);
        }
        else
        {
            _writer!.WriteLine(text);
            _writer.Flush();
        }
    }

    public void Flush()
    {
        if (_useClipboard && _clipboardContent!.Length > 0)
        {
            SetClipboardText(_clipboardContent.ToString());
        }
        _writer?.Flush();
    }

    private static void SetClipboardText(string text)
    {
        IntPtr pool = ObjCRuntime.objc_autoreleasePoolPush();
        try
        {
            IntPtr nsStringClass = ObjCRuntime.objc_getClass("NSString");
            IntPtr strSel = ObjCRuntime.sel_registerName("stringWithUTF8String:");
            IntPtr nsString = ObjCRuntime.objc_msgSend(nsStringClass, strSel, Marshal.StringToHGlobalAuto(text));

            IntPtr pbClass = ObjCRuntime.objc_getClass("NSPasteboard");
            IntPtr generalSel = ObjCRuntime.sel_registerName("generalPasteboard");
            IntPtr pb = ObjCRuntime.objc_msgSend(pbClass, generalSel);

            IntPtr declareSel = ObjCRuntime.sel_registerName("declareTypes:owner:");
            IntPtr nsArrayClass = ObjCRuntime.objc_getClass("NSArray");
            IntPtr arraySel = ObjCRuntime.sel_registerName("arrayWithObject:");
            IntPtr nsStringPboardType = ObjCRuntime.objc_msgSend(nsStringClass, strSel, Marshal.StringToHGlobalAuto("NSPasteboardTypeString"));
            IntPtr types = ObjCRuntime.objc_msgSend(nsArrayClass, arraySel, nsStringPboardType);
            ObjCRuntime.objc_msgSend(pb, declareSel, types, IntPtr.Zero);

            IntPtr setStrSel = ObjCRuntime.sel_registerName("setString:forType:");
            ObjCRuntime.objc_msgSend(pb, setStrSel, nsString, nsStringPboardType);
        }
        finally
        {
            ObjCRuntime.objc_autoreleasePoolPop(pool);
        }
    }

    public void Dispose()
    {
        Flush();
        _writer?.Dispose();
    }
}
