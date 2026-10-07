using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace RevitCortex.Plugin.UI;

/// <summary>
/// Asks Windows to draw a window's native title bar in dark mode, so the
/// plugin's dark windows do not carry a white caption strip. Supported on
/// Windows 10 1809+ and Windows 11; on anything older the call fails quietly
/// and the title bar stays light.
/// </summary>
internal static class DarkTitleBar
{
    // DWMWA_USE_IMMERSIVE_DARK_MODE: 20 on Windows 10 20H1+ / 11, 19 on 1809-1909.
    private const int AttributeCurrent = 20;
    private const int AttributeLegacy = 19;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    /// <summary>Call from the window's constructor.</summary>
    public static void Apply(Window window)
    {
        if (window == null) return;
        window.SourceInitialized += (_, _) => ApplyNow(window);
    }

    private static void ApplyNow(Window window)
    {
        try
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero) return;

            int enabled = 1;
            if (DwmSetWindowAttribute(handle, AttributeCurrent, ref enabled, sizeof(int)) != 0)
                DwmSetWindowAttribute(handle, AttributeLegacy, ref enabled, sizeof(int));
        }
        catch
        {
            // Cosmetic only: never let it stop a window from opening.
        }
    }
}
