using ScratchpadLite.Core.Formats;
using System.Buffers.Binary;

namespace ScratchpadLite.Core.Fonts;

/// <summary>
/// Reading and writing the container every OpenType font comes in: a header,
/// a sorted table directory, and the tables themselves.
/// </summary>
/// <remarks>
/// Font files are big-endian, which nothing else in this project is, so every
/// read goes through <see cref="BinaryPrimitives"/> rather than by hand.
/// <para>
/// Everything here is bounds-checked against the buffer it was given
/// (Section 7.5: a font file from the font folder is untrusted input, and a
/// table directory that points past the end of the file is the first thing a
/// malformed one does).
/// </para>
/// </remarks>
public static class Sfnt
{
    /// <summary>The four bytes at the start of a TrueType-outline font.</summary>
    public const uint TrueTypeFlavor = 0x00010000;

    /// <summary>"OTTO" - the start of a font with CFF outlines.</summary>
    public const uint CffFlavor = 0x4F54544F;

    /// <summary>"ttcf" - a font collection holding several fonts.</summary>
    public const uint CollectionTag = 0x74746366;

    /// <summary>One table: its four-character tag and its bytes.</summary>
    public sealed record Table(uint Tag, byte[] Data)
    {
        public string TagName => TagToString(Tag);

        public override string ToString() => $"{TagName} ({Data.Length} bytes)";
    }

    /// <summary>The file extension a font with this flavor should be given.</summary>
    public static string ExtensionFor(uint flavor) => flavor switch
    {
        CffFlavor => ".otf",
        CollectionTag => ".ttc",
        _ => ".ttf",
    };

    public static string TagToString(uint tag) =>
        new([(char)(byte)(tag >> 24), (char)(byte)(tag >> 16), (char)(byte)(tag >> 8), (char)(byte)tag]);

    public static uint StringToTag(string tag)
    {
        ArgumentNullException.ThrowIfNull(tag);

        if (tag.Length != 4)
        {
            throw new ArgumentException("a table tag is four characters", nameof(tag));
        }

        return ((uint)tag[0] << 24) | ((uint)tag[1] << 16) | ((uint)tag[2] << 8) | tag[3];
    }

    /// <summary>
    /// Assembles a font file from its tables: the offset table, the directory
    /// sorted by tag, each table padded to four bytes, and the checksums.
    /// </summary>
    /// <remarks>
    /// The three header fields after the table count - searchRange,
    /// entrySelector and rangeShift - are a binary-search hint no modern
    /// consumer uses, but DirectWrite validates them, so a font with them
    /// wrong may load and may not. They are computed here rather than zeroed.
    /// </remarks>
    public static byte[] Build(uint flavor, IEnumerable<Table> tables)
    {
        ArgumentNullException.ThrowIfNull(tables);

        var sorted = tables.OrderBy(t => t.Tag).ToList();
        if (sorted.Count == 0)
        {
            throw new FontFormatException("the font has no tables");
        }

        if (sorted.Count > ushort.MaxValue)
        {
            throw new FontFormatException($"{sorted.Count} tables is more than a font can hold");
        }

        var count = (ushort)sorted.Count;
        var directorySize = 12 + (16 * sorted.Count);
        var total = directorySize + sorted.Sum(t => Padded(t.Data.Length));

        Limits.CheckDecodedFontSize(total);

        var file = new byte[total];
        var span = file.AsSpan();

        // Offset table.
        BinaryPrimitives.WriteUInt32BigEndian(span, flavor);
        BinaryPrimitives.WriteUInt16BigEndian(span[4..], count);

        var entrySelector = (ushort)(31 - System.Numerics.BitOperations.LeadingZeroCount(count));
        var searchRange = (ushort)(1 << (entrySelector + 4));

        BinaryPrimitives.WriteUInt16BigEndian(span[6..], searchRange);
        BinaryPrimitives.WriteUInt16BigEndian(span[8..], entrySelector);
        BinaryPrimitives.WriteUInt16BigEndian(span[10..], (ushort)((count * 16) - searchRange));

        var offset = directorySize;
        for (var i = 0; i < sorted.Count; i++)
        {
            var table = sorted[i];
            var record = span[(12 + (i * 16))..];

            BinaryPrimitives.WriteUInt32BigEndian(record, table.Tag);
            BinaryPrimitives.WriteUInt32BigEndian(record[4..], Checksum(table.Data));
            BinaryPrimitives.WriteUInt32BigEndian(record[8..], (uint)offset);
            BinaryPrimitives.WriteUInt32BigEndian(record[12..], (uint)table.Data.Length);

            table.Data.CopyTo(file, offset);
            offset += Padded(table.Data.Length);
        }

        WriteCheckSumAdjustment(file, sorted);

        return file;
    }

