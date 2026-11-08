namespace ScratchpadLite.Core.Fonts;

/// <summary>
/// Mac font containers to sfnt (Section 6.6.3): .dfont, AppleDouble files
/// that start with an underscore and a dot, and data-fork suitcases. The
/// sfnt resources are read out of the resource map. Files that lost their
/// resource fork on the way to Windows carry no font data and are reported
/// as such (Section 13).
/// </summary>
/// <remarks>Phase 3.</remarks>
public static class MacFontExtractor
{
    public static IReadOnlyList<byte[]> ToSfnt(Stream container) =>
        throw new NotImplementedException("Phase 3");
}
