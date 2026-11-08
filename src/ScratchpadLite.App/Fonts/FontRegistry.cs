using System.Windows.Media;

namespace ScratchpadLite.App.Fonts;

/// <summary>
/// Turns what Core prepared into WPF FontFamily objects, and is the only
/// place that knows how a cached font file becomes a usable family
/// (Section 6.6).
///
/// Startup order (Section 4.3): nothing here runs before the first frame.
/// Right after it, a background task reads the font index, registers every
/// cached font, then checks the font folder for new, changed and removed
/// files and processes only those, raising FontsChanged when it finishes.
/// The Windows font list is fetched when the picker first opens or at idle
/// priority, whichever comes first.
/// </summary>
/// <remarks>Phase 2.</remarks>
internal static class FontRegistry
{
    /// <summary>Completes once the font index has been read, in milliseconds.</summary>
    internal static Task IndexReady => throw new NotImplementedException("Phase 2");

    /// <summary>Raised on the UI thread when the folder check adds or changes families.</summary>
    internal static event EventHandler? FontsChanged;

    /// <summary>Starts the background index load and folder check (after the first frame).</summary>
    internal static void StartBackgroundPreparation() => throw new NotImplementedException("Phase 2");

    /// <summary>A family name to a WPF family, falling back through Substitutions.</summary>
    internal static FontFamily Resolve(string familyName) => throw new NotImplementedException("Phase 2");

    /// <summary>Every family the picker shows: Windows fonts plus the font folder.</summary>
    internal static IReadOnlyList<string> AllFamilies => throw new NotImplementedException("Phase 2");

    /// <summary>Re-runs the folder check for Tools, Reload Fonts.</summary>
    internal static void Reload() => throw new NotImplementedException("Phase 2");

    internal static void OnFontsChanged() => FontsChanged?.Invoke(null, EventArgs.Empty);
}
