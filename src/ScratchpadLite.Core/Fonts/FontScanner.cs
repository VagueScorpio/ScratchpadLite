namespace ScratchpadLite.Core.Fonts;

/// <summary>
/// Walks the font folder recursively and sorts every file by type
/// (Section 6.6). It only lists and classifies: nothing is read, hashed or
/// converted here, so the folder check stays cheap when nothing changed.
/// </summary>
/// <remarks>Phase 2.</remarks>
public static class FontScanner
{
    /// <summary>One file found in the font folder, with its size and timestamp.</summary>
    public sealed record Found(string Path, FontFileKind Kind, long Length, DateTime ModifiedUtc);

    public static IReadOnlyList<Found> Scan(string fontDir, CancellationToken ct) =>
        throw new NotImplementedException("Phase 2");

    public static FontFileKind Classify(string path) => throw new NotImplementedException("Phase 2");
}
