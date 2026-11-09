using System.Buffers.Binary;
using System.Text;

namespace ScratchpadLite.Core.Fonts;

/// <summary>
/// Builds the tables a font needs besides its outlines: head, hhea, maxp,
/// hmtx, post, OS/2, name and cmap (Section 6.6.4, step 5).
/// </summary>
/// <remarks>
/// A Type 1 font carries none of these. It has outlines, widths and glyph
/// names, and everything an OpenType consumer expects to read - metrics,
/// character mapping, naming, embedding permission - has to be synthesised
/// from what the Type 1 font does say. This is the other half of what
/// Section 5.1 called SfntBuilder; the container assembly is
/// <see cref="Sfnt.Build"/>.
/// </remarks>
public static class SfntTables
{
    /// <summary>What the tables are built from.</summary>
    public sealed record Metrics
    {
        public int UnitsPerEm { get; init; } = 1000;

        /// <summary>xMin, yMin, xMax, yMax.</summary>
        public double[] BBox { get; init; } = [0, 0, 1000, 1000];

        public double ItalicAngle { get; init; }

        public bool IsFixedPitch { get; init; }

        public double UnderlinePosition { get; init; } = -100;

        public double UnderlineThickness { get; init; } = 50;

        /// <summary>Advance width per glyph, in glyph order.</summary>
        public IReadOnlyList<double> AdvanceWidths { get; init; } = [];

        /// <summary>Left side bearing per glyph, in glyph order.</summary>
        public IReadOnlyList<double> LeftSideBearings { get; init; } = [];

        /// <summary>Code point to glyph index. Empty means build a symbol cmap.</summary>
        public IReadOnlyDictionary<int, int> UnicodeToGlyph { get; init; } =
            new Dictionary<int, int>();

        /// <summary>
        /// Code to glyph index for a font whose glyph names mean nothing to the
        /// Adobe Glyph List - a symbol or dingbat font (Section 6.6.4).
        /// </summary>
        public IReadOnlyDictionary<int, int> SymbolCodeToGlyph { get; init; } =
            new Dictionary<int, int>();

        public string FamilyName { get; init; } = "Converted Font";

        public string StyleName { get; init; } = "Regular";

        public string FullName { get; init; } = "Converted Font";

        public string PostScriptName { get; init; } = "ConvertedFont";

        public string? Copyright { get; init; }

        public string Version { get; init; } = "Version 1.000";

        /// <summary>100 to 900, from the Weight string or the PFM.</summary>
        public int WeightClass { get; init; } = 400;

        public bool IsBold { get; init; }

        public bool IsItalic { get; init; }

        /// <summary>
        /// Type 1 has no embedding flag, so Section 6.6.4 says to set Preview
        /// and Print.
        /// </summary>
        public ushort FsType { get; init; } = 0x0004;

        public double Ascender { get; init; }

        public double Descender { get; init; }
    }

    /// <summary>Builds every table except the outlines.</summary>
    public static List<Sfnt.Table> Build(Metrics metrics, bool cffOutlines)
    {
        ArgumentNullException.ThrowIfNull(metrics);

        var glyphCount = metrics.AdvanceWidths.Count;

        if (glyphCount == 0)
        {
            throw new FontFormatException("a font needs at least one glyph");
        }

        var ascender = metrics.Ascender != 0 ? metrics.Ascender : Math.Max(metrics.BBox[3], 0);
        var descender = metrics.Descender != 0 ? metrics.Descender : Math.Min(metrics.BBox[1], 0);

        var tables = new List<Sfnt.Table>
        {
            new(Sfnt.StringToTag("head"), Head(metrics)),
            new(Sfnt.StringToTag("hhea"), Hhea(metrics, ascender, descender, glyphCount)),
            new(Sfnt.StringToTag("maxp"), Maxp(glyphCount, cffOutlines)),
            new(Sfnt.StringToTag("hmtx"), Hmtx(metrics, glyphCount)),
            new(Sfnt.StringToTag("post"), Post(metrics)),
            new(Sfnt.StringToTag("OS/2"), Os2(metrics, ascender, descender)),
            new(Sfnt.StringToTag("name"), Name(metrics)),
            new(Sfnt.StringToTag("cmap"), Cmap(metrics)),
        };

        return tables;
    }

