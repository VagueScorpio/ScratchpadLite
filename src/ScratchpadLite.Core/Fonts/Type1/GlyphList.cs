namespace ScratchpadLite.Core.Fonts.Type1;

/// <summary>
/// The Adobe Glyph List, BSD-3 licensed, embedded as a resource
/// (Section 6.6.4). Maps a Type 1 glyph name to a Unicode code point so the
/// generated font can carry a real cmap table. Loaded on first use only.
/// </summary>
/// <remarks>Phase 3.</remarks>
public static class GlyphList
{
    public static int? ToCodePoint(string glyphName) => throw new NotImplementedException("Phase 3");
}