    /// <summary>
    /// Reads a table directory. Accepts a single font; for a collection, use
    /// <see cref="ReadCollection"/>.
    /// </summary>
    public static IReadOnlyList<Table> ReadTables(ReadOnlySpan<byte> font)
    {
        var (flavor, tables) = ReadTablesAt(font, 0);
        _ = flavor;
        return tables;
    }

    /// <summary>Reads the flavor and tables of the font at <paramref name="start"/>.</summary>
    public static (uint Flavor, IReadOnlyList<Table> Tables) ReadTablesAt(ReadOnlySpan<byte> font, int start)
    {
        if (font.Length < start + 12)
        {
            throw new FontFormatException("the file is too short to hold a font header");
        }

        var header = font[start..];
        var flavor = BinaryPrimitives.ReadUInt32BigEndian(header);
        var count = BinaryPrimitives.ReadUInt16BigEndian(header[4..]);

        if (font.Length < start + 12 + (count * 16))
        {
            throw new FontFormatException($"the table directory claims {count} tables but the file is too short");
        }

        var tables = new List<Table>(count);
        for (var i = 0; i < count; i++)
        {
            var record = header[(12 + (i * 16))..];
            var tag = BinaryPrimitives.ReadUInt32BigEndian(record);
            var offset = BinaryPrimitives.ReadUInt32BigEndian(record[8..]);
            var length = BinaryPrimitives.ReadUInt32BigEndian(record[12..]);

            // The whole point of this check: a table that points outside the
            // file is how a malformed font turns into an out-of-range read.
            if (offset > (uint)font.Length || length > (uint)font.Length - offset)
            {
                throw new FontFormatException(
                    $"table {TagToString(tag)} points outside the file "
                    + $"(offset {offset}, length {length}, file {font.Length} bytes)");
            }

            tables.Add(new Table(tag, font.Slice((int)offset, (int)length).ToArray()));
        }

        return (flavor, tables);
    }

    /// <summary>
    /// Reads every font in a .ttc or .otc collection. A single font comes back
    /// as a collection of one, so callers do not need two code paths.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<Table>> ReadCollection(ReadOnlySpan<byte> font)
    {
        if (font.Length < 12)
        {
            throw new FontFormatException("the file is too short to hold a font header");
        }

        if (BinaryPrimitives.ReadUInt32BigEndian(font) != CollectionTag)
        {
            return [ReadTables(font)];
        }

        var numFonts = BinaryPrimitives.ReadUInt32BigEndian(font[8..]);
        if (numFonts == 0 || numFonts > 1024)
        {
            throw new FontFormatException($"a collection of {numFonts} fonts is not plausible");
        }

        if (font.Length < 12 + (numFonts * 4))
        {
            throw new FontFormatException("the collection's font list runs past the end of the file");
        }

        var fonts = new List<IReadOnlyList<Table>>((int)numFonts);
        for (var i = 0; i < numFonts; i++)
        {
            var offset = BinaryPrimitives.ReadUInt32BigEndian(font[(12 + (i * 4))..]);
            if (offset > (uint)font.Length)
            {
                throw new FontFormatException($"font {i} of the collection starts past the end of the file");
            }

            fonts.Add(ReadTablesAt(font, (int)offset).Tables);
        }

        return fonts;
    }

    /// <summary>
    /// Where one table is, without copying it: a tag and a window on the
    /// font's own bytes.
    /// </summary>
    public readonly record struct TableRef(uint Tag, int Offset, int Length);

    /// <summary>
    /// Reads the table directory as offsets rather than copies.
    /// </summary>
    /// <remarks>
    /// This exists because <see cref="ReadTablesAt"/> copies every table, and
    /// for a reader that wants four of a font's twenty tables that is the whole
    /// font copied to look at a fifth of it. Preparing a 400 MB font folder
    /// allocated about 400 MB in table copies on top of the fonts themselves -
    /// close to 800 MB in flight for work that needs almost none.
    /// <para>
    /// The decoders still use the copying version: they are rebuilding a font
    /// and genuinely need its tables as bytes. Readers use this one.
    /// </para>
    /// </remarks>
    public static (uint Flavor, TableRef[] Tables) ReadDirectory(ReadOnlySpan<byte> font, int start = 0)
    {
        if (font.Length < start + 12)
        {
            throw new FontFormatException("the file is too short to hold a font header");
        }

        var header = font[start..];
        var flavor = BinaryPrimitives.ReadUInt32BigEndian(header);
        var count = BinaryPrimitives.ReadUInt16BigEndian(header[4..]);

        if (font.Length < start + 12 + (count * 16))
        {
            throw new FontFormatException($"the table directory claims {count} tables but the file is too short");
        }

        var tables = new TableRef[count];

        for (var i = 0; i < count; i++)
        {
            var record = header[(12 + (i * 16))..];
            var tag = BinaryPrimitives.ReadUInt32BigEndian(record);
            var offset = BinaryPrimitives.ReadUInt32BigEndian(record[8..]);
            var length = BinaryPrimitives.ReadUInt32BigEndian(record[12..]);

            if (offset > (uint)font.Length || length > (uint)font.Length - offset)
            {
                throw new FontFormatException(
                    $"table {TagToString(tag)} points outside the file "
                    + $"(offset {offset}, length {length}, file {font.Length} bytes)");
            }

            tables[i] = new TableRef(tag, (int)offset, (int)length);
        }

        return (flavor, tables);
    }

