using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Threading;
using ScratchpadLite.Core.Model;
using ScratchpadLite.Core.Platform;

namespace ScratchpadLite.App.Services;

/// <summary>
/// Crash recovery, always on and never touching the real file
/// (Section 6.13.1). Every open document is written to its own file in the
/// data recovery folder on a timer, as IR JSON. A clean exit deletes them.
/// Anything left over means the last run did not finish, so the Recovery
/// dialog offers those documents on the next launch.
///
/// This is separate from AutoSave, which does write the real file.
/// The check runs after the first frame (Section 4.3 step 9).
/// </summary>
/// <remarks>
/// <para>
/// The whole design rests on one question: how does a launch tell "a copy of
/// this app is running right now and those files are its working state" from
/// "a copy died and those files are somebody's unsaved work"? A timestamp
/// cannot answer it and a process-id file cannot either, because process ids
/// are reused. The answer is a lock file held open with
/// <see cref="FileShare.None"/> for the whole life of the process: Windows
/// releases it when the process ends, however it ends, so a folder whose lock
/// file can be opened is a folder whose owner is gone. Nothing has to be
/// cleaned up by the thing that crashed.
/// </para>
/// <para>
/// Untitled documents are included, which is the point of recovery being
/// separate from AutoSave: AutoSave can do nothing for a document with no file
/// name, and that is exactly the document whose loss hurts most.
/// </para>
/// </remarks>
internal sealed class Recovery
{
    /// <summary>The lock file that marks a recovery folder as live.</summary>
    internal const string LockFileName = "session.lock";

    private readonly string _root;
    private readonly string _sessionDir;

    private FileStream? _lock;
    private DispatcherTimer? _timer;

    /// <param name="root">The recovery folder, normally <c>data/recovery</c>.</param>
    /// <param name="sessionId">
    /// Names this run's subfolder. A GUID in the application; a fixed string in
    /// a test that wants to find the folder again.
    /// </param>
    internal Recovery(string root, string sessionId)
    {
        ArgumentException.ThrowIfNullOrEmpty(root);
        ArgumentException.ThrowIfNullOrEmpty(sessionId);

        _root = root;
        _sessionDir = Path.Combine(root, sessionId);
    }

    /// <summary>This run's folder. Created by <see cref="Claim"/>.</summary>
    internal string SessionDir => _sessionDir;

    /// <summary>Raised on each timer tick, for the window to walk and save its documents.</summary>
    /// <remarks>
    /// The walk has to be on the UI thread and only the window knows which
    /// documents there are, so the timer asks rather than tells.
    /// </remarks>
    internal event EventHandler? DueForWrite;

    /// <summary>
    /// Creates this run's folder and takes its lock. Call once, after the first
    /// frame.
    /// </summary>
    /// <returns>False when the folder or the lock could not be made.</returns>
    internal bool Claim()
    {
        try
        {
            Directory.CreateDirectory(_sessionDir);

            _lock = new FileStream(
                Path.Combine(_sessionDir, LockFileName),
                new FileStreamOptions
                {
                    Mode = FileMode.Create,
                    Access = FileAccess.Write,
                    Share = FileShare.None,
                });

            // The contents are for a person reading the folder, not for the
            // check: what makes this work is the handle, which Windows releases
            // when the process ends however it ends.
            var note = System.Text.Encoding.UTF8.GetBytes(
                $"{Environment.ProcessId}{Environment.NewLine}{DateTime.UtcNow:O}{Environment.NewLine}");

            _lock.Write(note);
            _lock.Flush(flushToDisk: true);

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                      or ArgumentException or NotSupportedException)
        {
            // ArgumentException belongs here: the data folder can come from a
            // settings file or a command line, so a path Windows will not take
            // at all is a thing that can happen, and it must leave the
            // application running without recovery rather than not running.
            Log.Warn($"the recovery folder could not be locked, so recovery is off this run ({ex.Message})");

            _lock = null;

            return false;
        }
    }

    internal void Start(int intervalSeconds)
    {
        Stop();

        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(Math.Clamp(intervalSeconds, MinIntervalSeconds, MaxIntervalSeconds)),
        };

