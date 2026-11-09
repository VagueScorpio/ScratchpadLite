using System.Buffers.Binary;
using System.Text;

namespace ScratchpadLite.Core.Fonts.Type1;

/// <summary>
/// Writes the CFF table a converted Type 1 font needs: header, Name INDEX,
/// Top DICT, String INDEX, CharStrings INDEX, charset and Private DICT
/// (Section 6.6.4, step 4).
/// </summary>
/// <remarks>
/// CFF is a compact format with one awkward property: the Top DICT holds byte
/// offsets to the charset, the CharStrings INDEX and the Private DICT, and
/// those offsets depend on how large the Top DICT is, which depends on how
/// large the offsets are. The usual answer is to iterate until it settles;
/// this writer instead makes every offset a fixed five-byte operand (the
/// 29 encoding, a full 32-bit integer), so the Top DICT's size is known before
/// the offsets are, and one pass is enough. A handful of wasted bytes in a
/// cache file is a good trade for not having a convergence loop.
/// </remarks>
public static class CffWriter
{
    /// <summary>One glyph as the writer needs it.</summary>
    public sealed record Glyph(string Name, byte[] CharString);

    /// <summary>What the Top DICT says about the font.</summary>
    public sealed record Info
    {
        public string FontName { get; init; } = "Converted";

        public string? FullName { get; init; }

        public string? FamilyName { get; init; }

        public string? Weight { get; init; }

        public string? Notice { get; init; }

        public double ItalicAngle { get; init; }

        public bool IsFixedPitch { get; init; }

        public double UnderlinePosition { get; init; } = -100;

        public double UnderlineThickness { get; init; } = 50;

        public double[] FontBBox { get; init; } = [0, 0, 1000, 1000];

        public double[] BlueValues { get; init; } = [];

        public double[] OtherBlues { get; init; } = [];

        public double StdHw { get; init; }

        public double StdVw { get; init; }

        /// <summary>Units per em, which Type 1's FontMatrix usually makes 1000.</summary>
        public int UnitsPerEm { get; init; } = 1000;
    }

    // Top DICT operators.
    private const int OpNotice = 1;
    private const int OpFullName = 2;
    private const int OpFamilyName = 3;
    private const int OpWeight = 4;
    private const int OpFontBBox = 5;
    private const int OpCharset = 15;
    private const int OpCharStrings = 17;
    private const int OpPrivate = 18;

    // Two-byte Top DICT operators (12 x).
    private const int OpIsFixedPitch = 1;
    private const int OpItalicAngle = 2;
    private const int OpUnderlinePosition = 3;
    private const int OpUnderlineThickness = 4;
    private const int OpFontMatrix = 7;

    // Private DICT operators.
    private const int OpBlueValues = 6;
    private const int OpOtherBlues = 7;
    private const int OpStdHw = 10;
    private const int OpStdVw = 11;
    private const int OpDefaultWidthX = 20;
    private const int OpNominalWidthX = 21;

    /// <summary>The number of names CFF has built in; anything else is a SID we add.</summary>
    private const int StandardStringCount = 391;

