using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace ScratchpadLite.App.UI.Themes;

/// <summary>
/// Makes the window's title bar dark to match the dark theme, through the DWM
/// immersive dark mode window attribute (Section 6.16).
/// </summary>
/// <remarks>
/// This is a nicety, not a feature: where the attribute is unavailable — older
/// Windows 10 builds, or before the window has a handle — the call does nothing
/// and the title bar simply stays light. Nothing here is allowed to throw,
/// because a cosmetic touch must never be able to take the window down.
/// </remarks>
internal static class DarkTitleBar
{
    /// <summary>
    /// <c>DWMWA_USE_IMMERSIVE_DARK_MODE</c>. Attribute 20 since Windows 10
    /// build 18985; the earlier 1809 builds used 19, so both are tried.
    /// </summary>
    private const int UseImmersiveDarkMode = 20;

    private const int UseImmersiveDarkModeBefore20H1 = 19;

    internal static void Apply(Window window, bool dark)
    {
        try
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero)
            {
                // No handle yet: the window has not been shown. Re-applied from
                // SourceInitialized by the caller if it matters.
                return;
            }

            var value = dark ? 1 : 0;

            if (DwmSetWindowAttribute(handle, UseImmersiveDarkMode, ref value, sizeof(int)) != 0)
            {
                DwmSetWindowAttribute(handle, UseImmersiveDarkModeBefore20H1, ref value, sizeof(int));
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // dwmapi.dll is present on every supported Windows, but a cosmetic
            // call is not worth a crash if it somehow is not.
        }
    }

    [DllImport("dwmapi.dll", SetLastError = true)]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd,
        int attribute,
        ref int value,
        int valueSize);
}
