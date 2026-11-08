using System.Windows.Controls;

namespace ScratchpadLite.App.Editor;

/// <summary>
/// The spell checker built into WPF, which is the Windows one
/// (Section 6.11): red squiggles, suggestions in the context menu, the
/// language setting, and a custom dictionary kept as a lex file in the data
/// folder.
///
/// Startup rule (Section 2.1): it is switched on after the first frame,
/// never before, because turning it on loads the Windows spell checker.
/// </summary>
/// <remarks>Phase 7.</remarks>
internal static class SpellSupport
{
    internal static void EnableAfterFirstFrame(RichTextBox editor, string language) =>
        throw new NotImplementedException("Phase 7");

    internal static void AddToUserDictionary(string word) => throw new NotImplementedException("Phase 7");
}