    private static byte[] Head(Metrics metrics)
    {
        var head = new byte[54];
        var span = head.AsSpan();

        BinaryPrimitives.WriteUInt32BigEndian(span, 0x00010000);            // version 1.0
        BinaryPrimitives.WriteUInt32BigEndian(span[4..], 0x00010000);       // fontRevision
        // checkSumAdjustment at 8 is written by Sfnt.Build once the whole file
        // exists.
        BinaryPrimitives.WriteUInt32BigEndian(span[12..], 0x5F0F3CF5);      // magicNumber

        var flags = (ushort)0x0003;      // baseline at y=0, lsb at x=0
        BinaryPrimitives.WriteUInt16BigEndian(span[16..], flags);
        BinaryPrimitives.WriteUInt16BigEndian(span[18..], (ushort)metrics.UnitsPerEm);

        // created and modified: seconds since 1904, which is Apple's epoch.
        var seconds = (long)(DateTime.UtcNow - new DateTime(1904, 1, 1, 0, 0, 0, DateTimeKind.Utc))
            .TotalSeconds;

        BinaryPrimitives.WriteInt64BigEndian(span[20..], seconds);
        BinaryPrimitives.WriteInt64BigEndian(span[28..], seconds);

        BinaryPrimitives.WriteInt16BigEndian(span[36..], Clamp(metrics.BBox[0]));
        BinaryPrimitives.WriteInt16BigEndian(span[38..], Clamp(metrics.BBox[1]));
        BinaryPrimitives.WriteInt16BigEndian(span[40..], Clamp(metrics.BBox[2]));
        BinaryPrimitives.WriteInt16BigEndian(span[42..], Clamp(metrics.BBox[3]));

        var macStyle = (ushort)((metrics.IsBold ? 1 : 0) | (metrics.IsItalic ? 2 : 0));
        BinaryPrimitives.WriteUInt16BigEndian(span[44..], macStyle);
        BinaryPrimitives.WriteUInt16BigEndian(span[46..], 8);               // lowestRecPPEM
        BinaryPrimitives.WriteInt16BigEndian(span[48..], 2);                // fontDirectionHint
        BinaryPrimitives.WriteInt16BigEndian(span[50..], 0);                // indexToLocFormat
        BinaryPrimitives.WriteInt16BigEndian(span[52..], 0);                // glyphDataFormat

        return head;
    }

    private static byte[] Hhea(Metrics metrics, double ascender, double descender, int glyphCount)
    {
        var hhea = new byte[36];
        var span = hhea.AsSpan();

        BinaryPrimitives.WriteUInt32BigEndian(span, 0x00010000);
        BinaryPrimitives.WriteInt16BigEndian(span[4..], Clamp(ascender));
        BinaryPrimitives.WriteInt16BigEndian(span[6..], Clamp(descender));
        BinaryPrimitives.WriteInt16BigEndian(span[8..], 0);                 // lineGap

        var maxAdvance = metrics.AdvanceWidths.Count > 0 ? metrics.AdvanceWidths.Max() : 0;
        var minLsb = metrics.LeftSideBearings.Count > 0 ? metrics.LeftSideBearings.Min() : 0;

        BinaryPrimitives.WriteUInt16BigEndian(span[10..], (ushort)Math.Clamp(maxAdvance, 0, ushort.MaxValue));
        BinaryPrimitives.WriteInt16BigEndian(span[12..], Clamp(minLsb));
        BinaryPrimitives.WriteInt16BigEndian(span[14..], Clamp(metrics.BBox[2]));   // minRightSideBearing
        BinaryPrimitives.WriteInt16BigEndian(span[16..], Clamp(metrics.BBox[2]));   // xMaxExtent
        BinaryPrimitives.WriteInt16BigEndian(span[18..], 1);                // caretSlopeRise
        BinaryPrimitives.WriteInt16BigEndian(span[20..], 0);                // caretSlopeRun
        BinaryPrimitives.WriteUInt16BigEndian(span[34..], (ushort)glyphCount);

        return hhea;
    }

    private static byte[] Maxp(int glyphCount, bool cffOutlines)
    {
        // Version 0.5 for CFF outlines - it is six bytes and holds only the
        // glyph count, because everything else in the long version describes
        // glyf data that a CFF font does not have.
        if (cffOutlines)
        {
            var maxp = new byte[6];
            BinaryPrimitives.WriteUInt32BigEndian(maxp, 0x00005000);
            BinaryPrimitives.WriteUInt16BigEndian(maxp.AsSpan(4), (ushort)glyphCount);

            return maxp;
        }

        var full = new byte[32];
        BinaryPrimitives.WriteUInt32BigEndian(full, 0x00010000);
        BinaryPrimitives.WriteUInt16BigEndian(full.AsSpan(4), (ushort)glyphCount);

        return full;
    }

