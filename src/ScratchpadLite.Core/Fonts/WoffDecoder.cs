namespace ScratchpadLite.Core.Fonts;

/// <summary>
/// WOFF 1.0 to sfnt (Section 6.6.5). Each table is stored zlib-deflated,
/// so built-in ZLibStream inflates it and SfntBuilder reassembles the
/// tables. Every offset and length is bounds-checked and the decoded size
/// is capped (Section 7.5).
/// </summary>
/// <remarks>Phase 2.</remarks>
public static class WoffDecoder
{
    public static byte[] ToSfnt(Stream woff) => throw new NotImplementedException("Phase 2");
}