    /// <summary>
    /// The offsets of every font in a collection, or of the single font.
    /// </summary>
    public static int[] FontOffsets(ReadOnlySpan<byte> font)
    {
        if (font.Length < 12)
        {
            throw new FontFormatException("the file is too short to hold a font header");
        }

        if (BinaryPrimitives.ReadUInt32BigEndian(font) != CollectionTag)
        {
            return [0];
        }

        var numFonts = BinaryPrimitives.ReadUInt32BigEndian(font[8..]);

        if (numFonts == 0 || numFonts > 1024)
        {
            throw new FontFormatException($"a collection of {numFonts} fonts is not plausible");
        }

        if (font.Length < 12 + (numFonts * 4))
        {
            throw new FontFormatException("the collection's font list runs past the end of the file");
        }

        var offsets = new int[numFonts];

        for (var i = 0; i < numFonts; i++)
        {
            var offset = BinaryPrimitives.ReadUInt32BigEndian(font[(12 + (i * 4))..]);

            if (offset > (uint)font.Length)
            {
                throw new FontFormatException($"font {i} of the collection starts past the end of the file");
            }

            offsets[i] = (int)offset;
        }

        return offsets;
    }

    /// <summary>Looks up one table without copying it.</summary>
    public static bool TryFind(
        ReadOnlySpan<byte> font,
        TableRef[] tables,
        string tag,
        out ReadOnlySpan<byte> data)
    {
        ArgumentNullException.ThrowIfNull(tables);

        var wanted = StringToTag(tag);

        foreach (var table in tables)
        {
            if (table.Tag == wanted)
            {
                data = font.Slice(table.Offset, table.Length);
                return true;
            }
        }

        data = default;
        return false;
    }

    /// <summary>Finds one table, or null when the font does not have it.</summary>
    public static Table? Find(IReadOnlyList<Table> tables, string tag)
    {
        var wanted = StringToTag(tag);
        foreach (var table in tables)
        {
            if (table.Tag == wanted)
            {
                return table;
            }
        }

        return null;
    }

    internal static int Padded(int length) => (length + 3) & ~3;

    /// <summary>
    /// The sum of the table's bytes as big-endian 32-bit words, with the tail
    /// padded with zeros. Defined to wrap round, which is why it is unchecked.
    /// </summary>
    internal static uint Checksum(ReadOnlySpan<byte> data)
    {
        uint sum = 0;
        var whole = data.Length & ~3;

        for (var i = 0; i < whole; i += 4)
        {
            sum += BinaryPrimitives.ReadUInt32BigEndian(data[i..]);
        }

        if (whole != data.Length)
        {
            Span<byte> tail = stackalloc byte[4];
            data[whole..].CopyTo(tail);
            sum += BinaryPrimitives.ReadUInt32BigEndian(tail);
        }

        return sum;
    }

    /// <summary>
    /// Writes head.checkSumAdjustment: 0xB1B0AFBA minus the checksum of the
    /// whole file computed with that field zeroed.
    /// </summary>
    private static void WriteCheckSumAdjustment(byte[] file, IReadOnlyList<Table> sorted)
    {
        var headIndex = -1;
        var headTag = StringToTag("head");
        for (var i = 0; i < sorted.Count; i++)
        {
            if (sorted[i].Tag == headTag)
            {
                headIndex = i;
                break;
            }
        }

        if (headIndex < 0 || sorted[headIndex].Data.Length < 12)
        {
            // No head table: the font is invalid and FontInspector will say so
            // with a better message than this method could.
            return;
        }

        var headOffset = 12 + (sorted.Count * 16);
        for (var i = 0; i < headIndex; i++)
        {
            headOffset += Padded(sorted[i].Data.Length);
        }

        var adjustment = headOffset + 8;
        file.AsSpan(adjustment, 4).Clear();

        var whole = unchecked(0xB1B0AFBAu - Checksum(file));
        BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(adjustment), whole);
    }
}

/// <summary>
/// A font file that cannot be read. Always caught: a broken font goes into the
/// Font Report with its reason and never reaches the user as a crash
/// (Section 6.6.7).
/// </summary>
public sealed class FontFormatException : Exception
{
    public FontFormatException(string message) : base(message)
    {
    }

    public FontFormatException(string message, Exception inner) : base(message, inner)
    {
    }
}
