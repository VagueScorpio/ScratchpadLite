using System.Buffers.Binary;
using ScratchpadLite.Core.Formats;

namespace ScratchpadLite.Core.Fonts;

/// <summary>
/// Mac font containers to sfnt (Section 6.6.3): .dfont, AppleDouble files
/// that start with an underscore and a dot, and data-fork suitcases. The
/// sfnt resources are read out of the resource map. Files that lost their
/// resource fork on the way to Windows carry no font data and are reported
/// as such (Section 13).
/// </summary>
/// <remarks>
/// All three are the same thing wrapped differently: a classic Mac resource
/// fork, holding <c>sfnt</c> resources (TrueType or OpenType, ready to use) or
/// <c>POST</c> resources (Type 1, in pieces that have to be joined).
/// <list type="bullet">
///   <item>
///     <b>.dfont</b> - the resource fork stored as the file's own data, which is
///     what "data fork suitcase" means. Read from byte zero.
///   </item>
///   <item>
///     <b>._Name</b> - an AppleDouble file, which is how a Mac resource fork
///     survives being copied to a non-Mac disk. A small header lists entries;
///     entry ID 2 is the resource fork, and the resource map is inside it.
///   </item>
///   <item>
///     <b>Extensionless suitcases</b> - either of the above, decided by looking.
///   </item>
/// </list>
/// <para>
/// The common failure is a file that is empty or a few hundred bytes: copying a
/// Mac font to Windows through a filesystem that has no resource forks keeps the
/// data fork, which for a classic suitcase holds nothing. That is reported as
/// what it is, because there is nothing to recover and the user needs to know to
/// look for a .ttf instead.
/// </para>
/// </remarks>
public static class MacFontExtractor
{
    /// <summary>What came out of a container.</summary>
    public sealed record Extracted(IReadOnlyList<byte[]> Sfnts, IReadOnlyList<byte[]> Type1Pfb);

    internal const string NoResourceFork =
        "This Mac font lost its data when it was copied to Windows. "
        + "Look for a .ttf or .otf version.";

    public static IReadOnlyList<byte[]> ToSfnt(Stream container) => Read(container).Sfnts;

    /// <summary>
    /// Reads a container. <see cref="Extracted.Type1Pfb"/> holds the joined
    /// POST resources, which are Type 1 data for
    /// <see cref="Type1.Type1Converter"/> rather than fonts in their own right.
    /// </summary>
    public static Extracted Read(Stream container)
    {
        ArgumentNullException.ThrowIfNull(container);

        using var buffer = new MemoryStream();
        container.CopyTo(buffer);
        var data = buffer.GetBuffer().AsSpan(0, (int)buffer.Length);

        if (data.Length < 16)
        {
            throw new FontFormatException(NoResourceFork);
        }

        var start = ResourceForkStart(data);

        return ReadResourceFork(data, start);
    }

    /// <summary>
    /// Finds where the resource fork begins: zero for a .dfont, or the offset
    /// of entry ID 2 for an AppleDouble file.
    /// </summary>
    private static int ResourceForkStart(ReadOnlySpan<byte> data)
    {
        var magic = BinaryPrimitives.ReadUInt32BigEndian(data);

        // AppleSingle is 0x00051600, AppleDouble 0x00051607. Both carry an
        // entry list in the same shape.
        if (magic is not (0x00051600 or 0x00051607))
        {
            return 0;
        }

        if (data.Length < 26)
        {
            throw new FontFormatException(NoResourceFork);
        }

        var entries = BinaryPrimitives.ReadUInt16BigEndian(data[24..]);

        if (data.Length < 26 + (entries * 12))
        {
            throw new FontFormatException("the AppleDouble entry list runs past the end of the file");
        }

        for (var i = 0; i < entries; i++)
        {
            var entry = data[(26 + (i * 12))..];
            var id = BinaryPrimitives.ReadUInt32BigEndian(entry);
            var offset = BinaryPrimitives.ReadUInt32BigEndian(entry[4..]);
            var length = BinaryPrimitives.ReadUInt32BigEndian(entry[8..]);

            // 2 is the resource fork. 1 is the data fork, which for a font
            // suitcase holds nothing.
            if (id != 2)
            {
                continue;
            }

            if (offset > (uint)data.Length || length > (uint)data.Length - offset)
            {
                throw new FontFormatException("the AppleDouble resource fork runs past the end of the file");
            }

            if (length < 16)
            {
                throw new FontFormatException(NoResourceFork);
            }

            return (int)offset;
        }

        throw new FontFormatException(NoResourceFork);
    }

