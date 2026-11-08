namespace ScratchpadLite.Core.Fonts;

/// <summary>
/// Type 42 to sfnt (Section 6.6.3). A .t42 file is a PostScript wrapper
/// around a TrueType font: the sfnt bytes live in the hex strings of the
/// sfnts array and are reassembled in order.
/// </summary>
/// <remarks>Phase 3.</remarks>
public static class Type42Extractor
{
    public static byte[] ToSfnt(Stream t42) => throw new NotImplementedException("Phase 3");
}
