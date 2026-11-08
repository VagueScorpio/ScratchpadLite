namespace ScratchpadLite.Core.Fonts;

/// <summary>
/// Holds the prepared fonts at data/font_cache, named by content hash so
/// two copies of the same font share one cached file (Section 6.6.2).
/// User fonts are copied in; converted fonts are written as the OpenType
/// file the decoder produced.
/// </summary>
/// <remarks>Phase 2.</remarks>
public static class FontCache
{
    public static string PathFor(string sha1, string extension) =>
        throw new NotImplementedException("Phase 2");

    public static string Store(ReadOnlySpan<byte> fontBytes, string extension) =>
        throw new NotImplementedException("Phase 2");

    public static string Sha1(Stream input) => throw new NotImplementedException("Phase 2");
}