        _timer.Tick += (_, _) => DueForWrite?.Invoke(this, EventArgs.Empty);
        _timer.Start();
    }

    /// <summary>The range the Preferences spinner offers (Section 6.13.1).</summary>
    internal const int MinIntervalSeconds = 15;

    internal const int MaxIntervalSeconds = 30 * 60;

    internal void Stop()
    {
        _timer?.Stop();
        _timer = null;
    }

    /// <summary>
    /// Writes one document's recovery file, off the UI thread.
    /// </summary>
    /// <remarks>
    /// The caller walks the FlowDocument - that part has to be on the UI thread
    /// - and this takes the IR from there. Serializing and writing a 300-page
    /// document is not something to do between two keystrokes.
    /// </remarks>
    internal Task WriteAsync(DocumentSession session, SpDocument ir)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(ir);

        var record = RecordFor(session, ir);
        var path = FileFor(session);

        session.LastRecoveryWriteUtc = DateTime.UtcNow;

        return Task.Run(() => WriteFile(path, record));
    }

    /// <summary>
    /// Writes one document's recovery file now, on the calling thread.
    /// </summary>
    /// <remarks>
    /// For the crash handlers (Section 6.13.1). A process that is on its way
    /// down cannot wait for a background task, so this one blocks.
    /// </remarks>
    internal void WriteNow(DocumentSession session, SpDocument ir)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(ir);

        session.LastRecoveryWriteUtc = DateTime.UtcNow;

        WriteFile(FileFor(session), RecordFor(session, ir));
    }

    private string FileFor(DocumentSession session) =>
        Path.Combine(_sessionDir, $"{session.Id:N}.json");

    private static Saved RecordFor(DocumentSession session, SpDocument ir) =>
        new(
            session.Id.ToString("N"),
            session.FilePath,
            session.Title,
            session.FormatName,
            DateTime.UtcNow,
            ir);

    private static void WriteFile(string path, Saved record)
    {
        try
        {
            FileOps.AtomicWrite(
                path,
                stream => JsonSerializer.Serialize(stream, record, RecoveryJson.Default.Saved),
                keepBak: false);
        }
        catch (Exception ex) when (ex is FileOps.SaveException or IOException or UnauthorizedAccessException
                                      or JsonException or ArgumentException or NotSupportedException)
        {
            // A recovery write that fails is worth a line in the log and
            // nothing else. It is a safety net, and a safety net that
            // interrupts the user to say it is not there has made things worse.
            Log.Warn($"a recovery file could not be written ({ex.Message})");
        }
    }

    /// <summary>Deletes this document's recovery file, after it has been saved.</summary>
    internal void Forget(DocumentSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        try
        {
            File.Delete(FileFor(session));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"a recovery file could not be deleted ({ex.Message})");
        }
    }

    /// <summary>
    /// Releases the lock and deletes this run's folder (Section 6.13.1).
    /// </summary>
    internal void ClearOnCleanExit()
    {
        Stop();

        _lock?.Dispose();
        _lock = null;

        Discard(_sessionDir);
    }

    // --------------------------------------------------------- what was left --

    /// <summary>One document a previous run left behind.</summary>
    /// <param name="File">The recovery file itself, for reading or discarding.</param>
    internal sealed record Leftover(
        string File,
        string Folder,
        string Title,
        string? OriginalPath,
        string FormatName,
        DateTime SavedUtc);

    /// <summary>
    /// Everything a previous run left behind, newest first (Section 6.13.1).
    /// </summary>
    /// <remarks>
    /// A folder counts as abandoned when its lock file can be opened for
    /// writing, or when it has no lock file at all - which is what a process
    /// killed in its first second leaves. This run's own folder is skipped by
    /// name, because its lock is held by this process and a process can always
    /// open a file it already has open.
    /// </remarks>
    internal IReadOnlyList<Leftover> FindLeftovers() => FindLeftovers(_root, _sessionDir);

    /// <summary>
    /// Deletes abandoned folders that hold nothing worth offering.
    /// </summary>
    /// <remarks>
    /// A run that is killed having saved everything leaves a folder with a
    /// lock file in it and no documents, and nothing else would ever delete
    /// it: the clean-exit path never ran, and the next launch finds nothing in
    /// it to offer. Without this, every crash and every end-task leaves one
    /// more folder in <c>data/recovery</c> for ever. Nothing recoverable is
    /// touched - a folder with so much as one readable document in it is left
    /// alone for the dialog to deal with.
    /// </remarks>
    internal int TidyEmptyAbandonedFolders()
    {
        var removed = 0;

        try
        {
            if (!Directory.Exists(_root))
            {
                return 0;
            }

            foreach (var folder in Directory.EnumerateDirectories(_root))
            {
                if (IsOurs(folder) || !IsAbandoned(folder))
                {
                    continue;
                }

                if (Files(folder).Any(file => Read(file) is not null))
                {
                    continue;
                }

                Discard(folder);
                removed++;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"the recovery folder could not be tidied ({ex.Message})");
        }

        if (removed > 0)
        {
            Log.Info($"{removed} empty recovery folder(s) from earlier runs were deleted");
        }

        return removed;
    }

    private bool IsOurs(string folder) => SamePath(folder, _sessionDir);

    private static bool SamePath(string one, string other)
    {
        try
        {
            return string.Equals(
                Path.GetFullPath(one).TrimEnd(Path.DirectorySeparatorChar),
                Path.GetFullPath(other).TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException)
        {
            return false;
        }
    }

    internal static IReadOnlyList<Leftover> FindLeftovers(string root, string? skipFolder = null)
    {
        var found = new List<Leftover>();

        IEnumerable<string> folders;

        try
        {
            if (!Directory.Exists(root))
            {
                return found;
            }

            folders = Directory.EnumerateDirectories(root);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"the recovery folder could not be read ({ex.Message})");

            return found;
        }

        foreach (var folder in folders)
        {
            if (skipFolder is not null && SamePath(folder, skipFolder))
            {
                continue;
            }

            if (!IsAbandoned(folder))
            {
                continue;
            }

            foreach (var file in Files(folder))
            {
                if (Read(file) is { } record)
                {
                    found.Add(new Leftover(
                        file,
                        folder,
                        record.Title,
                        record.OriginalPath,
                        record.FormatName,
                        record.SavedUtc));
                }
            }
        }

        return [..found.OrderByDescending(f => f.SavedUtc)];
    }

    private static IEnumerable<string> Files(string folder)
    {
        try
        {
            return Directory.EnumerateFiles(folder, "*.json").ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>
    /// True when no live process holds this folder's lock.
    /// </summary>
    internal static bool IsAbandoned(string folder)
    {
        var lockFile = Path.Combine(folder, LockFileName);

        try
        {
            if (!File.Exists(lockFile))
            {
                return true;
            }

            // Opening it is the test. If another process has it with
            // FileShare.None this throws, and that process is alive.
            using var stream = new FileStream(
                lockFile,
                new FileStreamOptions
                {
                    Mode = FileMode.Open,
                    Access = FileAccess.ReadWrite,
                    Share = FileShare.None,
                });

            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            // Someone else's folder, or a permissions problem. Either way it is
            // not this run's business to offer it.
            return false;
        }
    }

    /// <summary>Reads one recovery file, or null when it cannot be understood.</summary>
    internal static Saved? Read(string file)
    {
        try
        {
            using var stream = File.OpenRead(file);

            return JsonSerializer.Deserialize(stream, RecoveryJson.Default.Saved);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            Log.Warn($"a recovery file could not be read and was ignored: {Path.GetFileName(file)} ({ex.Message})");

            return null;
        }
    }

    /// <summary>Deletes a recovery folder and everything in it.</summary>
    internal static void Discard(string folder)
    {
        try
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"a recovery folder could not be deleted: {folder} ({ex.Message})");
        }
    }

    /// <summary>What one recovery file holds.</summary>
    /// <remarks>
    /// The original path is kept so a restored document knows where it came
    /// from, and the title so the dialog can name an untitled one. The IR is
    /// the document itself: recovery stores what was on screen, not what was
    /// last saved.
    /// </remarks>
    internal sealed record Saved(
        string Id,
        string? OriginalPath,
        string Title,
        string FormatName,
        DateTime SavedUtc,
        SpDocument Document);
}

/// <summary>Source-generated JSON for the recovery files.</summary>
/// <remarks>
/// Separate from <c>IrJson</c> although it serializes an <c>SpDocument</c>
/// inside it: a context serializes the root type it is given, and the root
/// here is the record with the metadata around the document.
/// </remarks>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(Recovery.Saved))]
internal partial class RecoveryJson : JsonSerializerContext;
