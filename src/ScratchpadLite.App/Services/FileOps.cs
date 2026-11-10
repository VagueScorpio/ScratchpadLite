using System.IO;
using ScratchpadLite.Core.Platform;

namespace ScratchpadLite.App.Services;

/// <summary>
/// Safe saving (Section 6.14). The new content goes to a temporary file in
/// the same folder, is flushed to disk, and only then replaces the original
/// through a replace that keeps the old copy as a bak file when that setting
/// is on. An interrupted save therefore never leaves a half-written
/// document.
///
/// Read-only files, files on a disconnected network share and files another
/// program has locked each produce a plain sentence with a Details expander
/// (Section 9), not an exception dialog.
/// </summary>
/// <remarks>
/// The temporary file goes in the <em>same folder</em> as the target, not in
/// the system temp folder. That is the whole point: a move within one volume
/// is a directory operation the filesystem completes or does not, while a move
/// across volumes is a copy, and a copy can be interrupted half way - which is
/// the failure this is meant to prevent.
/// </remarks>
internal static class FileOps
{
    /// <summary>What a file looked like when it was opened (Section 6.14).</summary>
    /// <remarks>
    /// Kept so a save can notice that something else changed the file in the
    /// meantime and ask before overwriting someone else's work.
    /// </remarks>
    internal sealed record Stamp(DateTime WriteTimeUtc, long Length)
    {
        internal static Stamp? Of(string path)
        {
            try
            {
                var info = new FileInfo(path);

                return info.Exists ? new Stamp(info.LastWriteTimeUtc, info.Length) : null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }
    }

    /// <summary>Why a save could not be done, in words meant for the user.</summary>
    internal sealed class SaveException(string message, Exception? inner = null)
        : Exception(message, inner);

    /// <summary>
    /// Writes a file so that an interruption cannot damage the old one.
    /// </summary>
    /// <param name="path">The file to write.</param>
    /// <param name="writeContent">Writes the new content to the stream given.</param>
    /// <param name="keepBak">Keep the previous version as <c>.bak</c>.</param>
    internal static void AtomicWrite(string path, Action<Stream> writeContent, bool keepBak)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(writeContent);

        var folder = Path.GetDirectoryName(Path.GetFullPath(path));

        if (string.IsNullOrEmpty(folder))
        {
            throw new SaveException($"'{path}' is not a path this can save to.");
        }

        try
        {
            Directory.CreateDirectory(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw Explain(path, ex);
        }

        var temp = Path.Combine(folder, $"{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");

        try
        {
            using (var stream = new FileStream(
                       temp,
                       new FileStreamOptions
                       {
                           Mode = FileMode.CreateNew,
                           Access = FileAccess.Write,
                           Share = FileShare.None,
                       }))
            {
                writeContent(stream);

                // Flush(true) asks the OS to put it on the disk rather than
                // leaving it in a write cache, which is what makes the replace
                // below safe across a power loss as well as a crash.
                stream.Flush(flushToDisk: true);
            }

            Swap(temp, path, keepBak);
        }
        catch (Exception ex)
        {
            TryDelete(temp);

            throw ex is SaveException ? ex : Explain(path, ex);
        }
    }

    private static void Swap(string temp, string path, bool keepBak)
    {
        if (!File.Exists(path))
        {
            File.Move(temp, path);
            return;
        }

        var bak = path + ".bak";

        try
        {
            // File.Replace is one operation and takes the backup in the same
            // call, which is why it is preferred over delete-then-move: there
            // is no instant at which neither file exists.
            File.Replace(temp, path, keepBak ? bak : null, ignoreMetadataErrors: true);
        }
        catch (PlatformNotSupportedException)
        {
            // Replace needs a filesystem that supports it. A network share or
            // a FAT-formatted USB stick may not, and a portable app lives on
            // USB sticks, so the fallback matters.
            if (keepBak)
            {
                TryCopy(path, bak);
            }

            File.Move(temp, path, overwrite: true);
        }
        catch (IOException) when (!File.Exists(temp))
        {
            // Replace reported a failure but the temporary file is gone, which
            // means it got far enough. Nothing to undo.
        }
    }

    /// <summary>
    /// Whether the file changed on disk since it was opened
    /// (Section 6.14).
    /// </summary>
    internal static bool ChangedOnDisk(string path, Stamp? opened)
    {
        if (opened is null)
        {
            return false;        // it did not exist when opened
        }

        var now = Stamp.Of(path);

        return now is not null && now != opened;
    }

    /// <summary>
    /// Turns a file exception into something worth showing a person
    /// (Section 9).
    /// </summary>
    internal static SaveException Explain(string path, Exception ex)
    {
        var name = Path.GetFileName(path);

        return ex switch
        {
            UnauthorizedAccessException when IsReadOnly(path) => new SaveException(
                $"{name} is marked read-only. Clear that in its Properties, or save under a new name.",
                ex),

            UnauthorizedAccessException => new SaveException(
                $"Windows would not let this save {name}. "
                + "It may be in a protected folder - try saving somewhere else.",
                ex),

            // Sharing violation and lock violation: another program has it.
            IOException io when io.HResult is HResultSharingViolation or HResultLockViolation =>
                new SaveException(
                    $"{name} is open in another program. Close it there, or Save As a new name.",
                    ex),

            IOException io when io.HResult == HResultDiskFull => new SaveException(
                $"There is not enough room on the disk to save {name}.",
                ex),

            DirectoryNotFoundException => new SaveException(
                $"The folder for {name} is not there any more. "
                + "If it is on a network drive or a USB stick, check it is still connected.",
                ex),

            PathTooLongException => new SaveException(
                $"The path to {name} is too long for Windows. Try a shorter name or folder.",
                ex),

            IOException => new SaveException($"{name} could not be saved: {ex.Message}", ex),

            _ => new SaveException($"{name} could not be saved.", ex),
        };
    }

    // The Win32 error codes, as HRESULTs, that matter for a clear message.
    private const int HResultSharingViolation = unchecked((int)0x80070020);
    private const int HResultLockViolation = unchecked((int)0x80070021);
    private const int HResultDiskFull = unchecked((int)0x80070070);

    private static bool IsReadOnly(string path)
    {
        try
        {
            return File.Exists(path) && new FileInfo(path).IsReadOnly;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void TryCopy(string from, string to)
    {
        try
        {
            File.Copy(from, to, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A backup that cannot be written must not stop the save itself.
            Log.Warn($"the .bak copy could not be written: {ex.Message}");
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"a temporary save file was left behind: {path} ({ex.Message})");
        }
    }
}