    private static byte[] Hmtx(Metrics metrics, int glyphCount)
    {
        var hmtx = new byte[glyphCount * 4];

        for (var i = 0; i < glyphCount; i++)
        {
            var advance = (ushort)Math.Clamp(metrics.AdvanceWidths[i], 0, ushort.MaxValue);
            var lsb = i < metrics.LeftSideBearings.Count ? metrics.LeftSideBearings[i] : 0;

            BinaryPrimitives.WriteUInt16BigEndian(hmtx.AsSpan(i * 4), advance);
            BinaryPrimitives.WriteInt16BigEndian(hmtx.AsSpan((i * 4) + 2), Clamp(lsb));
        }

        return hmtx;
    }

    private static byte[] Post(Metrics metrics)
    {
        // Format 3 says "no glyph names in this table", which is right for a
        // CFF font: its names are in the charset.
        var post = new byte[32];
        var span = post.AsSpan();

        BinaryPrimitives.WriteUInt32BigEndian(span, 0x00030000);

        // italicAngle is 16.16 fixed point, and it is the one field here that
        // is easy to get wrong by writing it as an integer.
        BinaryPrimitives.WriteInt32BigEndian(span[4..], (int)Math.Round(metrics.ItalicAngle * 65536));

        BinaryPrimitives.WriteInt16BigEndian(span[8..], Clamp(metrics.UnderlinePosition));
        BinaryPrimitives.WriteInt16BigEndian(span[10..], Clamp(metrics.UnderlineThickness));
        BinaryPrimitives.WriteUInt32BigEndian(span[12..], metrics.IsFixedPitch ? 1u : 0u);

        return post;
    }

