namespace ScratchpadLite.Core.Fonts;

/// <summary>
/// Picks the closest available family when a document asks for a font that
/// is not installed and not in the font folder (Section 6.6). The original
/// name is kept on the run so saving writes it back unchanged, and the
/// missing-font bar lists what was substituted.
/// </summary>
/// <remarks>Phase 2.</remarks>
public static class Substitutions
{
    public static string? Resolve(string requestedFamily, IReadOnlyCollection<string> available) =>
        throw new NotImplementedException("Phase 2");
}
