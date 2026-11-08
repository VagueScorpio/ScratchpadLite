namespace ScratchpadLite.Core.Fonts;

/// <summary>
/// The card catalog (Section 6.6.1), kept at data/font_cache/index.json.
/// One entry per source file records its path, size, timestamp and hash,
/// the cached file it produced, and the family and face names inside it.
/// A file whose size and timestamp are unchanged is never re-read,
/// re-hashed or re-copied, so later launches cost nothing. Loading the
/// index is what IndexReady waits on, and it takes milliseconds.
/// </summary>
/// <remarks>Phase 2.</remarks>
public sealed class FontIndex
{
    /// <summary>One catalog card.</summary>
    public sealed record Entry(
        string SourcePath,
        long Length,
        DateTime ModifiedUtc,
        string Sha1,
        string CachedFile,
        IReadOnlyList<string> Families,
        string? FailureReason);

    public static FontIndex Load(string indexPath) => throw new NotImplementedException("Phase 2");

    public void Save(string indexPath) => throw new NotImplementedException("Phase 2");

    /// <summary>True when size and timestamp match the recorded card.</summary>
    public bool IsUnchanged(FontScanner.Found found) => throw new NotImplementedException("Phase 2");
}