    private static byte[] Os2(Metrics metrics, double ascender, double descender)
    {
        var os2 = new byte[96];                     // version 4
        var span = os2.AsSpan();

        BinaryPrimitives.WriteUInt16BigEndian(span, 4);
        BinaryPrimitives.WriteInt16BigEndian(span[2..], AverageWidth(metrics));
        BinaryPrimitives.WriteUInt16BigEndian(span[4..], (ushort)metrics.WeightClass);
        BinaryPrimitives.WriteUInt16BigEndian(span[6..], 5);                // widthClass: medium
        BinaryPrimitives.WriteUInt16BigEndian(span[8..], metrics.FsType);

        // Subscript, superscript and strikeout metrics, as fractions of the em
        // that match what most fonts use.
        var em = metrics.UnitsPerEm;
        BinaryPrimitives.WriteInt16BigEndian(span[10..], (short)(em * 0.65));    // ySubscriptXSize
        BinaryPrimitives.WriteInt16BigEndian(span[12..], (short)(em * 0.65));    // ySubscriptYSize
        BinaryPrimitives.WriteInt16BigEndian(span[14..], 0);
        BinaryPrimitives.WriteInt16BigEndian(span[16..], (short)(em * 0.075));   // ySubscriptYOffset
        BinaryPrimitives.WriteInt16BigEndian(span[18..], (short)(em * 0.65));
        BinaryPrimitives.WriteInt16BigEndian(span[20..], (short)(em * 0.65));
        BinaryPrimitives.WriteInt16BigEndian(span[22..], 0);
        BinaryPrimitives.WriteInt16BigEndian(span[24..], (short)(em * 0.35));    // ySuperscriptYOffset
        BinaryPrimitives.WriteInt16BigEndian(span[26..], (short)(em * 0.05));    // yStrikeoutSize
        BinaryPrimitives.WriteInt16BigEndian(span[28..], (short)(em * 0.26));    // yStrikeoutPosition
        BinaryPrimitives.WriteInt16BigEndian(span[30..], 0);                     // sFamilyClass

        // PANOSE. All zeros means "any", which is honest: nothing in a Type 1
        // font says what its PANOSE classification is.
        os2[32] = 2;                                // family kind: Latin text
        os2[35] = (byte)(metrics.IsFixedPitch ? 9 : 0);

        // Unicode and code page ranges. A symbol font gets the symbol code
        // page and nothing else, which is what tells Windows to look for a
        // (3,0) cmap (Section 6.6.4).
        if (metrics.SymbolCodeToGlyph.Count > 0)
        {
            BinaryPrimitives.WriteUInt32BigEndian(span[42..], 0x80000000);  // ulUnicodeRange1 bit 31
            BinaryPrimitives.WriteUInt32BigEndian(span[78..], 0x80000000);  // symbol code page
        }
        else
        {
            BinaryPrimitives.WriteUInt32BigEndian(span[42..], 0x00000003);  // Basic Latin, Latin-1
            BinaryPrimitives.WriteUInt32BigEndian(span[78..], 0x00000001);  // Latin-1 code page
        }

        Encoding.ASCII.GetBytes("SPLT").CopyTo(span[58..]);                 // achVendID

        var selection = (ushort)0;
        if (metrics.IsItalic)
        {
            selection |= 0x0001;
        }

        if (metrics.IsBold)
        {
            selection |= 0x0020;
        }

        if (selection == 0)
        {
            selection = 0x0040;                     // REGULAR
        }

        selection |= 0x0080;                        // USE_TYPO_METRICS

        BinaryPrimitives.WriteUInt16BigEndian(span[62..], selection);

        var first = FirstAndLastChar(metrics);
        BinaryPrimitives.WriteUInt16BigEndian(span[64..], first.First);
        BinaryPrimitives.WriteUInt16BigEndian(span[66..], first.Last);

        BinaryPrimitives.WriteInt16BigEndian(span[68..], Clamp(ascender));   // sTypoAscender
        BinaryPrimitives.WriteInt16BigEndian(span[70..], Clamp(descender));  // sTypoDescender
        BinaryPrimitives.WriteInt16BigEndian(span[72..], 0);                 // sTypoLineGap
        BinaryPrimitives.WriteUInt16BigEndian(span[74..], (ushort)Math.Max(0, ascender));
        BinaryPrimitives.WriteUInt16BigEndian(span[76..], (ushort)Math.Max(0, -descender));

        BinaryPrimitives.WriteInt16BigEndian(span[86..], (short)(em * 0.5));  // sxHeight
        BinaryPrimitives.WriteInt16BigEndian(span[88..], (short)(em * 0.7));  // sCapHeight
        BinaryPrimitives.WriteUInt16BigEndian(span[90..], 0);                 // usDefaultChar
        BinaryPrimitives.WriteUInt16BigEndian(span[92..], 32);                // usBreakChar
        BinaryPrimitives.WriteUInt16BigEndian(span[94..], 2);                 // usMaxContext

        return os2;
    }

    private static (ushort First, ushort Last) FirstAndLastChar(Metrics metrics)
    {
        var codes = metrics.UnicodeToGlyph.Count > 0
            ? metrics.UnicodeToGlyph.Keys.AsEnumerable()
            : metrics.SymbolCodeToGlyph.Keys.AsEnumerable();

        var inRange = codes.Where(c => c is > 0 and <= 0xFFFF).ToList();

        if (inRange.Count == 0)
        {
            return ((ushort)0, (ushort)0);
        }

        return ((ushort)inRange.Min(), (ushort)inRange.Max());
    }

    private static short AverageWidth(Metrics metrics)
    {
        var widths = metrics.AdvanceWidths.Where(w => w > 0).ToList();

        return widths.Count == 0 ? (short)0 : Clamp(widths.Average());
    }

