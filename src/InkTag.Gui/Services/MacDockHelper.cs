using System;
using System.IO;
using System.Runtime.InteropServices;
using Avalonia.Platform;
using InkTag.Core.Logging;

namespace InkTag.Gui.Services;

public static class MacDockHelper
{
    private const string LibObjC = "/usr/lib/libobjc.A.dylib";

    [DllImport(LibObjC, EntryPoint = "objc_getClass")]
    private static extern IntPtr objc_getClass(string className);

    [DllImport(LibObjC, EntryPoint = "sel_registerName")]
    private static extern IntPtr sel_registerName(string selectorName);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr objc_msgSend_IntPtr(IntPtr receiver, IntPtr selector, IntPtr arg);

    public static void TrySetDockIcon()
    {
        if (!OperatingSystem.IsMacOS()) return;

        try
        {
            // Ensure AppKit framework is loaded into process space
            try
            {
                NativeLibrary.Load("/System/Library/Frameworks/AppKit.framework/AppKit");
            }
            catch
            {
                // AppKit may already be dynamically linked by Avalonia
            }

            string tempIconPath = Path.Combine(Path.GetTempPath(), "InkTag.icns");
            if (!File.Exists(tempIconPath) || new FileInfo(tempIconPath).Length == 0)
            {
                if (AssetLoader.Exists(new Uri("avares://InkTag.Gui/Assets/InkTag.icns")))
                {
                    using var stream = AssetLoader.Open(new Uri("avares://InkTag.Gui/Assets/InkTag.icns"));
                    using var fs = File.Create(tempIconPath);
                    stream.CopyTo(fs);
                }
                else
                {
                    using var stream = AssetLoader.Open(new Uri("avares://InkTag.Gui/Assets/inktag.png"));
                    using var fs = File.Create(tempIconPath);
                    stream.CopyTo(fs);
                }
            }

            var nsApplicationClass = objc_getClass("NSApplication");
            if (nsApplicationClass == IntPtr.Zero)
            {
                AppLogger.LogWarning("[MacDockHelper] Failed to get NSApplication class.");
                return;
            }

            var sharedAppSel = sel_registerName("sharedApplication");
            var nsApp = objc_msgSend(nsApplicationClass, sharedAppSel);
            if (nsApp == IntPtr.Zero)
            {
                AppLogger.LogWarning("[MacDockHelper] Failed to get NSApplication.sharedApplication.");
                return;
            }

            var nsStringClass = objc_getClass("NSString");
            var stringWithUtf8Sel = sel_registerName("stringWithUTF8String:");
            IntPtr utf8Ptr = Marshal.StringToHGlobalAnsi(tempIconPath);
            var nsPath = objc_msgSend_IntPtr(nsStringClass, stringWithUtf8Sel, utf8Ptr);

            var nsImageClass = objc_getClass("NSImage");
            var allocSel = sel_registerName("alloc");
            var nsImageAlloc = objc_msgSend(nsImageClass, allocSel);
            var initWithContentsOfFileSel = sel_registerName("initWithContentsOfFile:");
            var nsImage = objc_msgSend_IntPtr(nsImageAlloc, initWithContentsOfFileSel, nsPath);

            if (utf8Ptr != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(utf8Ptr);
            }

            if (nsImage != IntPtr.Zero)
            {
                var setIconSel = sel_registerName("setApplicationIconImage:");
                objc_msgSend_IntPtr(nsApp, setIconSel, nsImage);

                // Explicitly update and refresh the NSDockTile
                var dockTileSel = sel_registerName("dockTile");
                var dockTile = objc_msgSend(nsApp, dockTileSel);
                if (dockTile != IntPtr.Zero)
                {
                    var nsImageViewClass = objc_getClass("NSImageView");
                    if (nsImageViewClass != IntPtr.Zero)
                    {
                        var initSel = sel_registerName("init");
                        var imageViewAlloc = objc_msgSend(nsImageViewClass, allocSel);
                        var imageView = objc_msgSend(imageViewAlloc, initSel);
                        if (imageView != IntPtr.Zero)
                        {
                            var setImageSel = sel_registerName("setImage:");
                            objc_msgSend_IntPtr(imageView, setImageSel, nsImage);

                            var setContentViewSel = sel_registerName("setContentView:");
                            objc_msgSend_IntPtr(dockTile, setContentViewSel, imageView);

                            var displaySel = sel_registerName("display");
                            objc_msgSend(dockTile, displaySel);
                        }
                    }
                }

                AppLogger.LogInfo("[MacDockHelper] Successfully set macOS application Dock icon and refreshed dockTile.");
            }
            else
            {
                AppLogger.LogWarning($"[MacDockHelper] Failed to initialize NSImage from '{tempIconPath}'.");
            }
        }
        catch (Exception ex)
        {
            AppLogger.LogWarning($"[MacDockHelper] Could not set macOS Dock icon: {ex.Message}");
        }
    }
}
