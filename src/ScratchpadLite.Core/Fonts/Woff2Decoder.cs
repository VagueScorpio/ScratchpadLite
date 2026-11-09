using System.Buffers.Binary;
using System.IO.Compression;
using ScratchpadLite.Core.Formats;

namespace ScratchpadLite.Core.Fonts;

/// <summary>
/// WOFF 2.0 to sfnt (Section 6.6.5). The whole table directory is one
/// Brotli stream, and the glyf and loca tables are stored in a transformed
/// form that has to be rebuilt. Ported from the logic of Google woff2,
/// which is MIT licensed, using built-in BrotliStream.
/// </summary>
/// <remarks>
/// WOFF2 is not a repackaging like WOFF 1.0. Three things are genuinely
/// different, and they are what the bulk of this file is:
/// <list type="number">
///   <item>
///     Every table is in one Brotli stream rather than deflated separately,
///     so nothing can be read until the whole thing is decompressed.
///   </item>
///   <item>
///     The table directory uses two variable-length integer encodings and
///     refers to common table tags by a five-bit index rather than by name.
///   </item>
///   <item>
///     <c>glyf</c> and <c>loca</c> are taken apart into seven parallel
///     streams and have to be rebuilt glyph by glyph. <c>loca</c> is not
///     stored at all - it is implied by the lengths of the glyphs that come
///     out. <c>hmtx</c> may also drop its left-side-bearing array, which is
///     then read back off the reconstructed glyph bounding boxes.
///   </item>
/// </list>
/// <para>
/// The point-coordinate decoding in <see cref="DecodeTriplets"/> follows the
/// spec's triplet-encoding table, which is the same arithmetic Google's woff2
/// uses. It is dense and there is no way to make it less so; the test for it
/// compares every glyph's advance width and outline against the original TTF,
/// which is the only check that really settles whether it is right.
/// </para>
/// <para>
/// Not supported: WOFF2 font collections, and the rest of Section 6.6.5 is.
/// See the note on <see cref="CollectionsNotSupported"/>.
/// </para>
/// </remarks>
public static class Woff2Decoder
{
    /// <summary>"wOF2".</summary>
    internal const uint Signature = 0x774F4632;

    public const string CollectionsNotSupported =
        "WOFF2 font collections are not supported. Use the .ttc or the individual .otf files.";

    private const int HeaderSize = 48;

    /// <summary>
    /// The 63 table tags WOFF2 can refer to by index. Order is fixed by the
    /// specification; an entry out of place would silently rename a table.
    /// </summary>
    private static readonly string[] KnownTags =
    [
        "cmap", "head", "hhea", "hmtx", "maxp", "name", "OS/2", "post",
        "cvt ", "fpgm", "glyf", "loca", "prep", "CFF ", "VORG", "EBDT",
        "EBLC", "gasp", "hdmx", "kern", "LTSH", "PCLT", "VDMX", "vhea",
        "vmtx", "BASE", "GDEF", "GPOS", "GSUB", "EBSC", "JSTF", "MATH",
        "CBDT", "CBLC", "COLR", "CPAL", "SVG ", "sbix", "acnt", "avar",
        "bdat", "bloc", "bsln", "cvar", "fdsc", "feat", "fmtx", "fvar",
        "gvar", "hsty", "just", "lcar", "mort", "morx", "opbd", "prop",
        "trak", "Zapf", "Silf", "Glat", "Gloc", "Feat", "Sill",
    ];

    private sealed record Entry(uint Tag, uint OriginalLength, uint TransformLength, int TransformVersion)
    {
        /// <summary>How many bytes this table takes up in the Brotli stream.</summary>
        internal uint StoredLength => Transformed ? TransformLength : OriginalLength;

        internal bool Transformed { get; } =
            (Tag == GlyfTag || Tag == LocaTag) ? TransformVersion == 0
            : Tag == HmtxTag && TransformVersion == 1;
    }

    private static readonly uint GlyfTag = Sfnt.StringToTag("glyf");
    private static readonly uint LocaTag = Sfnt.StringToTag("loca");
    private static readonly uint HmtxTag = Sfnt.StringToTag("hmtx");
    private static readonly uint HheaTag = Sfnt.StringToTag("hhea");
    private static readonly uint MaxpTag = Sfnt.StringToTag("maxp");

