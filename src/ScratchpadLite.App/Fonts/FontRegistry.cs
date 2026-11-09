using System.IO;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;
using ScratchpadLite.Core.Fonts;
using ScratchpadLite.Core.Platform;

namespace ScratchpadLite.App.Fonts;

/// <summary>
/// Turns what Core prepared into WPF FontFamily objects, and is the only
/// place that knows how a cached font file becomes a usable family
/// (Section 6.6).
///
/// Startup order (Section 4.3): nothing here runs before the first frame.
/// Right after it, a background task reads the font index, registers every
/// cached font, then checks the font folder for new, changed and removed
/// files and processes only those, raising FontsChanged when it finishes.
/// The Windows font list is fetched when the picker first opens or at idle
/// priority, whichever comes first.
/// </summary>
/// <remarks>
/// The two things to keep true here:
/// <list type="bullet">
///   <item>
///     <b>Registering a family opens no font file.</b> The family name comes
///     out of the index and goes into a <see cref="FontFamily"/> as a URI plus
///     a name; WPF reads the file only when text is actually drawn in it. That
///     is what makes a folder of 500 fonts cost nothing at startup.
///   </item>
///   <item>
///     <b>Nothing here is called before the first frame.</b> Section 2.1 says
///     so, and it is the single largest piece of work the app can be tricked
///     into doing early.
///   </item>
/// </list>
/// </remarks>
internal static class FontRegistry
{
    /// <summary>At most one picker rebuild per second while fonts are preparing (Section 6.7).</summary>
    private static readonly TimeSpan ChangeBatch = TimeSpan.FromSeconds(1);

    /// <summary>How long to wait after a font folder change before acting (Section 6.6.7).</summary>
    private static readonly TimeSpan WatcherDebounce = TimeSpan.FromSeconds(1);

    private static readonly TaskCompletionSource IndexLoaded =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static readonly Lock Gate = new();

    /// <summary>Family name to the family built from the cache. Guarded by <see cref="Gate"/>.</summary>
    private static readonly Dictionary<string, FontFamily> FolderFamilies =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly List<string> RecentlyUsed = [];

    private static HashSet<string>? _windowsFamilies;
    private static FontIndex _index = new();
    private static FileSystemWatcher? _watcher;
    private static CancellationTokenSource? _work;
    private static Timer? _debounce;
    private static DateTime _lastChangeRaised = DateTime.MinValue;
    private static bool _changePending;

    /// <summary>Completes once the font index has been read, in milliseconds.</summary>
    internal static Task IndexReady => IndexLoaded.Task;

    /// <summary>Raised on the UI thread when the folder check adds or changes families.</summary>
    internal static event EventHandler? FontsChanged;

    /// <summary>Raised on the UI thread while fonts are being prepared.</summary>
    internal static event EventHandler<FontPreparer.Progress>? PreparationProgress;

    /// <summary>The index, for the Font Report. Its rows are read, never edited.</summary>
    internal static FontIndex Index => _index;

    /// <summary>The family used when a document asks for something unavailable.</summary>
    internal static string DefaultFamily { get; set; } = "Calibri";

    /// <summary>Starts the background index load and folder check (after the first frame).</summary>
    internal static void StartBackgroundPreparation()
    {
        var work = new CancellationTokenSource();
        _work = work;

        // Below normal, so preparing 500 fonts cannot make typing stutter.
        var thread = new Thread(() => Run(work.Token))
        {
            IsBackground = true,
            Priority = ThreadPriority.BelowNormal,
            Name = "font preparation",
        };

        thread.Start();
    }

    /// <summary>Re-runs the folder check for Tools, Reload Fonts.</summary>
    internal static void Reload()
    {
        if (!IndexLoaded.Task.IsCompleted)
        {
            return;        // the first pass has not finished; it will pick this up
        }

        var work = new CancellationTokenSource();
        _work = work;

        var thread = new Thread(() => CheckFolder(work.Token))
        {
            IsBackground = true,
            Priority = ThreadPriority.BelowNormal,
            Name = "font reload",
        };

        thread.Start();
    }

    internal static void Stop()
    {
        _work?.Cancel();
        _debounce?.Dispose();
        _watcher?.Dispose();
        _watcher = null;
    }

