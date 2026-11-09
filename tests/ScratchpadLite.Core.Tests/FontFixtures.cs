using System.Buffers.Binary;
using System.IO.Compression;
using ScratchpadLite.Core.Fonts;

namespace ScratchpadLite.Core.Tests;

/// <summary>
/// Where the font tests get real fonts from, and the encoders they need to
/// make test input.
/// </summary>
/// <remarks>
/// No font binaries are committed: they are large, and a Windows font is not
/// ours to redistribute (Section 6.6.6). So the tests take two sources.
/// <list type="bullet">
///   <item>
///     <b>Windows' own fonts</b>, which are on every machine this app runs on
///     and on the GitHub Windows runner. They are read, never copied into the
///     repository.
///   </item>
///   <item>
///     <b>tests/Fixtures/Fonts</b>, which is in .gitignore. Anything dropped
///     in there is tested as well; when it is empty, those tests skip rather
///     than fail, so a clean clone and CI stay green.
///   </item>
/// </list>
/// </remarks>
internal static class FontFixtures
{
    private static readonly string WindowsFontDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts");

    /// <summary>
    /// A few Windows fonts chosen to be different from each other: a
    /// workhorse TrueType, one with CFF outlines, a monospace, and a symbol
    /// font with no Latin letters.
    /// </summary>
    internal static IEnumerable<string> WindowsFonts() =>
        new[] { "arial.ttf", "segoeui.ttf", "consola.ttf", "times.ttf", "wingding.ttf" }
            .Select(name => Path.Combine(WindowsFontDir, name))
            .Where(File.Exists);

    /// <summary>The first Windows font available, for a test that needs only one.</summary>
    internal static string? AnyWindowsFont() => WindowsFonts().FirstOrDefault();

    internal static IReadOnlyList<string> Dropped(string extension)
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Fonts");

        return Directory.Exists(dir)
            ? Directory.GetFiles(dir, $"*{extension}", SearchOption.AllDirectories)
            : [];
    }

    /// <summary>
    /// Packs an sfnt into a WOFF 1.0 file, so the decoder can be tested
    /// against a known original rather than against another of my own
    /// readers.
    /// </summary>
    internal static byte[] ToWoff(byte[] sfnt)
    {
        var (flavor, tables) = Sfnt.ReadTablesAt(sfnt, 0);
        var sorted = tables.OrderBy(t => t.Tag).ToList();

        var directorySize = 44 + (sorted.Count * 20);
        var body = new MemoryStream();
        var records = new List<(uint Tag, uint Offset, uint CompLength, uint OrigLength)>();

        foreach (var table in sorted)
        {
            var deflated = Deflate(table.Data);

            // The spec's rule: store the table as is when deflating it does
            // not help. Both branches have to be produced here, because both
            // have to be decoded.
            var useCompressed = deflated.Length < table.Data.Length;
            var bytes = useCompressed ? deflated : table.Data;

            var offset = (uint)(directorySize + body.Length);
            records.Add((table.Tag, offset, (uint)bytes.Length, (uint)table.Data.Length));

            body.Write(bytes);
            while (body.Length % 4 != 0)
            {
                body.WriteByte(0);
            }
        }

        var sfntSize = 12 + (sorted.Count * 16) + sorted.Sum(t => (t.Data.Length + 3) & ~3);
        var file = new byte[directorySize + body.Length];
        var span = file.AsSpan();

        BinaryPrimitives.WriteUInt32BigEndian(span, 0x774F4646);              // wOFF
        BinaryPrimitives.WriteUInt32BigEndian(span[4..], flavor);
        BinaryPrimitives.WriteUInt32BigEndian(span[8..], (uint)file.Length);
        BinaryPrimitives.WriteUInt16BigEndian(span[12..], (ushort)sorted.Count);
        BinaryPrimitives.WriteUInt32BigEndian(span[16..], (uint)sfntSize);

        for (var i = 0; i < records.Count; i++)
        {
            var record = span[(44 + (i * 20))..];
            var (tag, offset, compLength, origLength) = records[i];

            BinaryPrimitives.WriteUInt32BigEndian(record, tag);
            BinaryPrimitives.WriteUInt32BigEndian(record[4..], offset);
            BinaryPrimitives.WriteUInt32BigEndian(record[8..], compLength);
            BinaryPrimitives.WriteUInt32BigEndian(record[12..], origLength);
            BinaryPrimitives.WriteUInt32BigEndian(record[16..], Checksum(sorted[i].Data));
        }

        body.GetBuffer().AsSpan(0, (int)body.Length).CopyTo(span[directorySize..]);

        return file;
    }

    /// <summary>
    /// Rewrites a PFB as a PFA: the clear-text segments as they are, the
    /// binary segment as hex.
    /// </summary>
    /// <remarks>
    /// Built here because no .pfa file was found on this machine, and the PFA
    /// path through the parser needs testing too. The two forms hold the same
    /// font, so the parser has to produce identical charstrings from both.
    /// </remarks>
    internal static byte[] ToPfa(byte[] pfb)
    {
        var pfa = new MemoryStream();
        var at = 0;

        while (at + 6 <= pfb.Length && pfb[at] == 0x80)
        {
            var type = pfb[at + 1];

            if (type == 0x03)
            {
                break;
            }

            var length = (int)BinaryPrimitives.ReadUInt32LittleEndian(pfb.AsSpan(at + 2));
            at += 6;

            if (length > pfb.Length - at)
            {
                break;
            }

            if (type == 0x01)
            {
                pfa.Write(pfb, at, length);
            }
            else
            {
                // 64 hex characters per line, which is what real PFA files use.
                var hex = Convert.ToHexString(pfb.AsSpan(at, length));

                for (var i = 0; i < hex.Length; i += 64)
                {
                    var line = hex.AsSpan(i, Math.Min(64, hex.Length - i));
                    pfa.Write(System.Text.Encoding.ASCII.GetBytes(line.ToString()));
                    pfa.WriteByte((byte)'\n');
                }
            }

            at += length;
        }

        return pfa.ToArray();
    }

    private static byte[] Deflate(byte[] data)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            zlib.Write(data);
        }

        return output.ToArray();
    }

    private static uint Checksum(ReadOnlySpan<byte> data)
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
}