    public static byte[] ToSfnt(Stream woff2)
    {
        ArgumentNullException.ThrowIfNull(woff2);

        using var buffer = new MemoryStream();
        woff2.CopyTo(buffer);
        var data = buffer.GetBuffer().AsSpan(0, (int)buffer.Length);

        if (data.Length < HeaderSize)
        {
            throw new FontFormatException("the file is too short to be a WOFF2 font");
        }

        if (BinaryPrimitives.ReadUInt32BigEndian(data) != Signature)
        {
            throw new FontFormatException("this is not a WOFF 2.0 file");
        }

        var flavor = BinaryPrimitives.ReadUInt32BigEndian(data[4..]);
        var numTables = BinaryPrimitives.ReadUInt16BigEndian(data[12..]);
        var totalSfntSize = BinaryPrimitives.ReadUInt32BigEndian(data[16..]);
        var totalCompressedSize = BinaryPrimitives.ReadUInt32BigEndian(data[20..]);

        if (flavor == Sfnt.CollectionTag)
        {
            throw new FontFormatException(CollectionsNotSupported);
        }

        if (numTables == 0)
        {
            throw new FontFormatException("the WOFF2 file declares no tables");
        }

        Limits.CheckDecodedFontSize(totalSfntSize);

        var entries = ReadDirectory(data, numTables, out var directoryEnd);

        if (totalCompressedSize > (uint)(data.Length - directoryEnd))
        {
            throw new FontFormatException("the compressed block runs past the end of the file");
        }

        var decompressed = Decompress(
            data.Slice(directoryEnd, (int)totalCompressedSize),
            entries.Sum(e => (long)e.StoredLength));

        return Reassemble(flavor, entries, decompressed);
    }

    private static List<Entry> ReadDirectory(ReadOnlySpan<byte> data, int numTables, out int directoryEnd)
    {
        var reader = new Woff2Reader(data[HeaderSize..], "table directory");
        var entries = new List<Entry>(numTables);

        for (var i = 0; i < numTables; i++)
        {
            var flags = reader.ReadByte();
            var index = flags & 0x3F;
            var transformVersion = (flags >> 6) & 0x03;

            var tag = index == 0x3F
                ? reader.ReadUInt32()
                : Sfnt.StringToTag(KnownTags[index]);

            var originalLength = reader.ReadUIntBase128();

            // Only the three transformable tables carry a second length, and
            // only when their transform is actually in use. Reading one for
            // any other table would throw the whole directory out of step.
            var transformed =
                (tag == GlyfTag || tag == LocaTag) ? transformVersion == 0
                : tag == HmtxTag && transformVersion == 1;

            var transformLength = transformed ? reader.ReadUIntBase128() : 0u;

            entries.Add(new Entry(tag, originalLength, transformLength, transformVersion));
        }

        directoryEnd = HeaderSize + reader.Position;
        return entries;
    }

    private static byte[] Decompress(ReadOnlySpan<byte> compressed, long expected)
    {
        Limits.CheckDecodedFontSize(expected);

        var output = new byte[expected];

        try
        {
            using var source = new MemoryStream(compressed.ToArray(), writable: false);
            using var brotli = new BrotliStream(source, CompressionMode.Decompress);

            brotli.ReadExactly(output);

            if (brotli.ReadByte() != -1)
            {
                throw new FontFormatException(
                    "the Brotli stream holds more data than the table directory accounts for");
            }
        }
        catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException)
        {
            throw new FontFormatException("the Brotli stream is damaged", ex);
        }