    /// <summary>
    /// Walks a classic resource map and pulls out the sfnt and POST resources.
    /// </summary>
    private static Extracted ReadResourceFork(ReadOnlySpan<byte> data, int start)
    {
        var fork = data[start..];

        if (fork.Length < 16)
        {
            throw new FontFormatException(NoResourceFork);
        }

        var dataOffset = (int)BinaryPrimitives.ReadUInt32BigEndian(fork);
        var mapOffset = (int)BinaryPrimitives.ReadUInt32BigEndian(fork[4..]);
        var mapLength = (int)BinaryPrimitives.ReadUInt32BigEndian(fork[12..]);

        if (mapOffset < 0 || mapLength < 30 || mapOffset > fork.Length - mapLength)
        {
            throw new FontFormatException(NoResourceFork);
        }

        var map = fork.Slice(mapOffset, mapLength);
        var typeListOffset = BinaryPrimitives.ReadUInt16BigEndian(map[24..]);
        var nameListOffset = BinaryPrimitives.ReadUInt16BigEndian(map[26..]);
        _ = nameListOffset;

        if (typeListOffset > map.Length - 2)
        {
            throw new FontFormatException("the resource map's type list is outside the map");
        }

        // Stored as "one less than the number of types", which is a classic
        // way to lose a resource.
        var typeCount = BinaryPrimitives.ReadUInt16BigEndian(map[typeListOffset..]) + 1;

        var sfnts = new List<byte[]>();
        var postPieces = new List<(ushort Id, byte[] Data)>();

        for (var i = 0; i < typeCount; i++)
        {
            var entryAt = typeListOffset + 2 + (i * 8);

            if (entryAt > map.Length - 8)
            {
                break;
            }

            var entry = map[entryAt..];
            var type = BinaryPrimitives.ReadUInt32BigEndian(entry);
            var count = BinaryPrimitives.ReadUInt16BigEndian(entry[4..]) + 1;
            var referenceListOffset = BinaryPrimitives.ReadUInt16BigEndian(entry[6..]);

            var isSfnt = type == Sfnt.StringToTag("sfnt");
            var isPost = type == Sfnt.StringToTag("POST");

            if (!isSfnt && !isPost)
            {
                continue;
            }

            for (var r = 0; r < count; r++)
            {
                var referenceAt = typeListOffset + referenceListOffset + (r * 12);

                if (referenceAt < 0 || referenceAt > map.Length - 12)
                {
                    break;
                }

                var reference = map[referenceAt..];
                var id = BinaryPrimitives.ReadUInt16BigEndian(reference);

                // A 24-bit offset, with the high byte of the next word being
                // resource attributes.
                var resourceOffset =
                    (BinaryPrimitives.ReadUInt32BigEndian(reference[4..]) & 0x00FFFFFF);

                var at = dataOffset + (int)resourceOffset;

                if (at < 0 || at > fork.Length - 4)
                {
                    continue;
                }

                var length = (int)BinaryPrimitives.ReadUInt32BigEndian(fork[at..]);

                if (length <= 0 || length > fork.Length - at - 4)
                {
                    continue;
                }

                Limits.CheckDecodedFontSize(length);

                var bytes = fork.Slice(at + 4, length).ToArray();

                if (isSfnt)
                {
                    sfnts.Add(bytes);
                }
                else
                {
                    postPieces.Add((id, bytes));
                }
            }
        }

        var rebuilt = new List<byte[]>(sfnts.Count);

        foreach (var sfnt in sfnts)
        {
            try
            {
                var (flavor, tables) = Sfnt.ReadTablesAt(sfnt, 0);
                rebuilt.Add(Sfnt.Build(flavor, tables));
            }
            catch (FontFormatException)
            {
                // One bad resource in a suitcase of several should not lose the
                // others.
            }
        }

        var pfb = JoinPostResources(postPieces);

        if (rebuilt.Count == 0 && pfb.Count == 0)
        {
            throw new FontFormatException(
                sfnts.Count > 0
                    ? "the font resources in this Mac container could not be read"
                    : "this Mac container holds no font resources");
        }

        return new Extracted(rebuilt, pfb);
    }

    /// <summary>
    /// Joins POST resources into PFB data (Section 6.6.3).
    /// </summary>
    /// <remarks>
    /// A Mac Type 1 font is cut into POST resources numbered from 501. Each
    /// starts with a one-byte type - 1 for ASCII, 2 for binary, 5 for the end -
    /// followed by a pad byte, and the rest is a piece of what would be a PFB
    /// segment. Joining them in ID order and wrapping the result in PFB segment
    /// headers gives something <see cref="Type1.Type1Converter"/> can read, so
    /// the Type 1 path is shared rather than duplicated.
    /// </remarks>
    private static List<byte[]> JoinPostResources(List<(ushort Id, byte[] Data)> pieces)
    {
        if (pieces.Count == 0)
        {
            return [];
        }

        var ascii = new MemoryStream();
        var binary = new MemoryStream();

        foreach (var (_, data) in pieces.OrderBy(p => p.Id))
        {
            if (data.Length < 2)
            {
                continue;
            }

            var type = data[0];
            var body = data.AsSpan(2);

            switch (type)
            {
                case 1:
                    ascii.Write(body);
                    break;
                case 2:
                    binary.Write(body);
                    break;
                case 5:
                    break;        // end marker
            }
        }

        if (ascii.Length == 0 && binary.Length == 0)
        {
            return [];
        }

        var pfb = new MemoryStream();
        WriteSegment(pfb, 1, ascii);
        WriteSegment(pfb, 2, binary);

        // The 512 zeros and "cleartomark" that end a Type 1 program, as an
        // ASCII segment, then the end marker.
        var trailer = new MemoryStream();
        using (var writer = new StreamWriter(trailer, System.Text.Encoding.Latin1, leaveOpen: true))
        {
            for (var line = 0; line < 8; line++)
            {
                writer.Write(new string('0', 64));
                writer.Write('\n');
            }

            writer.Write("cleartomark\n");
        }

        WriteSegment(pfb, 1, trailer);

        pfb.WriteByte(0x80);
        pfb.WriteByte(0x03);

        return [pfb.ToArray()];
    }

    private static void WriteSegment(Stream pfb, byte type, MemoryStream body)
    {
        if (body.Length == 0)
        {
            return;
        }

        Span<byte> header = stackalloc byte[6];
        header[0] = 0x80;
        header[1] = type;

        // Segment lengths in a PFB are little-endian, unlike everything else in
        // a font file.
        BinaryPrimitives.WriteUInt32LittleEndian(header[2..], (uint)body.Length);

        pfb.Write(header);
        pfb.Write(body.GetBuffer(), 0, (int)body.Length);
    }
}
