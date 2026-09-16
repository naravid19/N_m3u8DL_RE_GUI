#nullable enable
using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace N_m3u8DL_RE_GUI.Services;

/// <summary>
/// Recolours the native (non-client) window frame — the system title bar — to match
/// the active theme. WPF's DynamicResource palette only reaches the client area, so
/// without this the dark theme keeps a bright #FFFFFF caption.
///
/// The whole thing is best-effort: pre-Windows-10-1809 systems simply do not have a
/// dark caption, so every failure is swallowed and the default frame stays in place.
/// </summary>
internal static class TitleBarTheme
{
    // DWMWA_USE_IMMERSIVE_DARK_MODE. The attribute was introduced as 19 on build 17763
    // and moved to 20 on 18985 / Windows 11; passing the wrong number fails (HRESULT
    // != 0) instead of doing something harmful, so trying 20 then 19 is safe.
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaUseImmersiveDarkModeLegacy = 19;

    // DWMWA_NCRENDERING_POLICY and its values. Used only to force a frame refresh.
    private const int DwmwaNcRenderingPolicy = 2;
    private const int DwmNcRenderingDisabled = 1;
    private const int DwmNcRenderingUseWindowStyle = 0;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    /// <summary>
    /// Applies <paramref name="dark"/> to <paramref name="window"/>'s system frame.
    /// A window without an HWND yet (still in its constructor) is skipped silently;
    /// callers re-invoke once the handle exists.
    /// </summary>
    public static void Apply(Window window, bool dark)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
            return;

        var value = dark ? 1 : 0;
        if (DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref value, sizeof(int)) != 0
            && DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkModeLegacy, ref value, sizeof(int)) != 0)
        {
            // Neither spelling took: the OS has no dark caption (pre-1809), so there is
            // nothing to refresh either. Leave the default frame untouched.
            return;
        }

        // Windows 10 21H2 (19044) does not repaint the frame when the dark-mode attribute
        // changes, so after a runtime switch the caption keeps showing the previous colour.
        // Measured here, only recreating the window's redirection surface refreshes it:
        // re-applying the non-client rendering policy does that in two calls, while
        // SWP_FRAMECHANGED, DwmExtendFrameIntoClientArea, RedrawWindow, WM_THEMECHANGED,
        // WM_DWMNCRENDERINGCHANGED and a 1px resize all left the caption stale (minimize/
        // restore also works but visibly disrupts the window). Both calls are issued back
        // to back, with no message pump in between, so the window is never displayed
        // without DWM-drawn chrome; the policy ends on the default USEWINDOWSTYLE.
        var disabled = DwmNcRenderingDisabled;
        DwmSetWindowAttribute(handle, DwmwaNcRenderingPolicy, ref disabled, sizeof(int));
        var useWindowStyle = DwmNcRenderingUseWindowStyle;
        DwmSetWindowAttribute(handle, DwmwaNcRenderingPolicy, ref useWindowStyle, sizeof(int));
    }
}
