using System;
using System.IO;
using System.Runtime.InteropServices;
using Avalonia.Platform;

namespace InkTag.Gui.Services;

public static class MacDockHelper
{
    [DllImport("/System/Library/Frameworks/AppKit.framework/AppKit")]
    private static extern IntPtr objc_getClass(string className);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr IntPtr_objc_msgSend(IntPtr receiver, IntPtr selector);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr IntPtr_objc_msgSend_IntPtr(IntPtr receiver, IntPtr selector, IntPtr arg);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "sel_registerName")]
    private static extern IntPtr sel_registerName(string selectorName);

    public static void TrySetDockIcon()
    {
        if (!OperatingSystem.IsMacOS()) return;

        try
        {
            string tempIconPath = Path.Combine(Path.GetTempPath(), "InkTag_DockIcon.png");
            if (!File.Exists(tempIconPath))
            {
                using var stream = AssetLoader.Open(new Uri("avares://InkTag.Gui/Assets/inktag.png"));
                using var fs = File.Create(tempIconPath);
                stream.CopyTo(fs);
            }

            var nsApplicationClass = objc_getClass("NSApplication");
            var sharedAppSel = sel_registerName("sharedApplication");
            var nsApp = IntPtr_objc_msgSend(nsApplicationClass, sharedAppSel);

            var nsStringClass = objc_getClass("NSString");
            var stringWithUtf8Sel = sel_registerName("stringWithUTF8String:");
            IntPtr utf8Ptr = Marshal.StringToHGlobalAnsi(tempIconPath);
            var nsPath = IntPtr_objc_msgSend_IntPtr(nsStringClass, stringWithUtf8Sel, utf8Ptr);

            var nsImageClass = objc_getClass("NSImage");
            var allocSel = sel_registerName("alloc");
            var nsImageAlloc = IntPtr_objc_msgSend(nsImageClass, allocSel);
            var initWithContentsOfFileSel = sel_registerName("initWithContentsOfFile:");
            var nsImage = IntPtr_objc_msgSend_IntPtr(nsImageAlloc, initWithContentsOfFileSel, nsPath);

            if (nsApp != IntPtr.Zero && nsImage != IntPtr.Zero)
            {
                var setIconSel = sel_registerName("setApplicationIconImage:");
                IntPtr_objc_msgSend_IntPtr(nsApp, setIconSel, nsImage);
            }

            if (utf8Ptr != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(utf8Ptr);
            }
        }
        catch
        {
            // Best effort; silently ignore on unsupported environments
        }
    }
}
