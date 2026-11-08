using System.Windows;

namespace ScratchpadLite.App.UI.Themes;

/// <summary>
/// Makes the window title bar dark to match a dark theme, through the
/// immersive dark mode window attribute (Section 6.16). It is a nicety:
/// where the attribute is unavailable the call is ignored and the title bar
/// simply stays light.
/// </summary>
/// <remarks>Phase 8.</remarks>
internal static class DarkTitleBar
{
    internal static void Apply(Window window, bool dark) => throw new NotImplementedException("Phase 8");
}
