using System.Buffers.Binary;
using System.Text;

namespace ScratchpadLite.Core.Fonts;

/// <summary>
/// Reads the family name, face names, version, license text and the OS/2
/// fsType embedding permission straight from raw sfnt bytes (Section 6.6).
/// Used by the font index, the Font Report, and the FONT_LICENSES.txt
/// generator, so it must never need WPF.
/// </summary>
/// <remarks>
/// WPF can report most of this, but only by loading the font, which keeps the
/// file open for the life of the process and costs a parse. The index needs
/// the names once per new file and the licence generator runs in the build
/// script, so both read the bytes directly instead.
/// </remarks>
public static class FontInspector
{
    /// <summary>What the name and OS/2 tables say about one font file.</summary>
    public sealed record Info(
        IReadOnlyList<string> Families,
        string? FullName,
        string? Version,
        string? LicenseDescription,
        string? LicenseUrl,
        ushort FsType)
    {
        /// <summary>The copyright notice, name ID 0.</summary>
        public string? Copyright { get; init; }

        /// <summary>The face names - Regular, Bold, Bold Italic - one per font in the file.</summary>
        public IReadOnlyList<string> Faces { get; init; } = [];

        /// <summary>How many glyphs the font has, from maxp.</summary>
        public int GlyphCount { get; init; }

        /// <summary>
        /// True when the font has outlines for the basic Latin letters, so the
        /// picker can draw its name in itself (Section 6.7). Decided here,
        /// once, and stored in the index, so the picker never opens a file.
        /// </summary>
        public bool HasLatinLetters { get; init; }

        /// <summary>True when the font carries a CFF table rather than glyf outlines.</summary>
        public bool IsCff { get; init; }

        /// <summary>True when the font has an fvar table, so it is a variable font.</summary>
        public bool IsVariable { get; init; }

        /// <summary>
        /// What fsType allows, as a phrase for the Font Report. Bit 9 is the
        /// one that matters for Section 7.4.
        /// </summary>
        public string EmbeddingRights => (FsType & 0x000F) switch
        {
            _ when (FsType & 0x0200) != 0 => "Restricted (no embedding)",
            0 => "Installable",
            var _ when (FsType & 0x0008) != 0 => "Editable",
            var _ when (FsType & 0x0004) != 0 => "Preview and print",
            _ => $"fsType 0x{FsType:X4}",
        };
    }

    /// <summary>
    /// Reads one font file. A collection is described by its first font, with
    /// every font's families gathered into <see cref="Info.Families"/>.
    /// </summary>
    public static Info Read(Stream sfnt)
    {
        ArgumentNullException.ThrowIfNull(sfnt);

        using var buffer = new MemoryStream();
        sfnt.CopyTo(buffer);

        return Read(buffer.GetBuffer().AsSpan(0, (int)buffer.Length));
    }

    public static Info Read(ReadOnlySpan<byte> sfnt)
    {
        var families = new List<string>();
        var faces = new List<string>();

        Info? first = null;

        // Offsets, not copies: a font's tables are read in place. See
        // Sfnt.ReadDirectory for why that matters at folder scale.
        foreach (var start in Sfnt.FontOffsets(sfnt))
        {
            var info = ReadOne(sfnt, start);

            foreach (var family in info.Families)
            {
                if (!families.Contains(family, StringComparer.OrdinalIgnoreCase))
                {
                    families.Add(family);
                }
            }

            faces.AddRange(info.Faces);
            first ??= info;
        }

        if (first is null)
        {
            throw new FontFormatException("the file holds no fonts");
        }

        return first with { Families = families, Faces = faces };
    }