    /// <summary>
    /// The name table, with the four records every Windows consumer reads plus
    /// the full and PostScript names.
    /// </summary>
    /// <remarks>
    /// Only platform 3 (Windows), encoding 1 (UCS-2), language 0x409 records
    /// are written. A Mac platform record would need MacRoman, and nothing on
    /// Windows reads it - WPF included.
    /// </remarks>
    private static byte[] Name(Metrics metrics)
    {
        (int Id, string Value)[] records =
        [
            (0, metrics.Copyright ?? ""),
            (1, metrics.FamilyName),
            (2, metrics.StyleName),
            (3, $"{metrics.FamilyName};{metrics.StyleName};Scratchpad Lite"),
            (4, metrics.FullName),
            (5, metrics.Version),
            (6, metrics.PostScriptName),
        ];

        var present = records.Where(r => !string.IsNullOrWhiteSpace(r.Value)).ToList();

        var strings = new MemoryStream();
        var offsets = new List<(int Id, int Offset, int Length)>(present.Count);

        foreach (var (id, value) in present)
        {
            var bytes = Encoding.BigEndianUnicode.GetBytes(value);
            offsets.Add((id, (int)strings.Length, bytes.Length));
            strings.Write(bytes);
        }

        var table = new byte[6 + (present.Count * 12) + (int)strings.Length];
        var span = table.AsSpan();

        BinaryPrimitives.WriteUInt16BigEndian(span, 0);                      // format 0
        BinaryPrimitives.WriteUInt16BigEndian(span[2..], (ushort)present.Count);
        BinaryPrimitives.WriteUInt16BigEndian(span[4..], (ushort)(6 + (present.Count * 12)));

        for (var i = 0; i < offsets.Count; i++)
        {
            var record = span[(6 + (i * 12))..];
            var (id, offset, length) = offsets[i];

            BinaryPrimitives.WriteUInt16BigEndian(record, 3);                // Windows
            BinaryPrimitives.WriteUInt16BigEndian(record[2..], 1);           // UCS-2
            BinaryPrimitives.WriteUInt16BigEndian(record[4..], 0x0409);      // US English
            BinaryPrimitives.WriteUInt16BigEndian(record[6..], (ushort)id);
            BinaryPrimitives.WriteUInt16BigEndian(record[8..], (ushort)length);
            BinaryPrimitives.WriteUInt16BigEndian(record[10..], (ushort)offset);
        }

        strings.GetBuffer().AsSpan(0, (int)strings.Length)
            .CopyTo(span[(6 + (present.Count * 12))..]);

        return table;
    }

    /// <summary>
    /// The cmap: format 4 for a text font, a (3,0) symbol subtable for a
    /// symbol font, and format 12 as well when anything is above U+FFFF
    /// (Section 6.6.4).
    /// </summary>
    private static byte[] Cmap(Metrics metrics)
    {
        var subtables = new List<(ushort Platform, ushort Encoding, byte[] Data)>();

        if (metrics.SymbolCodeToGlyph.Count > 0)
        {
            // A symbol font's codes live at U+F020 to U+F0FF, which is the
            // private-use area Windows uses for exactly this.
            var mapped = metrics.SymbolCodeToGlyph
                .Where(pair => pair.Key is >= 0x20 and <= 0xFF)
                .ToDictionary(pair => 0xF000 + pair.Key, pair => pair.Value);

            if (mapped.Count > 0)
            {
                subtables.Add((3, 0, Format4(mapped)));
            }
        }

        var unicode = metrics.UnicodeToGlyph
            .Where(pair => pair.Key is > 0 and <= 0xFFFF)
            .ToDictionary(pair => pair.Key, pair => pair.Value);

        if (unicode.Count > 0)
        {
            subtables.Add((3, 1, Format4(unicode)));
        }

        var above = metrics.UnicodeToGlyph.Where(pair => pair.Key > 0xFFFF).ToList();

        if (above.Count > 0)
        {
            subtables.Add((3, 10, Format12(metrics.UnicodeToGlyph)));
        }

        if (subtables.Count == 0)
        {
            // A font with no mapping at all will not render a single character,
            // so an empty format 4 is written rather than no cmap: that keeps
            // the font valid and the problem visible in the Font Report.
            subtables.Add((3, 1, Format4(new Dictionary<int, int>())));
        }

        var headerSize = 4 + (subtables.Count * 8);
        var total = headerSize + subtables.Sum(s => s.Data.Length);
        var cmap = new byte[total];
        var span = cmap.AsSpan();

        BinaryPrimitives.WriteUInt16BigEndian(span, 0);
        BinaryPrimitives.WriteUInt16BigEndian(span[2..], (ushort)subtables.Count);

        var at = headerSize;

        for (var i = 0; i < subtables.Count; i++)
        {
            var record = span[(4 + (i * 8))..];
            var (platform, encoding, data) = subtables[i];

            BinaryPrimitives.WriteUInt16BigEndian(record, platform);
            BinaryPrimitives.WriteUInt16BigEndian(record[2..], encoding);
            BinaryPrimitives.WriteUInt32BigEndian(record[4..], (uint)at);

            data.CopyTo(cmap, at);
            at += data.Length;
        }

        return cmap;
    }

