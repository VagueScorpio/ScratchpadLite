using ScratchpadLite.Core.Platform;
using System.IO.Compression;
using ScratchpadLite.Core.Formats;

namespace ScratchpadLite.Core.Fonts;

/// <summary>
/// Fonts inside a .zip dropped into the font folder (Section 6.6.3). Entries
/// are checked against Limits first (entry count, uncompressed total,
/// compression ratio, and names that climb out of the archive), then each
/// font entry is extracted to memory and handed to the normal pipeline.
/// </summary>
/// <remarks>
/// Nothing is ever written into <c>fonts/</c>: a zip of 300 fonts stays one
/// file on disk and its contents live only as long as it takes to prepare
/// them. That is also why the entry name is carried through as
/// <c>pack.zip!inner/path.otf</c> - the index has to be able to name something
/// that is not a file.
/// </remarks>
public static class ZipFontSource
{
    /// <summary>Separates the archive from the entry inside it, in index paths.</summary>
    public const char EntrySeparator = '!';

    /// <summary>One font found inside an archive.</summary>
    public sealed record Member(string EntryName, byte[] Bytes, FontFileKind Kind);

    public static IReadOnlyList<(string EntryName, byte[] Bytes, FontFileKind Kind)> Extract(
        string zipPath,
        CancellationToken ct) =>
        Read(zipPath, ct).Select(m => (m.EntryName, m.Bytes, m.Kind)).ToList();

    /// <summary>
    /// Reads every font entry out of the archive. Non-font entries are left
    /// out; licence files are returned so the licence generator can read them.
    /// </summary>
    public static IReadOnlyList<Member> Read(string zipPath, CancellationToken ct, int depth = 0)
    {
        var members = new List<Member>();
        ForEachMember(zipPath, ct, members.Add);

        return members;
    }

    /// <summary>
    /// Hands each font entry to <paramref name="onMember"/> one at a time.
    /// </summary>
    /// <remarks>
    /// Preferred over <see cref="Read"/> for the preparation pass, because a
    /// zip may be up to 200 MB and holding every member at once would mean
    /// holding all of it. One entry is in memory at a time here.
    /// </remarks>
    public static void ForEachMember(string zipPath, CancellationToken ct, Action<Member> onMember)
    {
        ArgumentException.ThrowIfNullOrEmpty(zipPath);
        ArgumentNullException.ThrowIfNull(onMember);

        Limits.CheckFileSize(zipPath, Limits.MaxFontZipBytes);

        using var archive = ZipFile.OpenRead(zipPath);
        Limits.CheckZip(archive);

        foreach (var entry in archive.Entries)
        {
            ct.ThrowIfCancellationRequested();

            // A directory entry, which has no name after the last slash.
            if (entry.Length == 0 && entry.Name.Length == 0)
            {
                continue;
            }

            if (entry.Length > Limits.MaxFontFileBytes)
            {
                continue;
            }

            byte[] bytes;
            try
            {
                bytes = Read(entry);
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException)
            {
                Log.Warn($"{Path.GetFileName(zipPath)}!{entry.FullName} could not be read: {ex.Message}");
                continue;
            }

            var kind = FontTypeDetector.Classify(
                entry.FullName,
                bytes.AsSpan(0, Math.Min(FontTypeDetector.HeaderBytes, bytes.Length)));

            // Zips inside zips: one level only (Section 6.6.3). Deeper than
            // that is a packaging mistake, not a font collection, and following
            // it is how a decompression bomb gets its leverage.
            if (kind == FontFileKind.ZipArchive)
            {
                continue;
            }

            if (kind is FontFileKind.Ignored)
            {
                continue;
            }

            onMember(new Member(entry.FullName, bytes, kind));
        }
    }

    /// <summary>Builds the index path for an entry: <c>pack.zip!inner/font.otf</c>.</summary>
    public static string PathFor(string zipRelativePath, string entryName) =>
        $"{zipRelativePath}{EntrySeparator}{entryName}";

    private static byte[] Read(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var buffer = new MemoryStream(capacity: (int)Math.Min(entry.Length, Limits.MaxFontFileBytes));

        // Copied with its own cap rather than trusting entry.Length, which is
        // taken from the archive's own directory and need not be true.
        var remaining = Limits.MaxFontFileBytes;
        var chunk = new byte[81920];
        int read;

        while ((read = stream.Read(chunk, 0, (int)Math.Min(chunk.Length, remaining))) > 0)
        {
            buffer.Write(chunk, 0, read);
            remaining -= read;

            if (remaining <= 0)
            {
                throw new InvalidDataException("the entry is larger than its directory claims");
            }
        }

        return buffer.ToArray();
    }
}
