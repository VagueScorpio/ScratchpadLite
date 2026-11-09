using System.Buffers.Binary;
using System.Text;
using ScratchpadLite.Core.Fonts;
using Xunit;

namespace ScratchpadLite.Core.Tests;

/// <summary>
/// Type 42 and the Mac containers (Section 6.6.3).
/// </summary>
/// <remarks>
/// No <c>.t42</c>, <c>.dfont</c> or AppleDouble file was found anywhere on this
/// machine, so these tests build their fixtures from a Windows font. That is
/// weaker than testing against something a real tool produced - it proves the
/// reader understands the format as written here - so both formats are also
/// listed in the Phase 3 report as untested against real-world files.
/// </remarks>
public class Type42ExtractorTests
{
    [Theory]
    [InlineData(2048, false)]
    [InlineData(1000, false)]
    [InlineData(65534, false)]
    [InlineData(1001, true)]
    public void AType42WrapperGivesBackTheFontInside(int chunkSize, bool padChunks)
    {
        var source = FontFixtures.AnyWindowsFont();
        Assert.NotNull(source);

        var original = File.ReadAllBytes(source!);
        var t42 = BuildType42(original, chunkSize, padChunks);

        using var input = new MemoryStream(t42);
        var recovered = Type42Extractor.ToSfnt(input);

        // Both the padded and unpadded conventions have to work, because the
        // format gives a reader no way to tell them apart.
        var before = FontInspector.Read(original);
        var after = FontInspector.Read(recovered);

        Assert.Equal(before.Families, after.Families);
        Assert.Equal(before.GlyphCount, after.GlyphCount);
    }