    public static byte[] Write(Info info, IReadOnlyList<Glyph> glyphs)
    {
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(glyphs);

        if (glyphs.Count == 0)
        {
            throw new FontFormatException("a CFF font needs at least one glyph");
        }

        if (glyphs[0].Name != ".notdef")
        {
            throw new FontFormatException("glyph 0 of a CFF font must be .notdef");
        }

        var strings = new StringTable();

        // Glyph names become SIDs, in glyph order, which is what the charset
        // records. Glyph 0 is .notdef and is never in the charset.
        var charsetSids = new ushort[glyphs.Count - 1];
        for (var i = 1; i < glyphs.Count; i++)
        {
            charsetSids[i - 1] = strings.IdFor(glyphs[i].Name);
        }

        var topDict = BuildTopDict(info, strings, out var charsetSlot, out var charStringsSlot, out var privateSlot);
        var privateDict = BuildPrivateDict(info);

        var nameIndex = BuildIndex([Encoding.ASCII.GetBytes(info.FontName)]);
        var stringIndex = BuildIndex(strings.Added.Select(Encoding.ASCII.GetBytes).ToArray());
        var globalSubrIndex = BuildIndex([]);
        var charset = BuildCharset(charsetSids);
        var charStringsIndex = BuildIndex(glyphs.Select(g => g.CharString).ToArray());

        var header = new byte[] { 1, 0, 4, 2 };          // major, minor, hdrSize, offSize

        // The Top DICT's own INDEX is measured before the offsets are known,
        // which is safe only because every reserved offset is a fixed five
        // bytes: patching one cannot change the DICT's length, so it cannot
        // change the length of the INDEX wrapping it.
        var topDictIndexLength = BuildIndex([topDict]).Length;

        var charsetOffset = header.Length + nameIndex.Length + topDictIndexLength
                            + stringIndex.Length + globalSubrIndex.Length;
        var charStringsOffset = charsetOffset + charset.Length;
        var privateOffset = charStringsOffset + charStringsIndex.Length;

        PatchOffset(topDict, charsetSlot, charsetOffset);
        PatchOffset(topDict, charStringsSlot, charStringsOffset);
        PatchOffset(topDict, privateSlot, privateOffset);

        // Built AFTER the patching, and this is the whole reason the order
        // matters: BuildIndex copies the bytes it is given, so an INDEX made
        // before the patch carries the unpatched DICT. The font then names a
        // CharStrings offset of zero, and DirectWrite rejects it with nothing
        // more specific than "Invalid file format".
        var topDictIndex = BuildIndex([topDict]);

        using var cff = new MemoryStream();
        cff.Write(header);
        cff.Write(nameIndex);
        cff.Write(topDictIndex);
        cff.Write(stringIndex);
        cff.Write(globalSubrIndex);
        cff.Write(charset);
        cff.Write(charStringsIndex);
        cff.Write(privateDict);

        return cff.ToArray();
    }

    private static byte[] BuildTopDict(
        Info info,
        StringTable strings,
        out int charsetSlot,
        out int charStringsSlot,
        out int privateSlot)
    {
        var dict = new List<byte>(128);

        void AddString(int op, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                AddOperand(dict, strings.IdFor(value));
                dict.Add((byte)op);
            }
        }

        AddString(OpNotice, info.Notice);
        AddString(OpFullName, info.FullName);
        AddString(OpFamilyName, info.FamilyName);
        AddString(OpWeight, info.Weight);

        foreach (var value in info.FontBBox)
        {
            AddOperand(dict, (int)Math.Round(value));
        }

        dict.Add(OpFontBBox);

        if (info.IsFixedPitch)
        {
            AddOperand(dict, 1);
            dict.Add(12);
            dict.Add(OpIsFixedPitch);
        }

        if (info.ItalicAngle != 0)
        {
            AddOperand(dict, (int)Math.Round(info.ItalicAngle));
            dict.Add(12);
            dict.Add(OpItalicAngle);
        }

        AddOperand(dict, (int)Math.Round(info.UnderlinePosition));
        dict.Add(12);
        dict.Add(OpUnderlinePosition);

        AddOperand(dict, (int)Math.Round(info.UnderlineThickness));
        dict.Add(12);
        dict.Add(OpUnderlineThickness);

        // A FontMatrix is only needed when the em is not 1000 units, which is
        // CFF's default.
        if (info.UnitsPerEm != 1000)
        {
            AddReal(dict, 1.0 / info.UnitsPerEm);
            AddOperand(dict, 0);
            AddOperand(dict, 0);
            AddReal(dict, 1.0 / info.UnitsPerEm);
            AddOperand(dict, 0);
            AddOperand(dict, 0);
            dict.Add(12);
            dict.Add(OpFontMatrix);
        }