    private static void Run(CancellationToken ct)
    {
        try
        {
            LoadIndexAndRegister(ct);
        }
        catch (Exception ex)
        {
            Log.Error("the font index could not be loaded", ex);
        }
        finally
        {
            // Always completed, even on failure: anything waiting on this must
            // not wait forever because a font file was broken.
            IndexLoaded.TrySetResult();
        }

        if (ct.IsCancellationRequested)
        {
            return;
        }

        CheckFolder(ct);
        StartWatching();
    }

    /// <summary>
    /// Step 1 of Section 6.6.1: register every cached font straight from the
    /// stored names, opening nothing.
    /// </summary>
    private static void LoadIndexAndRegister(CancellationToken ct)
    {
        var started = System.Diagnostics.Stopwatch.StartNew();

        _index = FontIndex.Load(AppPaths.FontIndexFile);

        var registered = 0;

        foreach (var entry in _index.Usable())
        {
            ct.ThrowIfCancellationRequested();

            if (Register(entry))
            {
                registered++;
            }
        }

        Log.Info(
            $"font index: {registered} families registered from {_index.Entries.Count} entries "
            + $"in {started.ElapsedMilliseconds} ms");

        if (registered > 0)
        {
            RaiseChanged(force: true);
        }
    }

    private static void CheckFolder(CancellationToken ct)
    {
        try
        {
            var result = FontPreparer.CheckFolder(
                AppPaths.FontDir,
                _index,
                onProgress: progress => OnUi(() => PreparationProgress?.Invoke(null, progress)),
                onFamiliesReady: entry =>
                {
                    if (Register(entry))
                    {
                        RaiseChanged(force: false);
                    }
                },
                ct);

            _index.Save(AppPaths.FontIndexFile);

            if (result.AnythingChanged)
            {
                RaiseChanged(force: true);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down, or a reload superseded this pass.
        }
        catch (Exception ex)
        {
            Log.Error("the font folder check failed", ex);
        }
    }

    /// <summary>
    /// Builds a <see cref="FontFamily"/> for one index entry without opening
    /// the file.
    /// </summary>
    /// <remarks>
    /// The URI form matters and is easy to get wrong: a base URI pointing at
    /// the cache folder, with a trailing slash, and a family string of
    /// "./file.ttf#Family Name". Written any other way WPF either throws or
    /// silently falls back to the default font, which looks like the font
    /// failing to load rather than the URI being wrong.
    /// </remarks>
    private static bool Register(FontIndex.Entry entry)
    {
        if (!entry.IsUsable)
        {
            return false;
        }

        var added = false;

        try
        {
            var baseUri = new Uri(AppPaths.FontCacheDir.TrimEnd('\\', '/') + Path.DirectorySeparatorChar);

            foreach (var family in entry.Families)
            {
                var fontFamily = new FontFamily(baseUri, $"./{entry.CachedFile}#{family}");

                lock (Gate)
                {
                    // First one wins, so a duplicate left in the index cannot
                    // replace the copy that was kept.
                    added |= FolderFamilies.TryAdd(family, fontFamily);
                }
            }
        }
        catch (Exception ex) when (ex is UriFormatException or ArgumentException)
        {
            Log.Warn($"{entry.SourcePath} could not be registered: {ex.Message}");
        }

        return added;
    }

    /// <summary>
    /// A family name to a WPF family, falling back through Substitutions.
    /// </summary>
    /// <remarks>
    /// A font-folder font wins over a Windows font of the same name
    /// (Section 6.6.7), because the user put it there on purpose.
    /// </remarks>
    internal static FontFamily Resolve(string familyName)
    {
        if (!string.IsNullOrWhiteSpace(familyName))
        {
            var name = familyName.Trim();

            lock (Gate)
            {
                if (FolderFamilies.TryGetValue(name, out var folderFont))
                {
                    return folderFont;
                }
            }

            if (WindowsFamilies().Contains(name))
            {
                return new FontFamily(name);
            }

            if (Substitutions.Resolve(name, Available()) is { } substitute)
            {
                return new FontFamily(substitute);
            }
        }

        return new FontFamily(DefaultFamily);
    }

    /// <summary>
    /// The font-folder family with this name, without touching the Windows
    /// font list.
    /// </summary>
    /// <remarks>
    /// Exists for the one caller that must not pay for enumerating the
    /// installed fonts: the editor's default font is applied before the first
    /// frame, and re-applied once the folder check has run in case the default
    /// is a font from <c>fonts/</c> rather than an installed one.
    /// </remarks>
    internal static bool TryFolderFamily(string familyName, out FontFamily family)
    {
        lock (Gate)
        {
            return FolderFamilies.TryGetValue(familyName?.Trim() ?? "", out family!);
        }
    }

    /// <summary>True when the name is a font this machine can actually show.</summary>
    internal static bool IsAvailable(string familyName)
    {
        if (string.IsNullOrWhiteSpace(familyName))
        {
            return false;
        }

        var name = familyName.Trim();

        lock (Gate)
        {
            if (FolderFamilies.ContainsKey(name))
            {
                return true;
            }
        }

        return WindowsFamilies().Contains(name);
    }

    /// <summary>Every family the picker shows: Windows fonts plus the font folder.</summary>
    internal static IReadOnlyList<string> AllFamilies =>
        FolderFamilyNames()
            .Concat(WindowsFamilies())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    /// <summary>The font folder's families, which the picker shows in their own group.</summary>
    internal static IReadOnlyList<string> FolderFamilyNames()
    {
        lock (Gate)
        {
            return FolderFamilies.Keys.OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase).ToList();
        }
    }

