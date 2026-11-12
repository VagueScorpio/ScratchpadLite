namespace ScratchpadLite.Core.Platform;

public enum LogLevel { Debug, Info, Warn, Error }

/// <summary>
/// Rolling file log (Section 9): <c>data/logs/scratchpad-YYYYMMDD.log</c>,
/// 7 days kept, 1 MB per file. Info by default, Debug with <c>--debug</c>.
/// </summary>
/// <remarks>
/// Opened lazily on the first write so startup pays nothing for it
/// (Section 4.3 step 3). File names are logged, never file content.
/// </remarks>
public static class Log
{
    private const long MaxBytes = 1024 * 1024;
    private const int KeepDays = 7;

    private static readonly Lock Gate = new();
    private static bool _pruned;

    public static LogLevel MinLevel { get; set; } = LogLevel.Info;

    public static void Debug(string message) => Write(LogLevel.Debug, message, null);

    public static void Info(string message) => Write(LogLevel.Info, message, null);

    public static void Warn(string message) => Write(LogLevel.Warn, message, null);

    public static void Error(string message, Exception? ex = null) => Write(LogLevel.Error, message, ex);

    private static void Write(LogLevel level, string message, Exception? ex)
    {
        if (level < MinLevel || AppPaths.DataDir.Length == 0)
        {
            return;
        }

        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level.ToString().ToUpperInvariant()}] {message}"
                   + (ex is null ? "" : Environment.NewLine + ex);

        lock (Gate)
        {
            try
            {
                if (!_pruned)
                {
                    Prune();
                    _pruned = true;
                }

                var path = CurrentFile();
                Roll(path);
                File.AppendAllText(path, line + Environment.NewLine);
            }
            catch (Exception ioEx) when (ioEx is IOException or UnauthorizedAccessException)
            {
                // Logging must never take the app down.
            }
        }
    }

    /// <summary>
    /// Today's log file, for the crash message to point at (Section 9).
    /// </summary>
    /// <remarks>
    /// Section 6.13.1 asks the crash message to name the log. Telling someone
    /// a log exists without telling them where is the same as not telling
    /// them.
    /// </remarks>
    public static string FilePath => CurrentFile();

    private static string CurrentFile() =>
        Path.Combine(AppPaths.LogDir, $"scratchpad-{DateTime.Now:yyyyMMdd}.log");

    private static void Roll(string path)
    {
        var info = new FileInfo(path);
        if (info.Exists && info.Length > MaxBytes)
        {
            var rolled = Path.ChangeExtension(path, $".{DateTime.Now:HHmmss}.log");
            File.Move(path, rolled, overwrite: true);
        }
    }

    private static void Prune()
    {
        if (!Directory.Exists(AppPaths.LogDir))
        {
            return;
        }

        var cutoff = DateTime.Now.AddDays(-KeepDays);
        foreach (var file in Directory.EnumerateFiles(AppPaths.LogDir, "scratchpad-*.log"))
        {
            if (File.GetLastWriteTime(file) < cutoff)
            {
                File.Delete(file);
            }
        }
    }
}