    /// <summary>
    /// A format 4 subtable, built as one segment per run of consecutive codes
    /// that map to consecutive glyphs.
    /// </summary>
    private static byte[] Format4(Dictionary<int, int> map)
    {
        var codes = map.Keys.Where(c => c is >= 0 and <= 0xFFFF).Order().ToList();
        var segments = new List<(int Start, int End, int Delta)>();

        var i = 0;
        while (i < codes.Count)
        {
            var start = codes[i];
            var delta = map[start] - start;
            var end = start;

            // A run continues while both the code and the glyph step by one,
            // which is what lets idDelta describe the whole segment.
            while (i + 1 < codes.Count
                   && codes[i + 1] == end + 1
                   && map[codes[i + 1]] - codes[i + 1] == delta)
            {
                end = codes[++i];
            }

            segments.Add((start, end, delta));
            i++;
        }

        // The required final segment, which maps 0xFFFF to nothing.
        segments.Add((0xFFFF, 0xFFFF, 1));

        var segCount = segments.Count;
        var length = 16 + (segCount * 8);
        var table = new byte[length];
        var span = table.AsSpan();

        BinaryPrimitives.WriteUInt16BigEndian(span, 4);
        BinaryPrimitives.WriteUInt16BigEndian(span[2..], (ushort)length);
        BinaryPrimitives.WriteUInt16BigEndian(span[4..], 0);                // language
        BinaryPrimitives.WriteUInt16BigEndian(span[6..], (ushort)(segCount * 2));

        var entrySelector = (int)Math.Floor(Math.Log2(segCount));
        BinaryPrimitives.WriteUInt16BigEndian(span[8..], (ushort)(1 << (entrySelector + 1)));
        BinaryPrimitives.WriteUInt16BigEndian(span[10..], (ushort)entrySelector);
        BinaryPrimitives.WriteUInt16BigEndian(
            span[12..], (ushort)((segCount * 2) - (1 << (entrySelector + 1))));

        var endCodes = 14;
        var startCodes = endCodes + (segCount * 2) + 2;
        var deltas = startCodes + (segCount * 2);
        var rangeOffsets = deltas + (segCount * 2);

        for (var s = 0; s < segCount; s++)
        {
            var (start, end, delta) = segments[s];

            BinaryPrimitives.WriteUInt16BigEndian(span[(endCodes + (s * 2))..], (ushort)end);
            BinaryPrimitives.WriteUInt16BigEndian(span[(startCodes + (s * 2))..], (ushort)start);
            BinaryPrimitives.WriteInt16BigEndian(span[(deltas + (s * 2))..], (short)delta);
            BinaryPrimitives.WriteUInt16BigEndian(span[(rangeOffsets + (s * 2))..], 0);
        }

        return table;
    }

    /// <summary>A format 12 subtable, for code points above U+FFFF.</summary>
    private static byte[] Format12(IReadOnlyDictionary<int, int> map)
    {
        var codes = map.Keys.Order().ToList();
        var groups = new List<(int Start, int End, int Glyph)>();

        var i = 0;
        while (i < codes.Count)
        {
            var start = codes[i];
            var glyph = map[start];
            var end = start;

            while (i + 1 < codes.Count
                   && codes[i + 1] == end + 1
                   && map[codes[i + 1]] == map[end] + 1)
            {
                end = codes[++i];
            }

            groups.Add((start, end, glyph));
            i++;
        }

        var length = 16 + (groups.Count * 12);
        var table = new byte[length];
        var span = table.AsSpan();

        BinaryPrimitives.WriteUInt16BigEndian(span, 12);
        BinaryPrimitives.WriteUInt16BigEndian(span[2..], 0);
        BinaryPrimitives.WriteUInt32BigEndian(span[4..], (uint)length);
        BinaryPrimitives.WriteUInt32BigEndian(span[8..], 0);                // language
        BinaryPrimitives.WriteUInt32BigEndian(span[12..], (uint)groups.Count);

        for (var g = 0; g < groups.Count; g++)
        {
            var record = span[(16 + (g * 12))..];
            var (start, end, glyph) = groups[g];

            BinaryPrimitives.WriteUInt32BigEndian(record, (uint)start);
            BinaryPrimitives.WriteUInt32BigEndian(record[4..], (uint)end);
            BinaryPrimitives.WriteUInt32BigEndian(record[8..], (uint)glyph);
        }

        return table;
    }

    private static short Clamp(double value) =>
        (short)Math.Clamp(Math.Round(value), short.MinValue, short.MaxValue);
}