    /// <summary>The last eight families used, newest first (Section 6.7).</summary>
    internal static IReadOnlyList<string> Recent()
    {
        lock (Gate)
        {
            return RecentlyUsed.ToList();
        }
    }

    internal static void NoteUsed(string familyName)
    {
        if (string.IsNullOrWhiteSpace(familyName))
        {
            return;
        }

        lock (Gate)
        {
            RecentlyUsed.RemoveAll(n => string.Equals(n, familyName, StringComparison.OrdinalIgnoreCase));
            RecentlyUsed.Insert(0, familyName);

            if (RecentlyUsed.Count > 8)
            {
                RecentlyUsed.RemoveRange(8, RecentlyUsed.Count - 8);
            }
        }
    }

    /// <summary>
    /// Whether a family can draw the Latin alphabet, so the picker knows
    /// whether to show the name in the font itself (Section 6.7).
    /// </summary>
    /// <remarks>
    /// For a font-folder font the answer is already in the index, worked out
    /// when the file was prepared, so this opens nothing. For a Windows font
    /// it is worked out on first ask and remembered for the session.
    /// </remarks>
    internal static bool HasLatinLetters(string familyName)
    {
        foreach (var entry in _index.Usable())
        {
            if (entry.Families.Contains(familyName, StringComparer.OrdinalIgnoreCase))
            {
                return entry.HasLatinLetters;
            }
        }

        return LatinByFamily.GetOrAdd(familyName, ProbeLatin);
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> LatinByFamily =
        new(StringComparer.OrdinalIgnoreCase);

    private static bool ProbeLatin(string familyName)
    {
        try
        {
            foreach (var typeface in new FontFamily(familyName).GetTypefaces())
            {
                if (!typeface.TryGetGlyphTypeface(out var glyphTypeface))
                {
                    continue;
                }

                return glyphTypeface.CharacterToGlyphMap.ContainsKey('A')
                       && glyphTypeface.CharacterToGlyphMap.ContainsKey('a');
            }
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or ArgumentException)
        {
            // A font that will not open cannot preview itself; the picker falls
            // back to the UI font, which is the right answer anyway.
            Log.Debug($"could not read {familyName}: {ex.Message}");
        }

        return false;
    }

    /// <summary>
    /// The Windows font list, fetched on first use (Section 2.1) and then kept.
    /// </summary>
    internal static HashSet<string> WindowsFamilies()
    {
        if (_windowsFamilies is { } cached)
        {
            return cached;
        }

        lock (Gate)
        {
            if (_windowsFamilies is { } already)
            {
                return already;
            }

            var started = System.Diagnostics.Stopwatch.StartNew();
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var family in System.Windows.Media.Fonts.SystemFontFamilies)
            {
                names.Add(DisplayName(family));
            }

            Log.Info($"Windows font list: {names.Count} families in {started.ElapsedMilliseconds} ms");

            _windowsFamilies = names;
            return names;
        }
    }

