using System.Security.Cryptography;
using System.Text;
using ScratchpadLite.Core.Platform;

namespace ScratchpadLite.Core.Fonts;

/// <summary>Which of a family's four faces a font file holds.</summary>
public enum FontFace
{
    Regular,
    Bold,
    Italic,
    BoldItalic,
}

/// <summary>
/// One font face travelling inside a document (Section 7.4).
/// </summary>
/// <param name="Family">The family name as the document asks for it.</param>
/// <param name="Face">Which of the four faces this file holds.</param>
/// <param name="Bytes">The font file, already deobfuscated.</param>
public sealed record EmbeddedFont(string Family, FontFace Face, byte[] Bytes)
{
    /// <summary>".ttf" or ".otf", used when the font is written to disk.</summary>
    public string Extension { get; init; } = ".ttf";

    /// <summary>
    /// A file name that is safe on every filesystem, says what it is, and
    /// cannot be another family's.
    /// </summary>
    /// <remarks>
    /// The four characters on the end are not decoration. Stripping a family
    /// name down to what a filesystem and a zip entry will take can map two
    /// different families onto one name - "Foo Bar" and "Foo-Bar" both become
    /// Foo-Bar - and a document using both would then have one of them
    /// pointing at the other's file. The fingerprint is of the family name as
    /// it really is, so two names that differ at all get different files.
    /// </remarks>
    public string FileName
    {
        get
        {
            var safe = string.Concat(
                    Family.Where(c => char.IsLetterOrDigit(c) || c is ' ' or '-' or '_'))
                .Trim()
                .Replace(' ', '-');

            return $"{(safe.Length == 0 ? "font" : safe)}-{Face}-{Fingerprint}{Extension}";
        }
    }

    /// <summary>
    /// Four hex characters that depend only on the family name.
    /// </summary>
    /// <remarks>
    /// SHA-1 rather than <c>string.GetHashCode</c>, which is seeded per
    /// process in .NET: a name in a file written today has to find the same
    /// file when the document is opened tomorrow.
    /// </remarks>
    private string Fingerprint =>
        Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(Family)))[..4].ToLowerInvariant();
}

/// <summary>
/// Decides which fonts may travel with a document, and why the others may
/// not (Section 7.4).
/// </summary>
/// <remarks>
/// <para>Three rules, and each of them is somebody else's decision rather
/// than this program's.</para>
///
/// <list type="bullet">
///   <item>Only fonts from the <c>fonts/</c> folder are embedded. A font that
///   came with Windows is on the PC at the other end already, and copying
///   Microsoft's fonts into a document would be both pointless and a licence
///   breach.</item>
///   <item>Only when the font's own <c>fsType</c> permits it: Installable or
///   Editable. Preview-and-print and Restricted are refused, and so is a font
///   that allows only bitmap embedding. That flag is the foundry's statement
///   about its own font, and it is not ours to reinterpret.</item>
///   <item>Never a font converted from Adobe Type 1. The file that would be
///   embedded is this program's own conversion rather than anything the
///   foundry shipped, and Section 6.6.4 sets its fsType to preview-and-print
///   for exactly that reason.</item>
/// </list>
///
/// <para>Everything refused is reported, not dropped quietly: a document that
/// silently fails to carry its fonts looks fine here and wrong everywhere
/// else.</para>
/// </remarks>
public static class FontEmbedding
{
    /// <summary>A font the document uses that will not be travelling with it.</summary>
    /// <param name="Family">The family name.</param>
    /// <param name="Reason">A sentence for the save warning.</param>
    public sealed record Skipped(string Family, string Reason);

    /// <summary>What <see cref="Plan"/> worked out.</summary>
    /// <param name="Fonts">The faces to embed.</param>
    /// <param name="Left">The families that will not travel, with reasons.</param>
    public sealed record Result(IReadOnlyList<EmbeddedFont> Fonts, IReadOnlyList<Skipped> Left);

    /// <summary>
    /// True when a font's OS/2 fsType allows it to be embedded in a document
    /// that can still be edited.
    /// </summary>
    /// <remarks>
    /// Bits 0 to 3 are meant to be exclusive: 0 is Installable, bit 1 is
    /// Restricted, bit 2 is preview and print, bit 3 is Editable. Bit 9,
    /// separately, says only bitmaps may be embedded, which for a word
    /// processor means no. Where a font sets bits that contradict each other,
    /// the most restrictive wins - a font that says both Restricted and
    /// Editable has said Restricted, and reading it the other way would be
    /// this program choosing the answer it preferred.
    /// </remarks>
    public static bool RightsAllow(ushort fsType) =>
        (fsType & 0x0200) == 0
        && (fsType & 0x0002) == 0
        && ((fsType & 0x000F) == 0 || (fsType & 0x0008) != 0);