        charsetSlot = ReserveOffset(dict, OpCharset);
        charStringsSlot = ReserveOffset(dict, OpCharStrings);

        // Private takes two operands - size then offset - and only the offset
        // needs patching.
        AddOperand(dict, PrivateDictSize(info));
        privateSlot = ReserveOffset(dict, OpPrivate);

        return dict.ToArray();
    }

    /// <summary>
    /// Writes a five-byte integer operand and the operator, and returns where
    /// the operand's bytes start so the value can be filled in later.
    /// </summary>
    private static int ReserveOffset(List<byte> dict, int op)
    {
        dict.Add(29);
        var slot = dict.Count;

        dict.AddRange([0, 0, 0, 0]);
        dict.Add((byte)op);

        return slot;
    }

    private static void PatchOffset(byte[] dict, int slot, int value) =>
        BinaryPrimitives.WriteInt32BigEndian(dict.AsSpan(slot), value);

    private static byte[] BuildPrivateDict(Info info)
    {
        var dict = new List<byte>(64);

        if (info.BlueValues.Length > 0)
        {
            AddDelta(dict, info.BlueValues);
            dict.Add(OpBlueValues);
        }

        if (info.OtherBlues.Length > 0)
        {
            AddDelta(dict, info.OtherBlues);
            dict.Add(OpOtherBlues);
        }

        if (info.StdHw != 0)
        {
            AddOperand(dict, (int)Math.Round(info.StdHw));
            dict.Add(OpStdHw);
        }

        if (info.StdVw != 0)
        {
            AddOperand(dict, (int)Math.Round(info.StdVw));
            dict.Add(OpStdVw);
        }

        // Both zero, so every glyph's Type 2 charstring states its own width
        // as a plain leading number. See Type2Writer.
        AddOperand(dict, 0);
        dict.Add(OpDefaultWidthX);
        AddOperand(dict, 0);
        dict.Add(OpNominalWidthX);

        return dict.ToArray();
    }

    private static int PrivateDictSize(Info info) => BuildPrivateDict(info).Length;

    /// <summary>
    /// A delta-encoded array, which is how CFF stores the alignment zones:
    /// the first value, then the difference to each one after it.
    /// </summary>
    private static void AddDelta(List<byte> dict, double[] values)
    {
        var previous = 0.0;

        foreach (var value in values)
        {
            AddOperand(dict, (int)Math.Round(value - previous));
            previous = value;
        }
    }

    private static void AddOperand(List<byte> dict, int value)
    {
        switch (value)
        {
            case >= -107 and <= 107:
                dict.Add((byte)(value + 139));
                break;

            case >= 108 and <= 1131:
                var positive = value - 108;
                dict.Add((byte)(247 + (positive >> 8)));
                dict.Add((byte)(positive & 0xFF));
                break;

            case >= -1131 and <= -108:
                var negative = -value - 108;
                dict.Add((byte)(251 + (negative >> 8)));
                dict.Add((byte)(negative & 0xFF));
                break;

            case >= short.MinValue and <= short.MaxValue:
                dict.Add(28);
                dict.Add((byte)(value >> 8));
                dict.Add((byte)value);
                break;

            default:
                dict.Add(29);
                dict.Add((byte)(value >> 24));
                dict.Add((byte)(value >> 16));
                dict.Add((byte)(value >> 8));
                dict.Add((byte)value);
                break;
        }
    }

    /// <summary>
    /// A real number, in CFF's nibble-coded decimal form.
    /// </summary>
    private static void AddReal(List<byte> dict, double value)
    {
        var text = value.ToString("G8", System.Globalization.CultureInfo.InvariantCulture);
        var nibbles = new List<byte>(text.Length + 2);

        foreach (var c in text)
        {
            switch (c)
            {
                case >= '0' and <= '9':
                    nibbles.Add((byte)(c - '0'));
                    break;
                case '.':
                    nibbles.Add(0xA);
                    break;
                case 'E' or 'e':
                    nibbles.Add(0xB);
                    break;
                case '-':
                    nibbles.Add(nibbles.Count == 0 ? (byte)0xE : (byte)0xC);
                    break;
                case '+':
                    break;
                default:
                    break;
            }
        }

        nibbles.Add(0xF);                               // end marker

        if (nibbles.Count % 2 != 0)
        {
            nibbles.Add(0xF);
        }

        dict.Add(30);

        for (var i = 0; i < nibbles.Count; i += 2)
        {
            dict.Add((byte)((nibbles[i] << 4) | nibbles[i + 1]));
        }
    }

    /// <summary>
    /// A CFF INDEX: a count, an offset size, count+1 offsets, then the data.
    /// </summary>
    private static byte[] BuildIndex(IReadOnlyList<byte[]> items)
    {
        if (items.Count == 0)
        {
            // An empty INDEX is just a zero count, with no offSize byte.
            return [0, 0];
        }

        var total = items.Sum(item => item.Length);

        // The offset size is chosen from how big the data is, which is the one
        // place CFF lets you save bytes without a convergence problem.
        //
        // The parentheses are not optional. "total + 1 switch { ... }" parses
        // as "total + (1 switch { ... })", so offSize came out as the size of
        // the whole INDEX rather than 1 to 4 - and every offset was written in
        // that many bytes. A 105 KB CharStrings INDEX became a 95 MB one, and
        // the font was still structurally valid, so nothing else caught it.
        var offSize = (total + 1) switch
        {
            <= 0xFF => 1,
            <= 0xFFFF => 2,
            <= 0xFFFFFF => 3,
            _ => 4,
        };

        using var index = new MemoryStream();

        Span<byte> count = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(count, (ushort)items.Count);
        index.Write(count);
        index.WriteByte((byte)offSize);

        var offset = 1;
        WriteOffset(index, offset, offSize);

        foreach (var item in items)
        {
            offset += item.Length;
            WriteOffset(index, offset, offSize);
        }

        foreach (var item in items)
        {
            index.Write(item);
        }

        return index.ToArray();
    }

    private static void WriteOffset(Stream target, int value, int offSize)
    {
        for (var shift = (offSize - 1) * 8; shift >= 0; shift -= 8)
        {
            target.WriteByte((byte)(value >> shift));
        }
    }

    /// <summary>
    /// The charset, as format 0: one SID per glyph after .notdef, in glyph
    /// order.
    /// </summary>
    private static byte[] BuildCharset(ushort[] sids)
    {
        var charset = new byte[1 + (sids.Length * 2)];
        charset[0] = 0;

        for (var i = 0; i < sids.Length; i++)
        {
            BinaryPrimitives.WriteUInt16BigEndian(charset.AsSpan(1 + (i * 2)), sids[i]);
        }

        return charset;
    }

    /// <summary>
    /// Hands out string IDs for glyph names and the dictionary's text.
    /// </summary>
    /// <remarks>
    /// CFF has 391 built-in strings, and most of a Latin font's glyph names are
    /// among them, so a writer can save a few hundred bytes by looking each name
    /// up there first. This does not: every name becomes a new SID. Carrying
    /// that table would be 391 strings that have to be in exactly the right
    /// order to avoid mislabelling glyphs, to save a byte or so per glyph in a
    /// file that lives in a cache. A custom SID is as valid as a standard one.
    /// </remarks>
    private sealed class StringTable
    {
        private readonly Dictionary<string, ushort> _ids = new(StringComparer.Ordinal);

        internal List<string> Added { get; } = [];

        internal ushort IdFor(string? name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return 0;
            }

            if (_ids.TryGetValue(name, out var existing))
            {
                return existing;
            }

            var id = (ushort)(StandardStringCount + Added.Count);
            Added.Add(name);
            _ids[name] = id;

            return id;
        }
    }
}
