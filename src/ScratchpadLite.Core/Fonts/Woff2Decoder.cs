namespace ScratchpadLite.Core.Fonts;

/// <summary>
/// WOFF 2.0 to sfnt (Section 6.6.5). The whole table directory is one
/// Brotli stream, and the glyf and loca tables are stored in a transformed
/// form that has to be rebuilt. Ported from the logic of Google woff2,
/// which is MIT licensed, using built-in BrotliStream.
/// </summary>
/// <remarks>Phase 2.</remarks>
public static class Woff2Decoder
{
    public static byte[] ToSfnt(Stream woff2) => throw new NotImplementedException("Phase 2");
}