    /// <summary>What fsType says, as a phrase for a warning.</summary>
    public static string RightsPhrase(ushort fsType) => fsType switch
    {
        _ when (fsType & 0x0200) != 0 => "it allows only bitmap embedding",
        _ when (fsType & 0x0002) != 0 => "its licence does not allow embedding",
        _ when (fsType & 0x0004) != 0 && (fsType & 0x0008) == 0 =>
            "its licence allows embedding for preview and printing only, "
            + "which would make the document read-only in Word",
        _ => $"its fsType is 0x{fsType:X4}",
    };

    /// <summary>
    /// Everything a save should put inside the file: what the document
    /// arrived carrying, plus whatever the font folder can add when the
    /// setting is on.
    /// </summary>
    /// <remarks>
    /// <para>The two sources answer different questions. A font the document
    /// came with stays in it whatever the setting says, because taking it out
    /// would be deciding on the user's behalf that their document should stop
    /// looking the way it looks everywhere else - and they would find out on
    /// somebody else's PC. The setting decides the narrower thing: whether a
    /// font from this PC is added to a document that did not have it.</para>
    ///
    /// <para>Either way, only families the document still uses travel. A font
    /// kept for text that has since been deleted is weight in the file and
    /// nothing else.</para>
    /// </remarks>
    public static Result ForSave(
        IReadOnlyList<EmbeddedFont> carried,
        IReadOnlySet<string> used,
        bool addFromFontFolder,
        FontIndex index,
        Func<string, byte[]?> read)
    {
        ArgumentNullException.ThrowIfNull(carried);
        ArgumentNullException.ThrowIfNull(used);

        var fonts = new List<EmbeddedFont>();
        var taken = new HashSet<(string Family, FontFace Face)>();

        foreach (var font in carried)
        {
            if (used.Contains(font.Family) && taken.Add((font.Family, font.Face)))
            {
                fonts.Add(font);
            }
        }

        if (!addFromFontFolder)
        {
            return new Result(fonts, []);
        }

        var planned = Plan(used, index, read);

        foreach (var font in planned.Fonts)
        {
            if (taken.Add((font.Family, font.Face)))
            {
                fonts.Add(font);
            }
        }

        // A family the document already carries is travelling, whatever the
        // font folder's copy of it says about its own rights.
        var left = planned.Left
            .Where(s => !fonts.Any(f => string.Equals(f.Family, s.Family, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        return new Result(fonts, left);
    }

    /// <summary>
    /// Works out which of the families a document uses can travel with it.
    /// </summary>
    /// <param name="families">The families the document asks for.</param>
    /// <param name="index">The font folder's catalogue.</param>
    /// <param name="read">
    /// Reads a cached font file. Passed in so this can be tested without a
    /// cache folder, and so the caller decides what counts as the cache.
    /// </param>
    public static Result Plan(
        IEnumerable<string> families,
        FontIndex index,
        Func<string, byte[]?> read)
    {
        ArgumentNullException.ThrowIfNull(families);
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(read);

        var fonts = new List<EmbeddedFont>();
        var left = new List<Skipped>();
        var taken = new HashSet<(string Family, FontFace Face)>();

        foreach (var family in families.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var entries = index.Entries
                .Where(e => e.IsUsable && e.Families.Contains(family, StringComparer.OrdinalIgnoreCase))
                .ToList();

            if (entries.Count == 0)
            {
                // Not a font folder font: either one of Windows' own, which
                // needs no help, or one this PC does not have. Neither is
                // worth a warning about embedding.
                continue;
            }

            var embedded = 0;

            foreach (var entry in entries)
            {
                if (entry.Kind == FontFileKind.Type1)
                {
                    Note(left, family, "it was converted from an Adobe Type 1 font, "
                        + "and what would travel is this program's conversion rather than the original");

                    continue;
                }

                if (!RightsAllow(entry.FsType))
                {
                    Note(left, family, RightsPhrase(entry.FsType));

                    continue;
                }

                if (read(entry.CachedFile) is not { Length: > 0 } bytes)
                {
                    Note(left, family, "its prepared copy could not be read");

                    continue;
                }

                var face = FaceOf(entry.Faces.Count > 0 ? entry.Faces[0] : "");

                if (!taken.Add((family, face)))
                {
                    // Two files claim the same face of the same family. The
                    // first one won when the folder was prepared, so it wins
                    // here too.
                    continue;
                }

                fonts.Add(new EmbeddedFont(family, face, bytes)
                {
                    Extension = ExtensionFor(bytes),
                });

                embedded++;
            }

            if (embedded > 0)
            {
                // A family that got at least one face through is travelling.
                // A complaint about one of its other faces would read as a
                // failure when it is not.
                left.RemoveAll(s => string.Equals(s.Family, family, StringComparison.OrdinalIgnoreCase));
            }
        }

        return new Result(fonts, left);
    }

    private static void Note(List<Skipped> left, string family, string reason)
    {
        if (!left.Any(s => string.Equals(s.Family, family, StringComparison.OrdinalIgnoreCase)))
        {
            left.Add(new Skipped(family, reason));
        }
    }

    /// <summary>
    /// ".otf" for a font with CFF outlines, ".ttf" otherwise, taken from the
    /// sfnt tag at the front of the file rather than from the index - the
    /// index does not record it, and the bytes are already in hand.
    /// </summary>
    public static string ExtensionFor(ReadOnlySpan<byte> font) =>
        font.Length >= 4 && font[0] == (byte)'O' && font[1] == (byte)'T'
        && font[2] == (byte)'T' && font[3] == (byte)'O'
            ? ".otf"
            : ".ttf";

    /// <summary>Turns a subfamily name - "Bold Italic" - into one of the four faces.</summary>
    public static FontFace FaceOf(string? subfamily)
    {
        var name = subfamily?.Trim() ?? "";

        var bold = name.Contains("bold", StringComparison.OrdinalIgnoreCase)
            || name.Contains("black", StringComparison.OrdinalIgnoreCase)
            || name.Contains("heavy", StringComparison.OrdinalIgnoreCase);

        var italic = name.Contains("italic", StringComparison.OrdinalIgnoreCase)
            || name.Contains("oblique", StringComparison.OrdinalIgnoreCase);

        return (bold, italic) switch
        {
            (true, true) => FontFace.BoldItalic,
            (true, false) => FontFace.Bold,
            (false, true) => FontFace.Italic,
            _ => FontFace.Regular,
        };
    }

    /// <summary>
    /// Writes embedded fonts into a folder of their own under the font cache
    /// and gives back the files, so WPF can load them for one document
    /// (Section 7.4).
    /// </summary>
    public static IReadOnlyList<(EmbeddedFont Font, string Path)> Unpack(
        IEnumerable<EmbeddedFont> fonts, Guid documentId)
    {
        ArgumentNullException.ThrowIfNull(fonts);

        var folder = FolderFor(documentId);
        var written = new List<(EmbeddedFont, string)>();

        foreach (var font in fonts)
        {
            try
            {
                Directory.CreateDirectory(folder);

                var path = Path.Combine(folder, font.FileName);
                File.WriteAllBytes(path, font.Bytes);

                written.Add((font, path));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                          or ArgumentException or NotSupportedException)
            {
                // A document that cannot unpack its fonts still opens; it
                // falls back to a font this PC has, which is what happens
                // without embedding at all.
                Log.Warn($"an embedded font could not be unpacked: {ex.Message}");
            }
        }

        return written;
    }

    /// <summary>Where one document's embedded fonts live while it is open.</summary>
    public static string FolderFor(Guid documentId) =>
        Path.Combine(AppPaths.FontCacheDir, FontCache.EmbeddedFolderName, documentId.ToString("N"));

    /// <summary>
    /// Deletes the unpacked fonts of documents that are no longer open.
    /// </summary>
    /// <remarks>
    /// Called at launch rather than when a tab closes, because WPF holds a
    /// font file open for as long as the family is registered and will not
    /// let go until the process ends. A folder left behind by the last run is
    /// nobody's, so it can go.
    /// </remarks>
    public static int TidyUnpacked(IEnumerable<Guid> stillOpen)
    {
        ArgumentNullException.ThrowIfNull(stillOpen);

        var keep = stillOpen.Select(id => id.ToString("N")).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var root = Path.Combine(AppPaths.FontCacheDir, FontCache.EmbeddedFolderName);
        var removed = 0;

        try
        {
            if (!Directory.Exists(root))
            {
                return 0;
            }

            foreach (var folder in Directory.GetDirectories(root))
            {
                if (keep.Contains(Path.GetFileName(folder)))
                {
                    continue;
                }

                try
                {
                    Directory.Delete(folder, recursive: true);
                    removed++;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Still open somewhere. It will go on a later launch.
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"the embedded font folder could not be tidied: {ex.Message}");
        }

        return removed;
    }
}
