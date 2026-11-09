using System.Security.Cryptography;
using ScratchpadLite.Core.Platform;

namespace ScratchpadLite.Core.Fonts;

/// <summary>
/// Holds the prepared fonts at data/font_cache, named by content hash so
/// two copies of the same font share one cached file (Section 6.6.2).
/// User fonts are copied in; converted fonts are written as the OpenType
/// file the decoder produced.
/// </summary>
/// <remarks>
/// Why a copy exists at all: WPF keeps a font file open for as long as the
/// family is registered, so loading straight out of <c>fonts/</c> would stop
/// the user deleting or replacing their own files while the editor is open.
/// Naming by hash means a changed font gets a new cache file rather than
/// fighting over the old one, and two files holding the same font share one
/// copy.
/// </remarks>
public static class FontCache
{
    /// <summary>
    /// Where embedded fonts from saved documents live (Section 7.4). Never
    /// touched by the folder check, which is why it is a subfolder rather than
    /// a naming convention.
    /// </summary>
    public const string EmbeddedFolderName = "embedded";

    public static string PathFor(string sha1, string extension)
    {
        ArgumentException.ThrowIfNullOrEmpty(sha1);

        return Path.Combine(AppPaths.FontCacheDir, sha1 + extension);
    }

    /// <summary>
    /// Writes the font to the cache unless an identical copy is already there,
    /// and returns the file name (not the full path) for the index.
    /// </summary>
    public static string Store(ReadOnlySpan<byte> fontBytes, string extension)
    {
        var name = Sha1(fontBytes) + extension;
        var path = Path.Combine(AppPaths.FontCacheDir, name);

        // The hash is of the content, so a file that is already there is the
        // same font and rewriting it would only risk disturbing a copy WPF
        // currently has open.
        if (File.Exists(path))
        {
            return name;
        }

        Directory.CreateDirectory(AppPaths.FontCacheDir);

        // Written under a temporary name and moved into place, so a cache file
        // is never half-written if the app is killed mid-copy - a half-written
        // font would be found by name on the next launch and fail to load.
        var temp = $"{path}.{Guid.NewGuid():N}.tmp";

        try
        {
            File.WriteAllBytes(temp, fontBytes);
            File.Move(temp, path, overwrite: true);
        }
        catch (IOException) when (File.Exists(path))
        {
            // Another copy of the app won the race. Its file is identical.
            TryDelete(temp);
        }
        catch
        {
            TryDelete(temp);
            throw;
        }

        return name;
    }

    /// <summary>Deletes a cache file that nothing refers to any more.</summary>
    public static void Remove(string cachedFileName)
    {
        if (string.IsNullOrEmpty(cachedFileName))
        {
            return;
        }

        // Guards against an index entry whose name has a path in it reaching
        // outside the cache folder.
        var name = Path.GetFileName(cachedFileName);
        if (name != cachedFileName)
        {
            Log.Warn($"refusing to delete a cache entry with a path in its name: {cachedFileName}");
            return;
        }

        TryDelete(Path.Combine(AppPaths.FontCacheDir, name));
    }

    public static string Sha1(Stream input)
    {
        ArgumentNullException.ThrowIfNull(input);

        return Convert.ToHexStringLower(SHA1.HashData(input));
    }

    public static string Sha1(ReadOnlySpan<byte> bytes)
    {
        Span<byte> hash = stackalloc byte[SHA1.HashSizeInBytes];
        SHA1.HashData(bytes, hash);

        return Convert.ToHexStringLower(hash);
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A font WPF still has open cannot be deleted, which is expected
            // and harmless: it is replaced on a later launch.
            Log.Debug($"could not delete {Path.GetFileName(path)}: {ex.Message}");
        }
    }
}
