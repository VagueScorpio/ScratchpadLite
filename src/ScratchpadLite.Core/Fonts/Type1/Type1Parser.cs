namespace ScratchpadLite.Core.Fonts.Type1;

/// <summary>
/// Reads an Adobe Type 1 font (Section 6.6.4). Handles both the PFB segment
/// headers and the plain PFA hex form, decrypts the eexec private section
/// and then each charstring, and returns the font matrix, encoding,
/// subroutines and glyph charstrings.
/// </summary>
/// <remarks>Phase 3.</remarks>
public static class Type1Parser
{
    /// <summary>The parts of a Type 1 font the converter needs.</summary>
    public sealed record Font(
        string FontName,
        IReadOnlyDictionary<string, byte[]> CharStrings,
        IReadOnlyList<byte[]> Subroutines,
        IReadOnlyList<string> Encoding,
        double[] FontMatrix);

    public static Font Parse(Stream input) => throw new NotImplementedException("Phase 3");
}