    /// <summary>The English name of a family, or whatever name it does have.</summary>
    internal static string DisplayName(FontFamily family)
    {
        ArgumentNullException.ThrowIfNull(family);

        if (family.FamilyNames.TryGetValue(XmlLanguage.GetLanguage("en-us"), out var english))
        {
            return english;
        }

        return family.FamilyNames.Count > 0 ? family.FamilyNames.Values.First() : family.Source;
    }

    /// <summary>
    /// The families named by a document that this machine cannot show and has
    /// no substitute for (Section 6.6).
    /// </summary>
    internal static IReadOnlyList<string> MissingFrom(IEnumerable<string> requested)
    {
        ArgumentNullException.ThrowIfNull(requested);

        var available = Available();

        return requested
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Where(name => !IsAvailable(name) && Substitutions.Resolve(name, available) is null)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static HashSet<string> Available()
    {
        var available = new HashSet<string>(WindowsFamilies(), StringComparer.OrdinalIgnoreCase);

        lock (Gate)
        {
            foreach (var name in FolderFamilies.Keys)
            {
                available.Add(name);
            }
        }

        return available;
    }

    /// <summary>
    /// A FileSystemWatcher on the font folder, created after the first frame
    /// (Section 6.6.7).
    /// </summary>
    private static void StartWatching()
    {
        if (_watcher is not null)
        {
            return;
        }

        try
        {
            var watcher = new FileSystemWatcher(AppPaths.FontDir)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
            };

            watcher.Created += OnFolderChanged;
            watcher.Deleted += OnFolderChanged;
            watcher.Changed += OnFolderChanged;
            watcher.Renamed += OnFolderChanged;

            // Dropping 200 fonts in at once raises hundreds of events, and the
            // buffer overflowing is normal rather than an error - the debounced
            // rescan finds everything either way.
            watcher.Error += (_, e) => Log.Debug($"font folder watcher: {e.GetException().Message}");

            watcher.EnableRaisingEvents = true;
            _watcher = watcher;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            Log.Warn($"the font folder cannot be watched: {ex.Message}");
        }
    }

    private static void OnFolderChanged(object sender, FileSystemEventArgs e)
    {
        // Copying a font raises Created then several Changed events, and a
        // whole folder raises hundreds, so the rescan waits for quiet.
        lock (Gate)
        {
            _debounce?.Dispose();
            _debounce = new Timer(_ => Reload(), null, WatcherDebounce, Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>
    /// Raises FontsChanged on the UI thread, at most once a second unless
    /// forced (Section 6.7).
    /// </summary>
    private static void RaiseChanged(bool force)
    {
        lock (Gate)
        {
            if (!force)
            {
                if (_changePending)
                {
                    return;
                }

                var since = DateTime.UtcNow - _lastChangeRaised;
                if (since < ChangeBatch)
                {
                    _changePending = true;
                    _ = Task.Delay(ChangeBatch - since).ContinueWith(
                        _ => RaiseChanged(force: true),
                        TaskScheduler.Default);
                    return;
                }
            }

            _changePending = false;
            _lastChangeRaised = DateTime.UtcNow;
        }

        OnUi(() => FontsChanged?.Invoke(null, EventArgs.Empty));
    }

    /// <summary>
    /// Runs a UI callback on the UI thread, or not at all.
    /// </summary>
    /// <remarks>
    /// The "or not at all" is the point. This used to fall back to running the
    /// action inline when there was no Application - which is the state during
    /// <c>--measure-startup</c>, because that shuts down at the first frame. The
    /// callback then touched the status bar from the preparation thread, threw
    /// "the calling thread cannot access this object", and took the whole
    /// folder check down with it: the index was never saved, so every
    /// measurement run re-prepared the entire font folder from scratch.
    /// <para>
    /// There is no sensible inline fallback for a UI update. If there is no UI,
    /// there is nothing to tell.
    /// </para>
    /// </remarks>
    private static void OnUi(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;

        if (dispatcher is null || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
        {
            return;
        }

        if (dispatcher.CheckAccess())
        {
            action();
            return;
        }

        try
        {
            dispatcher.BeginInvoke(action, System.Windows.Threading.DispatcherPriority.Background);
        }
        catch (TaskCanceledException)
        {
            // The dispatcher shut down between the check and the post. Normal
            // at exit.
        }
    }

    internal static void OnFontsChanged() => FontsChanged?.Invoke(null, EventArgs.Empty);
}
