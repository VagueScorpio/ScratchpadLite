using ScratchpadLite.Core.Platform;

namespace ScratchpadLite.Core.Fonts;

/// <summary>
/// Walks the font folder recursively and sorts every file by type
/// (Section 6.6). It only lists and classifies: nothing is read, hashed or
/// converted here, so the folder check stays cheap when nothing changed.
/// </summary>
/// <remarks>
/// "Cheap" is the whole point. On a launch where nothing has changed, this is
/// all that runs: one recursive directory listing for names, sizes and times,
/// compared against the index. No font is opened and nothing is hashed, so a
/// folder of 500 fonts costs a few milliseconds on a background thread
/// (Section 6.6.1).
/// <para>
/// The one exception is the first 64 bytes of a file whose size or timestamp
/// has changed, which is what the type is decided from - extensions lie.
/// </para>
/// </remarks>
public static class FontScanner
{
    /// <summary>One file found in the font folder, with its size and timestamp.</summary>
    public sealed record Found(string Path, FontFileKind Kind, long Length, DateTime ModifiedUtc)
    {
        /// <summary>The path relative to the font folder, which is the index key.</summary>
        public string RelativePath { get; init; } = Path;
    }

    public static IReadOnlyList<Found> Scan(string fontDir, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(fontDir);

        if (!Directory.Exists(fontDir))
        {
            return [];
        }

        var found = new List<Found>();

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.System,
        };

        foreach (var path in Directory.EnumerateFiles(fontDir, "*", options))
        {
            ct.ThrowIfCancellationRequested();

            FileInfo info;
            try
            {
                info = new FileInfo(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            var relative = Path.GetRelativePath(fontDir, path);
            var kind = Classify(path);

            if (kind == FontFileKind.Ignored)
            {
                continue;
            }

            found.Add(new Found(path, kind, info.Length, info.LastWriteTimeUtc)
            {
                RelativePath = relative,
            });
        }

        Log.Debug($"font folder scan: {found.Count} candidate files");

        return found;
    }

    /// <summary>
    /// Reads the first bytes of the file and classifies it. Returns
    /// <see cref="FontFileKind.Ignored"/> rather than throwing when the file
    /// cannot be opened, since a locked file is not an error worth reporting.
    /// </summary>
    public static FontFileKind Classify(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        // Decided without reading anything, so a design file or a bitmap font
        // never costs an open.
        if (FontTypeDetector.RefusalFor(path) is not null)
        {
            return FontFileKind.Unsupported;
        }

        Span<byte> header = stackalloc byte[FontTypeDetector.HeaderBytes];
        var read = 0;

        try
        {
            using var stream = new FileStream(
                path,
                new FileStreamOptions
                {
                    Mode = FileMode.Open,
                    Access = FileAccess.Read,
                    Share = FileShare.ReadWrite | FileShare.Delete,
                });

            read = stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return FontFileKind.Ignored;
        }

        return FontTypeDetector.Classify(path, header[..read]);
    }
}