    private static Info ReadOne(ReadOnlySpan<byte> font, int start)
    {
        var (_, tables) = Sfnt.ReadDirectory(font, start);

        var hasHead = Sfnt.TryFind(font, tables, "head", out _);
        var hasName = Sfnt.TryFind(font, tables, "name", out var name);
        var hasCmap = Sfnt.TryFind(font, tables, "cmap", out var cmap);
        var hasCff = Sfnt.TryFind(font, tables, "CFF ", out _) || Sfnt.TryFind(font, tables, "CFF2", out _);
        var hasGlyf = Sfnt.TryFind(font, tables, "glyf", out _);
        var hasLoca = Sfnt.TryFind(font, tables, "loca", out _);

        // The Section 6.6.2 validation step, in the one place that reads the
        // tables anyway. A font missing any of these will not render.
        if (!hasHead)
        {
            throw new FontFormatException("the font has no head table");
        }

        if (!hasName)
        {
            throw new FontFormatException("the font has no name table");
        }

        if (!hasCmap)
        {
            throw new FontFormatException("the font has no cmap table, so no character maps to a glyph");
        }

        if (!hasCff && (!hasGlyf || !hasLoca))
        {
            throw new FontFormatException("the font has neither glyf outlines nor a CFF table");
        }

        var names = ReadNames(name);

        var glyphCount = Sfnt.TryFind(font, tables, "maxp", out var maxp) && maxp.Length >= 6
            ? BinaryPrimitives.ReadUInt16BigEndian(maxp[4..])
            : 0;

        var fsType = Sfnt.TryFind(font, tables, "OS/2", out var os2) && os2.Length >= 10
            ? BinaryPrimitives.ReadUInt16BigEndian(os2[8..])
            : (ushort)0;

        var families = names.TryGetValue(1, out var family) ? new List<string> { family } : [];

        // The typographic family (ID 16) is the one that groups a large family
        // such as "Source Sans 3" whose ID 1 is split into "Source Sans 3
        // Light", "Source Sans 3 Black" and so on.
        if (names.TryGetValue(16, out var typographic)
            && !families.Contains(typographic, StringComparer.OrdinalIgnoreCase))
        {
            families.Insert(0, typographic);
        }

        return new Info(
            Families: families,
            FullName: names.GetValueOrDefault(4),
            Version: names.GetValueOrDefault(5),
            LicenseDescription: names.GetValueOrDefault(13),
            LicenseUrl: names.GetValueOrDefault(14),
            FsType: fsType)
        {
            Copyright = names.GetValueOrDefault(0),
            Faces = names.TryGetValue(2, out var face) ? [face] : [],
            GlyphCount = glyphCount,
            HasLatinLetters = HasLatin(cmap),
            IsCff = hasCff,
            IsVariable = Sfnt.TryFind(font, tables, "fvar", out _),
        };
    }

    /// <summary>
    /// Reads the name table, preferring the Windows Unicode English record for
    /// each name ID and falling back to whatever else is there.
    /// </summary>
    private static Dictionary<int, string> ReadNames(ReadOnlySpan<byte> table)
    {
        var result = new Dictionary<int, string>();

        if (table.Length < 6)
        {
            return result;
        }

        var span = table;
        var count = BinaryPrimitives.ReadUInt16BigEndian(span[2..]);
        var storageOffset = BinaryPrimitives.ReadUInt16BigEndian(span[4..]);

        if (table.Length < 6 + (count * 12))
        {
            return result;
        }

        // Higher is better: an English Windows record wins over a Mac record,
        // which wins over anything else.
        var scores = new Dictionary<int, int>();

        for (var i = 0; i < count; i++)
        {
            var record = span[(6 + (i * 12))..];

            var platform = BinaryPrimitives.ReadUInt16BigEndian(record);
            var language = BinaryPrimitives.ReadUInt16BigEndian(record[4..]);
            var nameId = BinaryPrimitives.ReadUInt16BigEndian(record[6..]);
            var length = BinaryPrimitives.ReadUInt16BigEndian(record[8..]);
            var offset = BinaryPrimitives.ReadUInt16BigEndian(record[10..]);

            var start = storageOffset + offset;
            if (start < 0 || length > table.Length - start)
            {
                continue;
            }

            var score = platform switch
            {
                3 when language == 0x0409 => 4,      // Windows, US English
                3 => 3,                              // Windows, other language
                0 => 2,                              // Unicode
                1 when language == 0 => 1,           // Mac, English
                _ => 0,
            };

            if (scores.TryGetValue(nameId, out var existing) && existing >= score)
            {
                continue;
            }

            var bytes = span.Slice(start, length);

            // Platform 1 is MacRoman single-byte; everything else here is
            // UTF-16 big-endian. Getting this wrong shows up as names full of
            // null characters, which is how it was found.
            var text = platform == 1
                ? Encoding.Latin1.GetString(bytes)
                : Encoding.BigEndianUnicode.GetString(bytes);

            text = text.Trim('\0', ' ');

            if (text.Length == 0)
            {
                continue;
            }

            result[nameId] = text;
            scores[nameId] = score;
        }

        return result;
    }

