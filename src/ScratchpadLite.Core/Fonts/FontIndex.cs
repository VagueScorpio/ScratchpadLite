using System.Text.Json;
using System.Text.Json.Serialization;
using ScratchpadLite.Core.Platform;

namespace ScratchpadLite.Core.Fonts;

/// <summary>What happened to one file in the font folder (Section 6.6.7).</summary>
public enum FontStatus
{
    /// <summary>A .ttf, .otf or .ttc, copied to the cache and loaded as is.</summary>
    Loaded,

    /// <summary>Decoded or converted into an OpenType file once, then cached.</summary>
    Converted,

    /// <summary>The same family and face as another file, which was kept instead.</summary>
    Duplicate,

    /// <summary>Too large, or not worth opening. The message says why.</summary>
    Skipped,

    /// <summary>A format WPF cannot use. The message says what to do instead.</summary>
    Unsupported,

    /// <summary>Something went wrong. The message is the reason.</summary>
    Failed,

    /// <summary>The file is gone, but its family stays registered until restart.</summary>
    Removed,
}

/// <summary>
/// The card catalog (Section 6.6.1), kept at data/font_cache/index.json.
/// One entry per source file records its path, size, timestamp and hash,
/// the cached file it produced, and the family and face names inside it.
/// A file whose size and timestamp are unchanged is never re-read,
/// re-hashed or re-copied, so later launches cost nothing. Loading the
/// index is what IndexReady waits on, and it takes milliseconds.
/// </summary>
/// <remarks>
/// The analogy that matters: to know what is on the shelves the librarian
/// reads the catalog, not every book. A launch with an unchanged font folder
/// opens no font file at all - the families are registered from the names
/// stored here, and WPF only reads a font when text actually uses it.
/// </remarks>
public sealed class FontIndex
{
    /// <summary>
    /// Bumped whenever the shape of an entry changes. An index written by an
    /// older version is discarded and rebuilt rather than half-understood.
    /// </summary>
    public const int CurrentVersion = 1;

    /// <summary>One catalog card.</summary>
    public sealed record Entry(
        string SourcePath,
        long Length,
        DateTime ModifiedUtc,
        string Sha1,
        string CachedFile,
        IReadOnlyList<string> Families,
        string? FailureReason)
    {
        public FontFileKind Kind { get; init; }

        public FontStatus Status { get; init; }

        public IReadOnlyList<string> Faces { get; init; } = [];

        /// <summary>fsType from OS/2, for Section 7.4.</summary>
        public ushort FsType { get; init; }

        public string? Copyright { get; init; }

        public string? LicenseDescription { get; init; }

        public string? LicenseUrl { get; init; }

        /// <summary>
        /// Whether the font can draw the Latin alphabet, so the picker can show
        /// its name in itself without opening the file (Section 6.7).
        /// </summary>
        public bool HasLatinLetters { get; init; }

        public bool IsVariable { get; init; }

        public int GlyphCount { get; init; }

        /// <summary>The line the Font Report shows for this file.</summary>
        public string Message { get; init; } = "";

        /// <summary>True when this entry produced a usable family.</summary>
        [JsonIgnore]
        public bool IsUsable =>
            Status is FontStatus.Loaded or FontStatus.Converted
            && CachedFile.Length > 0
            && Families.Count > 0;
    }

    private readonly Dictionary<string, Entry> _byPath = new(StringComparer.OrdinalIgnoreCase);

    public int Version { get; private init; } = CurrentVersion;

    public IReadOnlyCollection<Entry> Entries => _byPath.Values;

    /// <summary>True when the index was rebuilt rather than read.</summary>
    public bool WasRebuilt { get; private init; }

