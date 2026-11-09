using System.IO.Compression;
using System.Xml;

namespace ScratchpadLite.Core.Formats;

/// <summary>
/// Opened files are untrusted (Section 7.5): size caps, zip-bomb guards, path
/// traversal checks, and safe XML settings.
/// </summary>
/// <remarks>
/// Phase 2 fills in the parts the font folder needs - the size caps and the
/// zip guards, because a .zip of fonts is the first untrusted archive the app
/// opens. The document formats use the same methods from Phase 4.
/// </remarks>
public static class Limits
{
    public const long MaxFileBytes = 200L * 1024 * 1024;
    public const int MaxZipEntries = 10_000;
    public const long MaxUncompressedBytes = 1024L * 1024 * 1024;
    public const long MaxDecodedFontBytes = 100L * 1024 * 1024;

    /// <summary>A single font file this large is not worth opening (Section 6.6.2).</summary>
    public const long MaxFontFileBytes = 50L * 1024 * 1024;

    /// <summary>A .zip of fonts this large is not worth opening (Section 6.6.2).</summary>
    public const long MaxFontZipBytes = 200L * 1024 * 1024;

    /// <summary>
    /// An entry that inflates by more than this is treated as a zip bomb. Font
    /// and text files do not reach it; a file of zeros does.
    /// </summary>
    public const long MaxCompressionRatio = 200;

    /// <summary>Throws when the file is larger than the cap.</summary>
    public static void CheckFileSize(string path, long maxBytes = MaxFileBytes)
    {
        var length = new FileInfo(path).Length;
        if (length > maxBytes)
        {
            throw new FileTooLargeException(
                $"{Path.GetFileName(path)} is {length / (1024 * 1024)} MB, over the "
                + $"{maxBytes / (1024 * 1024)} MB limit");
        }
    }

    /// <summary>
    /// Checks entry count, total uncompressed size, per-entry compression ratio,
    /// and entry names that climb out of the archive or are absolute.
    /// </summary>
    public static void CheckZip(ZipArchive archive)
    {
        ArgumentNullException.ThrowIfNull(archive);

        if (archive.Entries.Count > MaxZipEntries)
        {
            throw new FileTooLargeException(
                $"the archive holds {archive.Entries.Count} entries, over the {MaxZipEntries} limit");
        }

        long total = 0;
        foreach (var entry in archive.Entries)
        {
            CheckEntryName(entry.FullName);

            total += entry.Length;
            if (total > MaxUncompressedBytes)
            {
                throw new FileTooLargeException(
                    $"the archive unpacks to more than {MaxUncompressedBytes / (1024 * 1024)} MB");
            }

            // A directory entry and a stored (uncompressed) entry both have a
            // ratio of about 1, so only a genuinely suspicious entry trips this.
            if (entry.CompressedLength > 0
                && entry.Length / entry.CompressedLength > MaxCompressionRatio)
            {
                throw new FileTooLargeException(
                    $"'{entry.FullName}' inflates {entry.Length / entry.CompressedLength} times, "
                    + "which looks like a zip bomb");
            }
        }
    }

    /// <summary>
    /// Throws when a decoder's output would be over the cap. Checked before the
    /// buffer is allocated, since the point is not to allocate it.
    /// </summary>
    public static void CheckDecodedFontSize(long bytes)
    {
        if (bytes is < 0 or > MaxDecodedFontBytes)
        {
            throw new FileTooLargeException(
                $"the decoded font would be {bytes} bytes, over the "
                + $"{MaxDecodedFontBytes / (1024 * 1024)} MB limit");
        }
    }

    /// <summary>
    /// Rejects an entry name that is absolute or climbs out of the archive.
    /// Nothing here extracts to disk, but entry names are also shown in the
    /// Font Report and used as index keys, so they are checked anyway.
    /// </summary>
    public static void CheckEntryName(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return;
        }

        if (Path.IsPathRooted(name)
            || name.Contains(':', StringComparison.Ordinal)
            || name.Split('/', '\\').Contains(".."))
        {
            throw new FileTooLargeException($"'{name}' is not a safe entry name");
        }
    }

    /// <summary>Reader settings with DTD processing prohibited and no resolver.</summary>
    public static XmlReaderSettings SafeXml() => new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        CloseInput = false,
    };
}

/// <summary>
/// A file, archive or decoder output over one of the Section 7.5 limits. It is
/// a refusal to read something, not a failure, so it carries a message meant
/// for the Font Report or the user rather than a stack trace.
/// </summary>
public sealed class FileTooLargeException : Exception
{
    public FileTooLargeException(string message) : base(message)
    {
    }
}
