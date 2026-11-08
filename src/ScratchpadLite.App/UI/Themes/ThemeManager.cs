using ScratchpadLite.Core.Platform;

namespace ScratchpadLite.App.UI.Themes;

/// <summary>
/// Light, dark and follow-Windows themes (Section 6.16). Follow-Windows
/// reads the system app theme and listens for changes. Switching swaps one
/// merged resource dictionary, so it costs nothing at startup beyond
/// loading the one dictionary in use.
///
/// Speed check (Section 2.1): if the WPF Fluent theme costs more than one
/// hundred and fifty milliseconds at startup, the hand-made dictionaries
/// are used instead. Phase 0 measures this.
/// </summary>
/// <remarks>Phase 0.</remarks>
internal static class ThemeManager
{
    internal static void Apply(ThemeChoice choice) => throw new NotImplementedException("Phase 0");

    /// <summary>True when Windows is set to a dark app theme.</summary>
    internal static bool WindowsPrefersDark() => throw new NotImplementedException("Phase 0");
}