    public static FontIndex Load(string indexPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(indexPath);

        if (!File.Exists(indexPath))
        {
            return new FontIndex { WasRebuilt = true };
        }

        try
        {
            using var stream = File.OpenRead(indexPath);
            var file = JsonSerializer.Deserialize(stream, FontIndexJson.Default.FontIndexFile);

            if (file is null || file.Version != CurrentVersion)
            {
                Log.Info(
                    $"the font index is version {file?.Version.ToString() ?? "unreadable"}, "
                    + $"this build writes {CurrentVersion} - rebuilding it");

                return new FontIndex { WasRebuilt = true };
            }

            var index = new FontIndex();
            foreach (var entry in file.Entries)
            {
                index._byPath[entry.SourcePath] = entry;
            }

            return index;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // A damaged index costs one slower launch, not a broken app.
            Log.Warn($"the font index could not be read ({ex.GetType().Name}) - rebuilding it");
            return new FontIndex { WasRebuilt = true };
        }
    }

    /// <summary>Writes the index atomically (Section 6.14).</summary>
    public void Save(string indexPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(indexPath);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(indexPath)!);

            var file = new FontIndexFile
            {
                Version = CurrentVersion,
                Entries = _byPath.Values.OrderBy(e => e.SourcePath, StringComparer.OrdinalIgnoreCase).ToList(),
            };

            var json = JsonSerializer.Serialize(file, FontIndexJson.Default.FontIndexFile);
            var temp = $"{indexPath}.{Guid.NewGuid():N}.tmp";

            File.WriteAllText(temp, json);
            File.Move(temp, indexPath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Losing the index means the next launch prepares the fonts again.
            // Slower, not broken, and not worth telling the user about.
            Log.Warn($"the font index could not be saved: {ex.Message}");
        }
    }

    /// <summary>True when size and timestamp match the recorded card.</summary>
    public bool IsUnchanged(FontScanner.Found found)
    {
        ArgumentNullException.ThrowIfNull(found);

        return _byPath.TryGetValue(found.RelativePath, out var entry)
               && entry.Length == found.Length
               && entry.ModifiedUtc == found.ModifiedUtc;
    }

    public Entry? Find(string relativePath) => _byPath.GetValueOrDefault(relativePath);

    public void Put(Entry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        _byPath[entry.SourcePath] = entry;
    }

    /// <summary>
    /// Drops the entries whose source file is gone and returns the cache files
    /// that nothing refers to any more, so the caller can delete them.
    /// </summary>
    /// <remarks>
    /// Two entries can share one cache file - the same font dropped in twice -
    /// so a cache file is only deletable once no remaining entry names it.
    /// </remarks>
    public IReadOnlyList<string> PruneMissing(IReadOnlyCollection<string> presentPaths)
    {
        ArgumentNullException.ThrowIfNull(presentPaths);

        var present = new HashSet<string>(presentPaths, StringComparer.OrdinalIgnoreCase);
        var gone = _byPath.Keys.Where(path => !present.Contains(path)).ToList();

        if (gone.Count == 0)
        {
            return [];
        }

        var released = new List<string>();

        foreach (var path in gone)
        {
            var entry = _byPath[path];
            _byPath.Remove(path);

            if (entry.CachedFile.Length > 0)
            {
                released.Add(entry.CachedFile);
            }
        }

        var stillUsed = _byPath.Values
            .Select(e => e.CachedFile)
            .Where(name => name.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return released.Where(name => !stillUsed.Contains(name)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>The usable entries, one per family, resolving duplicates.</summary>
    public IEnumerable<Entry> Usable() => _byPath.Values.Where(e => e.IsUsable);
}

/// <summary>The on-disk shape of the index.</summary>
public sealed class FontIndexFile
{
    public int Version { get; set; } = FontIndex.CurrentVersion;

    public List<FontIndex.Entry> Entries { get; set; } = [];
}

/// <summary>
/// Source-generated JSON for the index: no reflection, which is what keeps it
/// off the startup path (Section 2.1).
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = false,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault)]
[JsonSerializable(typeof(FontIndexFile))]
internal sealed partial class FontIndexJson : JsonSerializerContext;