    [Fact]
    public void APostScriptFileWithNoSfntsArrayIsRefusedWithAReason()
    {
        var program = Encoding.ASCII.GetBytes("%!PS-TrueTypeFont\n/FontName /Nothing def\n");

        using var input = new MemoryStream(program);
        var ex = Assert.Throws<FontFormatException>(() => Type42Extractor.ToSfnt(input));

        Assert.Contains("/sfnts", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnSfntsArrayHoldingRubbishIsRefusedRatherThanProducingAFont()
    {
        var program = Encoding.ASCII.GetBytes(
            "%!PS-TrueTypeFont\n/sfnts [<deadbeefdeadbeefdeadbeef>] def\n");

        using var input = new MemoryStream(program);
        Assert.Throws<FontFormatException>(() => Type42Extractor.ToSfnt(input));
    }

    /// <summary>
    /// Wraps an sfnt the way a PostScript driver does: hex strings in an
    /// <c>/sfnts</c> array.
    /// </summary>
    private static byte[] BuildType42(byte[] sfnt, int chunkSize, bool padChunks)
    {
        var text = new StringBuilder();

        text.Append("%!PS-TrueTypeFont-1.0\n");
        text.Append("/FontName /TestFont def\n");
        text.Append("/sfnts [\n");

        for (var at = 0; at < sfnt.Length; at += chunkSize)
        {
            var length = Math.Min(chunkSize, sfnt.Length - at);
            var chunk = sfnt.AsSpan(at, length).ToArray();

            // The specification's rule: each string is padded to an even
            // length. So a generator using even chunk boundaries never adds a
            // pad byte at all, and only an odd chunk length produces one -
            // which is why the padded case here uses an odd size.
            if (padChunks && at + length < sfnt.Length && chunk.Length % 2 != 0)
            {
                chunk = [.. chunk, 0];
            }

            text.Append('<').Append(Convert.ToHexString(chunk)).Append(">\n");
        }

        text.Append("] def\n");
        text.Append("/CharStrings 1 dict dup begin end readonly def\n");

        return Encoding.ASCII.GetBytes(text.ToString());
    }
}

public class MacFontExtractorTests
{
    [Fact]
    public void ADfontGivesBackTheFontsInside()
    {
        var source = FontFixtures.AnyWindowsFont();
        Assert.NotNull(source);

        var original = File.ReadAllBytes(source!);
        var dfont = BuildResourceFork([original]);

        using var input = new MemoryStream(dfont);
        var recovered = MacFontExtractor.ToSfnt(input);

        Assert.Single(recovered);

        var before = FontInspector.Read(original);
        var after = FontInspector.Read(recovered[0]);

        Assert.Equal(before.Families, after.Families);
        Assert.Equal(before.GlyphCount, after.GlyphCount);
    }

    [Fact]
    public void ASuitcaseHoldingSeveralFontsGivesBackAllOfThem()
    {
        var fonts = FontFixtures.WindowsFonts().Take(3).Select(File.ReadAllBytes).ToList();
        Assert.True(fonts.Count >= 2, "this test needs at least two Windows fonts");

        using var input = new MemoryStream(BuildResourceFork(fonts));
        var recovered = MacFontExtractor.ToSfnt(input);

        Assert.Equal(fonts.Count, recovered.Count);

        // Each one comes back whole, not just the first.
        foreach (var (expected, actual) in fonts.Zip(recovered))
        {
            Assert.Equal(
                FontInspector.Read(expected).GlyphCount,
                FontInspector.Read(actual).GlyphCount);
        }
    }

    [Fact]
    public void AnAppleDoubleFileGivesBackTheFontInItsResourceFork()
    {
        var source = FontFixtures.AnyWindowsFont();
        Assert.NotNull(source);

        var original = File.ReadAllBytes(source!);

        using var input = new MemoryStream(BuildAppleDouble(BuildResourceFork([original])));
        var recovered = MacFontExtractor.ToSfnt(input);

        Assert.Single(recovered);
        Assert.Equal(
            FontInspector.Read(original).GlyphCount,
            FontInspector.Read(recovered[0]).GlyphCount);
    }

    [Fact]
    public void AMacFontThatLostItsResourceForkSaysSoRatherThanFailingVaguely()
    {
        // The common case: copying a classic Mac suitcase to Windows keeps the
        // data fork, which for a suitcase holds nothing. The user needs to know
        // to look for a .ttf, which a generic error would not tell them.
        using var empty = new MemoryStream(new byte[64]);

        var ex = Assert.Throws<FontFormatException>(() => MacFontExtractor.ToSfnt(empty));

        Assert.Equal(MacFontExtractorMessages.NoResourceFork, ex.Message);
    }

    [Fact]
    public void AnAppleDoubleFileWithNoResourceForkEntryIsRefused()
    {
        var header = new byte[26];
        BinaryPrimitives.WriteUInt32BigEndian(header, 0x00051607);
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(24), 0);

        using var input = new MemoryStream(header);
        Assert.Throws<FontFormatException>(() => MacFontExtractor.ToSfnt(input));
    }

    /// <summary>
    /// Builds a classic Mac resource fork holding one <c>sfnt</c> resource per
    /// font.
    /// </summary>
    private static byte[] BuildResourceFork(IReadOnlyList<byte[]> fonts)
    {
        using var data = new MemoryStream();
        var offsets = new List<uint>(fonts.Count);

        var length = new byte[4];

        foreach (var font in fonts)
        {
            offsets.Add((uint)data.Length);

            BinaryPrimitives.WriteUInt32BigEndian(length, (uint)font.Length);
            data.Write(length);
            data.Write(font);
        }

        // Header, then the data, then the map.
        const int headerSize = 256;
        var dataOffset = headerSize;
        var mapOffset = headerSize + (int)data.Length;

        // Map: 22 reserved bytes, attributes, type-list offset, name-list
        // offset, then the type list.
        var typeListOffset = 28;
        var referenceListOffset = typeListOffset + 2 + 8;
        var mapLength = referenceListOffset + (fonts.Count * 12);

        var file = new byte[mapOffset + mapLength];
        var span = file.AsSpan();

        BinaryPrimitives.WriteUInt32BigEndian(span, (uint)dataOffset);
        BinaryPrimitives.WriteUInt32BigEndian(span[4..], (uint)mapOffset);
        BinaryPrimitives.WriteUInt32BigEndian(span[8..], (uint)data.Length);
        BinaryPrimitives.WriteUInt32BigEndian(span[12..], (uint)mapLength);

        data.GetBuffer().AsSpan(0, (int)data.Length).CopyTo(span[dataOffset..]);

        var map = span[mapOffset..];
        BinaryPrimitives.WriteUInt16BigEndian(map[24..], (ushort)typeListOffset);
        BinaryPrimitives.WriteUInt16BigEndian(map[26..], (ushort)mapLength);

        // One less than the number of types, which is how the format counts.
        BinaryPrimitives.WriteUInt16BigEndian(map[typeListOffset..], 0);

        var typeEntry = map[(typeListOffset + 2)..];
        BinaryPrimitives.WriteUInt32BigEndian(typeEntry, Sfnt.StringToTag("sfnt"));
        BinaryPrimitives.WriteUInt16BigEndian(typeEntry[4..], (ushort)(fonts.Count - 1));
        BinaryPrimitives.WriteUInt16BigEndian(typeEntry[6..], (ushort)(referenceListOffset - typeListOffset));

        for (var i = 0; i < fonts.Count; i++)
        {
            var reference = map[(referenceListOffset + (i * 12))..];

            BinaryPrimitives.WriteUInt16BigEndian(reference, (ushort)(256 + i));     // resource id
            BinaryPrimitives.WriteInt16BigEndian(reference[2..], -1);                // name offset
            BinaryPrimitives.WriteUInt32BigEndian(reference[4..], offsets[i]);       // attrs + offset
        }

        return file;
    }

    /// <summary>Wraps a resource fork in an AppleDouble header.</summary>
    private static byte[] BuildAppleDouble(byte[] resourceFork)
    {
        const int headerSize = 26 + 12;

        var file = new byte[headerSize + resourceFork.Length];
        var span = file.AsSpan();

        BinaryPrimitives.WriteUInt32BigEndian(span, 0x00051607);        // AppleDouble
        BinaryPrimitives.WriteUInt32BigEndian(span[4..], 0x00020000);   // version
        BinaryPrimitives.WriteUInt16BigEndian(span[24..], 1);           // one entry

        var entry = span[26..];
        BinaryPrimitives.WriteUInt32BigEndian(entry, 2);                        // resource fork
        BinaryPrimitives.WriteUInt32BigEndian(entry[4..], headerSize);
        BinaryPrimitives.WriteUInt32BigEndian(entry[8..], (uint)resourceFork.Length);

        resourceFork.CopyTo(file, headerSize);

        return file;
    }
}

/// <summary>The messages the Mac tests assert on, so they stay in one place.</summary>
internal static class MacFontExtractorMessages
{
    internal const string NoResourceFork =
        "This Mac font lost its data when it was copied to Windows. "
        + "Look for a .ttf or .otf version.";
}
