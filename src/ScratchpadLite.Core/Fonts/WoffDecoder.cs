using System.Buffers.Binary;
using System.IO.Compression;
using ScratchpadLite.Core.Formats;

namespace ScratchpadLite.Core.Fonts;

/// <summary>
/// WOFF 1.0 to sfnt (Section 6.6.5). Each table is stored zlib-deflated,
/// so built-in ZLibStream inflates it and SfntBuilder reassembles the
/// tables. Every offset and length is bounds-checked and the decoded size
/// is capped (Section 7.5).
/// </summary>
/// <remarks>
/// WOFF 1.0 is a repackaging rather than a real transformation: the tables are
/// the font's own tables, each one deflated on its own, so decoding is inflate
/// and reassemble. That is the whole format, and it is why this decoder is 80
/// lines and <see cref="Woff2Decoder"/> is not.
/// </remarks>
public static class WoffDecoder
{
    /// <summary>"wOFF".</summary>
    internal const uint Signature = 0x774F4646;

    private const int HeaderSize = 44;
    private const int DirectoryEntrySize = 20;

    public static byte[] ToSfnt(Stream woff)
    {
        ArgumentNullException.ThrowIfNull(woff);

        var data = ReadAll(woff);

        if (data.Length < HeaderSize)
        {
            throw new FontFormatException("the file is too short to be a WOFF font");
        }

        var header = data.AsSpan();
        if (BinaryPrimitives.ReadUInt32BigEndian(header) != Signature)
        {
            throw new FontFormatException("this is not a WOFF 1.0 file");
        }

        var flavor = BinaryPrimitives.ReadUInt32BigEndian(header[4..]);
        var numTables = BinaryPrimitives.ReadUInt16BigEndian(header[12..]);
        var totalSfntSize = BinaryPrimitives.ReadUInt32BigEndian(header[16..]);

        if (numTables == 0)
        {
            throw new FontFormatException("the WOFF file declares no tables");
        }

        Limits.CheckDecodedFontSize(totalSfntSize);

        var directoryEnd = HeaderSize + (numTables * DirectoryEntrySize);
        if (data.Length < directoryEnd)
        {
            throw new FontFormatException(
                $"the WOFF directory claims {numTables} tables but the file is too short");
        }

        var tables = new List<Sfnt.Table>(numTables);
        long decoded = 0;

        for (var i = 0; i < numTables; i++)
        {
            var entry = header[(HeaderSize + (i * DirectoryEntrySize))..];

            var tag = BinaryPrimitives.ReadUInt32BigEndian(entry);
            var offset = BinaryPrimitives.ReadUInt32BigEndian(entry[4..]);
            var compressedLength = BinaryPrimitives.ReadUInt32BigEndian(entry[8..]);
            var originalLength = BinaryPrimitives.ReadUInt32BigEndian(entry[12..]);

            if (offset > (uint)data.Length || compressedLength > (uint)data.Length - offset)
            {
                throw new FontFormatException(
                    $"table {Sfnt.TagToString(tag)} points outside the WOFF file");
            }

            decoded += originalLength;
            Limits.CheckDecodedFontSize(decoded);

            var stored = data.AsSpan((int)offset, (int)compressedLength);

            // The spec's rule, and it is this way round: compLength equal to
            // origLength means the table was stored as is, because deflating it
            // did not help. Anything else is deflated.
            var bytes = compressedLength >= originalLength
                ? stored[..(int)originalLength].ToArray()
                : Inflate(stored, originalLength, tag);

            tables.Add(new Sfnt.Table(tag, bytes));
        }

        return Sfnt.Build(flavor, tables);
    }

    private static byte[] Inflate(ReadOnlySpan<byte> compressed, uint expected, uint tag)
    {
        var output = new byte[expected];

        try
        {
            using var source = new MemoryStream(compressed.ToArray(), writable: false);
            using var inflate = new ZLibStream(source, CompressionMode.Decompress);

            inflate.ReadExactly(output);

            // A table that inflates to more than it promised means the
            // directory is lying, and the extra bytes would be silently lost.
            if (inflate.ReadByte() != -1)
            {
                throw new FontFormatException(
                    $"table {Sfnt.TagToString(tag)} inflates to more than its stated {expected} bytes");
            }
        }
        catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException)
        {
            throw new FontFormatException(
                $"table {Sfnt.TagToString(tag)} is not valid zlib data", ex);
        }

        return output;
    }

    private static byte[] ReadAll(Stream stream)
    {
        if (stream is MemoryStream memory && memory.TryGetBuffer(out var buffer))
        {
            return buffer.AsSpan(buffer.Offset, buffer.Count).ToArray();
        }

        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }
}
