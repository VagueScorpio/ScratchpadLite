using System.IO.Compression;
using System.Xml;

namespace ScratchpadLite.Core.Formats;

/// <summary>
/// Opened files are untrusted (Section 7.5): size caps, zip-bomb guards, path
/// traversal checks, and safe XML settings.
/// </summary>
/// <remarks>Phase 4.</remarks>
public static class Limits
{
    public const long MaxFileBytes = 200L * 1024 * 1024;
    public const int MaxZipEntries = 10_000;
    public const long MaxUncompressedBytes = 1024L * 1024 * 1024;
    public const long MaxDecodedFontBytes = 100L * 1024 * 1024;

    /// <summary>Throws when the file is larger than the cap.</summary>
    public static void CheckFileSize(string path, long maxBytes = MaxFileBytes) =>
        throw new NotImplementedException("Phase 4");

    /// <summary>
    /// Checks entry count, total uncompressed size, per-entry compression ratio,
    /// and entry names that climb out of the archive or are absolute.
    /// </summary>
    public static void CheckZip(ZipArchive archive) => throw new NotImplementedException("Phase 4");

    /// <summary>Reader settings with DTD processing prohibited and no resolver.</summary>
    public static XmlReaderSettings SafeXml() => new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        CloseInput = false,
    };
}