        return output;
    }

    private static byte[] Reassemble(uint flavor, List<Entry> entries, byte[] decompressed)
    {
        // Slice the one decompressed block into the tables, in directory order.
        var stored = new Dictionary<uint, byte[]>(entries.Count);
        var offset = 0;

        foreach (var entry in entries)
        {
            var length = (int)entry.StoredLength;
            if (length > decompressed.Length - offset)
            {
                throw new FontFormatException(
                    $"table {Sfnt.TagToString(entry.Tag)} runs past the end of the decompressed data");
            }

            if (!stored.TryAdd(entry.Tag, decompressed.AsSpan(offset, length).ToArray()))
            {
                throw new FontFormatException(
                    $"table {Sfnt.TagToString(entry.Tag)} appears twice in the directory");
            }

            offset += length;
        }

        var tables = new List<Sfnt.Table>(entries.Count);
        GlyfReconstruction? glyf = null;

        var glyfEntry = entries.FirstOrDefault(e => e.Tag == GlyfTag);
        if (glyfEntry is { Transformed: true })
        {
            glyf = ReconstructGlyf(stored[GlyfTag]);
        }

        foreach (var entry in entries)
        {
            byte[] bytes;

            if (entry.Tag == GlyfTag && glyf is not null)
            {
                bytes = glyf.Glyf;
            }
            else if (entry.Tag == LocaTag && glyf is not null)
            {
                // loca is never stored: its own entry's transformLength is zero
                // and the table is implied by the reconstructed glyph lengths.
                bytes = glyf.Loca;
            }
            else if (entry.Tag == HmtxTag && entry.Transformed)
            {
                bytes = ReconstructHmtx(stored[HmtxTag], stored, glyf);
            }
            else
            {
                bytes = stored[entry.Tag];

                if (entry.Transformed)
                {
                    throw new FontFormatException(
                        $"table {Sfnt.TagToString(entry.Tag)} uses transform version "
                        + $"{entry.TransformVersion}, which this decoder does not know");
                }

                if (bytes.Length != entry.OriginalLength)
                {
                    throw new FontFormatException(
                        $"table {Sfnt.TagToString(entry.Tag)} is {bytes.Length} bytes but the "
                        + $"directory says {entry.OriginalLength}");
                }
            }

            tables.Add(new Sfnt.Table(entry.Tag, bytes));
        }

        return Sfnt.Build(flavor, tables);
    }

    private sealed record GlyfReconstruction(byte[] Glyf, byte[] Loca, short[] XMin);

    /// <summary>
    /// Rebuilds glyf and loca from the seven transformed streams
    /// (Section 6.6.5).
    /// </summary>
    private static GlyfReconstruction ReconstructGlyf(byte[] transformed)
    {
        var header = new Woff2Reader(transformed, "transformed glyf header");

        _ = header.ReadUInt16();                    // reserved
        var optionFlags = header.ReadUInt16();
        var numGlyphs = header.ReadUInt16();
        var indexFormat = header.ReadUInt16();

        var nContourSize = header.ReadUInt32();
        var nPointsSize = header.ReadUInt32();
        var flagSize = header.ReadUInt32();
        var glyphSize = header.ReadUInt32();
        var compositeSize = header.ReadUInt32();
        var bboxSize = header.ReadUInt32();
        var instructionSize = header.ReadUInt32();

        var streams = transformed.AsSpan(header.Position);
        long needed = (long)nContourSize + nPointsSize + flagSize + glyphSize
                      + compositeSize + bboxSize + instructionSize;

        if (needed > streams.Length)
        {
            throw new FontFormatException(
                "the transformed glyf streams are longer than the table that holds them");
        }

        // The seven streams sit end to end in the order their sizes were
        // declared, so each one's start is the running total of the ones before.
        var nContourAt = 0;
        var nPointsAt = nContourAt + (int)nContourSize;
        var flagAt = nPointsAt + (int)nPointsSize;
        var glyphAt = flagAt + (int)flagSize;
        var compositeAt = glyphAt + (int)glyphSize;
        var bboxAt = compositeAt + (int)compositeSize;
        var instructionAt = bboxAt + (int)bboxSize;
        var afterStreams = instructionAt + (int)instructionSize;

        var nContourStream = new Woff2Reader(streams.Slice(nContourAt, (int)nContourSize), "nContour stream");
        var nPointsStream = new Woff2Reader(streams.Slice(nPointsAt, (int)nPointsSize), "nPoints stream");
        var flagStream = new Woff2Reader(streams.Slice(flagAt, (int)flagSize), "flag stream");
        var glyphStream = new Woff2Reader(streams.Slice(glyphAt, (int)glyphSize), "glyph stream");
        var compositeStream = new Woff2Reader(streams.Slice(compositeAt, (int)compositeSize), "composite stream");
        var bboxSpan = streams.Slice(bboxAt, (int)bboxSize);
        var instructionStream = new Woff2Reader(
            streams.Slice(instructionAt, (int)instructionSize), "instruction stream");

        // One bit per glyph, but padded up to a four-byte boundary rather than
        // a one-byte one. The difference is at most three bytes and it shifts
        // every bounding box that follows: with 2901 glyphs, ceil(n/8) gives
        // 363 and the right answer is 364, which turned every composite
        // glyph's box into two halves of its neighbours. Fonts with no stored
        // boxes read nothing after the bitmap and so cannot catch this.
        var bitmapBytes = ((numGlyphs + 31) / 32) * 4;

        if (bboxSpan.Length < bitmapBytes)
        {
            throw new FontFormatException("the bbox stream is too short to hold its bitmap");
        }

        var bboxBitmap = bboxSpan[..bitmapBytes];
        var bboxStream = new Woff2Reader(bboxSpan[bitmapBytes..], "bbox stream");

        // The overlap bitmap, unlike the bbox one, is padded only to a byte.
        var overlapBytes = (numGlyphs + 7) / 8;
        var overlapBitmap = ReadOnlySpan<byte>.Empty;
        if ((optionFlags & 0x0001) != 0)
        {
            if (streams.Length - afterStreams < overlapBytes)
            {
                throw new FontFormatException("the overlap bitmap is missing from the transformed glyf");
            }

            overlapBitmap = streams.Slice(afterStreams, overlapBytes);
        }

        var glyf = new List<byte>(numGlyphs * 64);
        var offsets = new uint[numGlyphs + 1];
        var xMins = new short[numGlyphs];

        for (var i = 0; i < numGlyphs; i++)
        {
            offsets[i] = (uint)glyf.Count;

            var numContours = nContourStream.ReadInt16();
            var haveBbox = (bboxBitmap[i >> 3] & (0x80 >> (i & 7))) != 0;

            if (numContours == 0)
            {
                // An empty glyph - a space, say - has no outline at all, and
                // loca records it by two equal offsets.
                if (haveBbox)
                {
                    throw new FontFormatException($"glyph {i} is empty but carries a bounding box");
                }

                continue;
            }

            byte[] glyph;

            if (numContours < 0)
            {
                if (!haveBbox)
                {
                    // The spec requires it: a composite glyph's bounds cannot be
                    // worked out without resolving its components.
                    throw new FontFormatException($"composite glyph {i} has no bounding box");
                }

                glyph = BuildComposite(ref compositeStream, ref glyphStream, ref instructionStream,
                    ref bboxStream, out var compositeXMin);
                xMins[i] = compositeXMin;
            }
            else
            {
                var overlap = overlapBitmap.Length > 0
                              && (overlapBitmap[i >> 3] & (0x80 >> (i & 7))) != 0;

                glyph = BuildSimple(numContours, ref nPointsStream, ref flagStream, ref glyphStream,
                    ref instructionStream, haveBbox, ref bboxStream, overlap, out var simpleXMin);
                xMins[i] = simpleXMin;
            }

            glyf.AddRange(glyph);

            // Each glyph starts on a four-byte boundary, which also keeps the
            // short loca format - which stores offset/2 - exact.
            while (glyf.Count % 4 != 0)
            {
                glyf.Add(0);
            }

            Limits.CheckDecodedFontSize(glyf.Count);
        }

        offsets[numGlyphs] = (uint)glyf.Count;

        return new GlyfReconstruction(glyf.ToArray(), BuildLoca(offsets, indexFormat), xMins);
    }

    private static byte[] BuildLoca(uint[] offsets, ushort indexFormat)
    {
        if (indexFormat == 0)
        {
            var loca = new byte[offsets.Length * 2];
            for (var i = 0; i < offsets.Length; i++)
            {
                if (offsets[i] % 2 != 0 || offsets[i] / 2 > ushort.MaxValue)
                {
                    throw new FontFormatException(
                        "the glyphs do not fit the short loca format the font asked for");
                }

                BinaryPrimitives.WriteUInt16BigEndian(loca.AsSpan(i * 2), (ushort)(offsets[i] / 2));
            }

            return loca;
        }

        var longLoca = new byte[offsets.Length * 4];
        for (var i = 0; i < offsets.Length; i++)
        {
            BinaryPrimitives.WriteUInt32BigEndian(longLoca.AsSpan(i * 4), offsets[i]);
        }

        return longLoca;
    }

    private static byte[] BuildSimple(
        short numContours,
        ref Woff2Reader nPointsStream,
        ref Woff2Reader flagStream,
        ref Woff2Reader glyphStream,
        ref Woff2Reader instructionStream,
        bool haveBbox,
        ref Woff2Reader bboxStream,
        bool overlap,
        out short xMin)
    {
        var endPoints = new ushort[numContours];
        var total = 0;

        for (var c = 0; c < numContours; c++)
        {
            total += nPointsStream.Read255UInt16();

            if (total > ushort.MaxValue)
            {
                throw new FontFormatException("a glyph with more than 65535 points");
            }

            endPoints[c] = (ushort)(total - 1);
        }

        var flags = flagStream.ReadBytes(total).ToArray();
        var points = DecodeTriplets(flags, ref glyphStream);

        var instructionLength = glyphStream.Read255UInt16();
        var instructions = instructionStream.ReadBytes(instructionLength);

        short minX, minY, maxX, maxY;
        if (haveBbox)
        {
            minX = bboxStream.ReadInt16();
            minY = bboxStream.ReadInt16();
            maxX = bboxStream.ReadInt16();
            maxY = bboxStream.ReadInt16();
        }
        else
        {
            // Not stored because it can be derived, which is the whole reason
            // the transform saves space on simple glyphs.
            (minX, minY, maxX, maxY) = Bounds(points);
        }

        xMin = minX;

        var glyph = new List<byte>(32 + (total * 5) + instructions.Length);

        AddInt16(glyph, numContours);
        AddInt16(glyph, minX);
        AddInt16(glyph, minY);
        AddInt16(glyph, maxX);
        AddInt16(glyph, maxY);

        foreach (var end in endPoints)
        {
            AddUInt16(glyph, end);
        }

        AddUInt16(glyph, instructionLength);
        glyph.AddRange(instructions);

        // The flags are written one per point, with no run-length compression,
        // and every coordinate in its 16-bit form. That is a few bytes larger
        // than a font designer's tool would write and exactly as correct; the
        // file this goes into is a cache nobody ships.
        const byte onCurvePoint = 0x01;
        const byte overlapSimple = 0x40;

        for (var i = 0; i < points.Count; i++)
        {
            var flag = points[i].OnCurve ? onCurvePoint : (byte)0;

            if (i == 0 && overlap)
            {
                flag |= overlapSimple;
            }

            glyph.Add(flag);
        }

        var previousX = 0;
        foreach (var point in points)
        {
            AddInt16(glyph, Delta(point.X, previousX));
            previousX = point.X;
        }

        var previousY = 0;
        foreach (var point in points)
        {
            AddInt16(glyph, Delta(point.Y, previousY));
            previousY = point.Y;
        }

        return glyph.ToArray();
    }

    private static byte[] BuildComposite(
        ref Woff2Reader compositeStream,
        ref Woff2Reader glyphStream,
        ref Woff2Reader instructionStream,
        ref Woff2Reader bboxStream,
        out short xMin)
    {
        const ushort argsAreWords = 0x0001;
        const ushort haveScale = 0x0008;
        const ushort moreComponents = 0x0020;
        const ushort haveXAndYScale = 0x0040;
        const ushort haveTwoByTwo = 0x0080;
        const ushort haveInstructions = 0x0100;

        var start = compositeStream.Position;
        ushort flags;

        do
        {
            flags = compositeStream.ReadUInt16();
            _ = compositeStream.ReadUInt16();                       // glyph index

            compositeStream.ReadBytes((flags & argsAreWords) != 0 ? 4 : 2);

            if ((flags & haveScale) != 0)
            {
                compositeStream.ReadBytes(2);
            }
            else if ((flags & haveXAndYScale) != 0)
            {
                compositeStream.ReadBytes(4);
            }
            else if ((flags & haveTwoByTwo) != 0)
            {
                compositeStream.ReadBytes(8);
            }
        }
        while ((flags & moreComponents) != 0);

        var length = compositeStream.Position - start;
        var components = compositeStream.Slice(start, length);

        var minX = bboxStream.ReadInt16();
        var minY = bboxStream.ReadInt16();
        var maxX = bboxStream.ReadInt16();
        var maxY = bboxStream.ReadInt16();

        xMin = minX;

        var glyph = new List<byte>(16 + length);

        AddInt16(glyph, -1);
        AddInt16(glyph, minX);
        AddInt16(glyph, minY);
        AddInt16(glyph, maxX);
        AddInt16(glyph, maxY);

        glyph.AddRange(components);

        if ((flags & haveInstructions) != 0)
        {
            var instructionLength = glyphStream.Read255UInt16();
            AddUInt16(glyph, instructionLength);
            glyph.AddRange(instructionStream.ReadBytes(instructionLength));
        }

        return glyph.ToArray();
    }

    private readonly record struct Point(int X, int Y, bool OnCurve);

    /// <summary>
    /// Decodes the point coordinates from the glyph stream, following the
    /// specification's triplet-encoding table.
    /// </summary>
    /// <remarks>
    /// One byte per point has already been read into <paramref name="flags"/>.
    /// The top bit says whether the point is off-curve; the low seven bits
    /// select both how many more bytes this point costs and how to read a
    /// signed x and y delta out of them. The five ranges below are the table,
    /// and the magic numbers are the table's, not a choice made here.
    /// </remarks>
    private static List<Point> DecodeTriplets(byte[] flags, ref Woff2Reader glyphStream)
    {
        var points = new List<Point>(flags.Length);
        var x = 0;
        var y = 0;

        foreach (var raw in flags)
        {
            var onCurve = (raw >> 7) == 0;
            var flag = raw & 0x7F;

            var byteCount = flag switch
            {
                < 84 => 1,
                < 120 => 2,
                < 124 => 3,
                _ => 4,
            };

            var data = glyphStream.ReadBytes(byteCount);

            int dx, dy;

            if (flag < 10)
            {
                dx = 0;
                dy = WithSign(flag, ((flag & 14) << 7) + data[0]);
            }
            else if (flag < 20)
            {
                dx = WithSign(flag, (((flag - 10) & 14) << 7) + data[0]);
                dy = 0;
            }
            else if (flag < 84)
            {
                var b0 = flag - 20;
                var b1 = data[0];
                dx = WithSign(flag, 1 + (b0 & 0x30) + (b1 >> 4));
                dy = WithSign(flag >> 1, 1 + ((b0 & 0x0C) << 2) + (b1 & 0x0F));
            }
            else if (flag < 120)
            {
                var b0 = flag - 84;
                dx = WithSign(flag, 1 + ((b0 / 12) << 8) + data[0]);
                dy = WithSign(flag >> 1, 1 + (((b0 % 12) >> 2) << 8) + data[1]);
            }
            else if (flag < 124)
            {
                var b2 = data[1];
                dx = WithSign(flag, (data[0] << 4) + (b2 >> 4));
                dy = WithSign(flag >> 1, ((b2 & 0x0F) << 8) + data[2]);
            }
            else
            {
                dx = WithSign(flag, (data[0] << 8) + data[1]);
                dy = WithSign(flag >> 1, (data[2] << 8) + data[3]);
            }

            x += dx;
            y += dy;

            if (x is < short.MinValue or > short.MaxValue || y is < short.MinValue or > short.MaxValue)
            {
                throw new FontFormatException("a glyph point lies outside the coordinate range");
            }

            points.Add(new Point(x, y, onCurve));
        }

        return points;

        // The low bit of the flag is the sign, for both x and y - y uses the
        // flag shifted right by one, which is why the calls above look uneven.
        static int WithSign(int flag, int value) => (flag & 1) != 0 ? value : -value;
    }

    private static (short MinX, short MinY, short MaxX, short MaxY) Bounds(List<Point> points)
    {
        if (points.Count == 0)
        {
            return (0, 0, 0, 0);
        }

        var minX = int.MaxValue;
        var minY = int.MaxValue;
        var maxX = int.MinValue;
        var maxY = int.MinValue;

        foreach (var point in points)
        {
            minX = Math.Min(minX, point.X);
            minY = Math.Min(minY, point.Y);
            maxX = Math.Max(maxX, point.X);
            maxY = Math.Max(maxY, point.Y);
        }

        return ((short)minX, (short)minY, (short)maxX, (short)maxY);
    }

    /// <summary>
    /// Puts back the hmtx arrays the transform left out (Section 6.6.5).
    /// </summary>
    /// <remarks>
    /// The transform's saving is that for most glyphs the left side bearing
    /// equals the glyph's xMin, so the array can be dropped and read back off
    /// glyf. That makes this the one table whose reconstruction depends on
    /// another table's reconstruction having already happened.
    /// </remarks>
    private static byte[] ReconstructHmtx(
        byte[] transformed,
        Dictionary<uint, byte[]> stored,
        GlyfReconstruction? glyf)
    {
        var reader = new Woff2Reader(transformed, "transformed hmtx");

        var flags = reader.ReadByte();
        var lsbAbsent = (flags & 0x01) != 0;
        var leftSideBearingAbsent = (flags & 0x02) != 0;

        if ((flags & 0xFC) != 0)
        {
            throw new FontFormatException($"the transformed hmtx sets unknown flags (0x{flags:X2})");
        }

        var numberOfHMetrics = ReadNumberOfHMetrics(stored);
        var numGlyphs = ReadNumGlyphs(stored);

        if (numberOfHMetrics > numGlyphs)
        {
            throw new FontFormatException(
                $"hhea says {numberOfHMetrics} metrics for {numGlyphs} glyphs");
        }

        if ((lsbAbsent || leftSideBearingAbsent) && glyf is null)
        {
            throw new FontFormatException(
                "the transformed hmtx leaves out side bearings, which only glyf can supply");
        }

        var advances = new ushort[numberOfHMetrics];
        for (var i = 0; i < numberOfHMetrics; i++)
        {
            advances[i] = reader.ReadUInt16();
        }

        var bearings = new short[numGlyphs];

        for (var i = 0; i < numberOfHMetrics; i++)
        {
            bearings[i] = lsbAbsent ? XMin(glyf, i, numGlyphs) : reader.ReadInt16();
        }

        for (var i = numberOfHMetrics; i < numGlyphs; i++)
        {
            bearings[i] = leftSideBearingAbsent ? XMin(glyf, i, numGlyphs) : reader.ReadInt16();
        }

        if (!reader.AtEnd)
        {
            throw new FontFormatException(
                $"the transformed hmtx has {reader.Remaining} bytes left over");
        }

        var hmtx = new byte[(numberOfHMetrics * 4) + ((numGlyphs - numberOfHMetrics) * 2)];
        var span = hmtx.AsSpan();

        for (var i = 0; i < numberOfHMetrics; i++)
        {
            BinaryPrimitives.WriteUInt16BigEndian(span[(i * 4)..], advances[i]);
            BinaryPrimitives.WriteInt16BigEndian(span[((i * 4) + 2)..], bearings[i]);
        }

        for (var i = numberOfHMetrics; i < numGlyphs; i++)
        {
            var at = (numberOfHMetrics * 4) + ((i - numberOfHMetrics) * 2);
            BinaryPrimitives.WriteInt16BigEndian(span[at..], bearings[i]);
        }

        return hmtx;
    }

    private static short XMin(GlyfReconstruction? glyf, int glyph, int numGlyphs)
    {
        if (glyf is null || glyf.XMin.Length != numGlyphs)
        {
            throw new FontFormatException(
                "the hmtx transform needs one xMin per glyph and glyf did not supply them");
        }

        return glyf.XMin[glyph];
    }

    private static int ReadNumberOfHMetrics(Dictionary<uint, byte[]> stored)
    {
        if (!stored.TryGetValue(HheaTag, out var hhea) || hhea.Length < 36)
        {
            throw new FontFormatException("the font has no usable hhea table");
        }

        return BinaryPrimitives.ReadUInt16BigEndian(hhea.AsSpan(34));
    }

    private static int ReadNumGlyphs(Dictionary<uint, byte[]> stored)
    {
        if (!stored.TryGetValue(MaxpTag, out var maxp) || maxp.Length < 6)
        {
            throw new FontFormatException("the font has no usable maxp table");
        }

        return BinaryPrimitives.ReadUInt16BigEndian(maxp.AsSpan(4));
    }

    /// <summary>
    /// A coordinate delta as TrueType stores it. A font whose points jump by
    /// more than 32767 cannot be written in this form at all, so it is a
    /// malformed font rather than an arithmetic accident.
    /// </summary>
    private static short Delta(int value, int previous)
    {
        var delta = value - previous;

        if (delta is < short.MinValue or > short.MaxValue)
        {
            throw new FontFormatException($"a coordinate step of {delta} is too large for a glyph");
        }

        return (short)delta;
    }

    private static void AddInt16(List<byte> target, short value)
    {
        target.Add((byte)(value >> 8));
        target.Add((byte)value);
    }

    private static void AddUInt16(List<byte> target, ushort value)
    {
        target.Add((byte)(value >> 8));
        target.Add((byte)value);
    }
}