    /// <summary>
    /// True when the cmap maps the basic Latin letters, so the font can draw
    /// its own name (Section 6.7).
    /// </summary>
    /// <remarks>
    /// Only formats 4 and 12 are read, which between them cover every modern
    /// font. A symbol font's (3,0) subtable is neither, so it answers false -
    /// which is the right answer for one.
    /// </remarks>
    private static bool HasLatin(ReadOnlySpan<byte> cmap)
    {
        try
        {
            return MapsCharacter(cmap, 'A') && MapsCharacter(cmap, 'a');
        }
        catch (Exception ex) when (ex is FontFormatException or ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    private static bool MapsCharacter(ReadOnlySpan<byte> cmap, uint codepoint)
    {
        if (cmap.Length < 4)
        {
            return false;
        }

        var span = cmap;
        var count = BinaryPrimitives.ReadUInt16BigEndian(span[2..]);

        if (cmap.Length < 4 + (count * 8))
        {
            return false;
        }

        for (var i = 0; i < count; i++)
        {
            var record = span[(4 + (i * 8))..];
            var offset = BinaryPrimitives.ReadUInt32BigEndian(record[4..]);

            if (offset + 4 > (uint)cmap.Length)
            {
                continue;
            }

            var subtable = span[(int)offset..];
            var format = BinaryPrimitives.ReadUInt16BigEndian(subtable);

            var glyph = format switch
            {
                4 => LookupFormat4(subtable, codepoint),
                12 => LookupFormat12(subtable, codepoint),
                _ => 0,
            };

            if (glyph != 0)
            {
                return true;
            }
        }

        return false;
    }

    private static int LookupFormat4(ReadOnlySpan<byte> subtable, uint codepoint)
    {
        if (codepoint > 0xFFFF || subtable.Length < 14)
        {
            return 0;
        }

        var segCountX2 = BinaryPrimitives.ReadUInt16BigEndian(subtable[6..]);
        var segCount = segCountX2 / 2;

        var endCodes = 14;
        var startCodes = endCodes + segCountX2 + 2;
        var deltas = startCodes + segCountX2;
        var rangeOffsets = deltas + segCountX2;

        if (subtable.Length < rangeOffsets + segCountX2)
        {
            return 0;
        }

        for (var s = 0; s < segCount; s++)
        {
            var end = BinaryPrimitives.ReadUInt16BigEndian(subtable[(endCodes + (s * 2))..]);
            if (codepoint > end)
            {
                continue;
            }

            var start = BinaryPrimitives.ReadUInt16BigEndian(subtable[(startCodes + (s * 2))..]);
            if (codepoint < start)
            {
                return 0;
            }

            var delta = BinaryPrimitives.ReadInt16BigEndian(subtable[(deltas + (s * 2))..]);
            var rangeOffset = BinaryPrimitives.ReadUInt16BigEndian(subtable[(rangeOffsets + (s * 2))..]);

            if (rangeOffset == 0)
            {
                return (ushort)(codepoint + delta);
            }

            var at = rangeOffsets + (s * 2) + rangeOffset + ((int)(codepoint - start) * 2);
            if (at + 2 > subtable.Length)
            {
                return 0;
            }

            var glyph = BinaryPrimitives.ReadUInt16BigEndian(subtable[at..]);
            return glyph == 0 ? 0 : (ushort)(glyph + delta);
        }

        return 0;
    }

    private static int LookupFormat12(ReadOnlySpan<byte> subtable, uint codepoint)
    {
        if (subtable.Length < 16)
        {
            return 0;
        }

        var groups = BinaryPrimitives.ReadUInt32BigEndian(subtable[12..]);
        if (groups > (uint)((subtable.Length - 16) / 12))
        {
            return 0;
        }

        for (var g = 0u; g < groups; g++)
        {
            var record = subtable[(int)(16 + (g * 12))..];
            var start = BinaryPrimitives.ReadUInt32BigEndian(record);
            var end = BinaryPrimitives.ReadUInt32BigEndian(record[4..]);

            if (codepoint < start)
            {
                return 0;
            }

            if (codepoint <= end)
            {
                return (int)(BinaryPrimitives.ReadUInt32BigEndian(record[8..]) + (codepoint - start));
            }
        }

        return 0;
    }
}
