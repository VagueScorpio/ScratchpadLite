namespace ScratchpadLite.Core.Platform;

/// <summary>
/// Where everything lives (Section 6.1).
/// </summary>
/// <remarks>
/// In a single-file app <c>Assembly.Location</c> is empty, so the program folder
/// always comes from <see cref="AppContext.BaseDirectory"/>.
/// <para>
/// Portable mode: <c>data/</c> sits next to the .exe when that folder is
/// writable. When it is not, settings fall back to LocalAppData and
/// <see cref="IsPortable"/> is false so the window can show a one-time notice.
/// </para>
/// </remarks>
public static class AppPaths
{
    public const string FontFolderName = "fonts";
    public const string DataFolderName = "data";

    /// <summary>The folder that holds the .exe.</summary>
    public static string ProgramDir { get; } = AppContext.BaseDirectory;

    /// <summary>
    /// The font folder. Never deleted or overwritten; created with a README only
    /// when missing. Overridable with <c>--fonts &lt;path&gt;</c>.
    /// </summary>
    public static string FontDir
    {
        get => EnsureDir(_fontDirOverride ?? Path.Combine(ProgramDir, FontFolderName));
    }

    /// <summary>Set by <see cref="Initialize"/>. Empty until then.</summary>
    public static string DataDir { get; private set; } = "";

    public static bool IsPortable { get; private set; }

    private static string? _fontDirOverride;

    public static string SettingsFile => Path.Combine(DataDir, "settings.json");
    public static string StylesFile => Path.Combine(DataDir, "styles.json");
    public static string RecentFilesFile => Path.Combine(DataDir, "recent-files.json");
    public static string UserDictionaryFile => Path.Combine(DataDir, "user-dictionary.lex");
    public static string FontCacheDir => Path.Combine(DataDir, "font_cache");
    public static string FontIndexFile => Path.Combine(FontCacheDir, "index.json");
    public static string RecoveryDir => Path.Combine(DataDir, "recovery");
    public static string LogDir => Path.Combine(DataDir, "logs");

    /// <summary>
    /// Picks the data folder and creates its subfolders. Runs before the first
    /// frame (Section 4.3) and must stay cheap: a few directory probes only.
    /// </summary>
    public static void Initialize()
    {
        var portable = Path.Combine(ProgramDir, DataFolderName);
        if (IsWritable(portable))
        {
            DataDir = portable;
            IsPortable = true;
        }
        else
        {
            DataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ScratchpadLite");
            IsPortable = false;
        }

        foreach (var sub in new[] { "font_cache", "recovery", "logs" })
        {
            EnsureDir(Path.Combine(DataDir, sub));
        }
    }

    /// <summary>
    /// Applies <c>--fonts &lt;path&gt;</c> (development and tests). A relative
    /// path resolves against <see cref="ProgramDir"/>.
    /// </summary>
    public static void OverrideFontDir(string path)
    {
        _fontDirOverride = Path.IsPathRooted(path)
            ? path
            : Path.GetFullPath(Path.Combine(ProgramDir, path));
    }

    /// <summary>The notice shown when portable mode could not be used.</summary>
    public static string NonPortableNotice =>
        $"Portable mode is off because the program folder is read-only. Settings are stored in {DataDir}.";

    private static string EnsureDir(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            // Caller decides; FontDir callers treat a missing folder as "no fonts".
        }

        return path;
    }

    private static bool IsWritable(string dir)
    {
        try
        {
            Directory.CreateDirectory(dir);
            var probe = Path.Combine(dir, $".write-probe-{Guid.NewGuid():N}");
            File.WriteAllBytes(probe, []);
            File.Delete(probe);
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }
}
