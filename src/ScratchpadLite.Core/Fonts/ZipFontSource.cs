namespace ScratchpadLite.Core.Fonts;

/// <summary>
/// Fonts inside a .zip dropped into the font folder (Section 6.6.3). Entries
/// are checked against Limits first (entry count, uncompressed total,
/// compression ratio, and names that climb out of the archive), then each
/// font entry is extracted to memory and handed to the normal pipeline.
/// </summary>
/// <remarks>Phase 2.</remarks>
public static class ZipFontSource
{
    public static IReadOnlyList<(string EntryName, byte[] Bytes, FontFileKind Kind)> Extract(
        string zipPath,
        CancellationToken ct) => throw new NotImplementedException("Phase 2");
}
