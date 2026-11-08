namespace ScratchpadLite.Core.Fonts.Type1;

/// <summary>
/// Writes a CFF table from converted charstrings (Section 6.6.4): the
/// header, name, top dictionary, string and global subroutine indexes, the
/// charset, the encoding, and the private dictionary with the local
/// subroutine index.
/// </summary>
/// <remarks>Phase 3.</remarks>
public static class CffWriter
{
    public static byte[] Write(
        string fontName,
        IReadOnlyDictionary<string, byte[]> type2CharStrings,
        double[] fontMatrix) => throw new NotImplementedException("Phase 3");
}
