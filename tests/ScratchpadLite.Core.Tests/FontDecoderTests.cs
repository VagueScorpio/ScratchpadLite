using System.Buffers.Binary;
using ScratchpadLite.Core.Fonts;
using ScratchpadLite.Core.Formats;
using Xunit;

namespace ScratchpadLite.Core.Tests;

/// <summary>
/// The web font decoders (Section 6.6.5).
/// </summary>
/// <remarks>
/// WOFF 1.0 is tested by round trip: a Windows font is packed into a WOFF by
/// the test's own encoder and the decoder has to give back tables identical to
/// the original's, byte for byte. That is a real oracle, because the original
/// is not something this project produced.
/// <para>
/// WOFF2 cannot be tested that way - writing an encoder here would only check
/// my decoder against my encoder, and a mirrored misreading of the
/// specification would pass. So WOFF2 is tested against files produced by
/// Google's encoder, dropped into tests/Fixtures/Fonts, and the assertions
/// are the internal consistency a correct decode must have: loca agreeing with
/// glyf, every glyph inside the font's declared bounding box, and the glyph
/// count matching maxp. Coordinates decoded with the triplet table misread
/// would fail the bounding-box check immediately.
/// </para>
/// </remarks>
public class WoffDecoderTests
{
    [Fact]
    public void AWoffRoundTripsBackToTheSameTables()
    {
        var path = FontFixtures.AnyWindowsFont();
        Assert.NotNull(path);

        var original = File.ReadAllBytes(path!);
        var woff = FontFixtures.ToWoff(original);

        using var input = new MemoryStream(woff);
        var decoded = WoffDecoder.ToSfnt(input);

        var before = Sfnt.ReadTables(original).OrderBy(t => t.Tag).ToList();
        var after = Sfnt.ReadTables(decoded).OrderBy(t => t.Tag).ToList();

        Assert.Equal(before.Select(t => t.TagName), after.Select(t => t.TagName));

        for (var i = 0; i < before.Count; i++)
        {
            var expected = before[i].Data;
            var actual = after[i].Data;

            // head.checkSumAdjustment describes the whole file, not the table,
            // and the rebuilt file is not required to lay its tables out the
            // same way the original did. So that one field is blanked in both
            // and checked separately below.
            if (before[i].TagName == "head")
            {
                expected = expected.ToArray();
                actual = actual.ToArray();
                expected.AsSpan(8, 4).Clear();
                actual.AsSpan(8, 4).Clear();
            }

            Assert.Equal(expected, actual);
        }

        // The real assertion about the checksum: summing the whole file as
        // big-endian words, with checkSumAdjustment counted, must come to the
        // magic constant. That is what DirectWrite checks.
        Assert.Equal(0xB1B0AFBAu, WholeFileChecksum(decoded));
    }

    private static uint WholeFileChecksum(byte[] file)
    {
        uint sum = 0;
        for (var i = 0; i + 4 <= file.Length; i += 4)
        {
            sum += BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan(i));
        }

        return sum;
    }

    [Theory]
    [InlineData("arial.ttf")]
    [InlineData("segoeui.ttf")]
    [InlineData("consola.ttf")]
    public void EveryWindowsFontSurvivesTheRoundTrip(string name)
    {
        var path = FontFixtures.WindowsFonts().FirstOrDefault(p => Path.GetFileName(p) == name);
        Assert.NotNull(path);

        var original = File.ReadAllBytes(path!);

        using var input = new MemoryStream(FontFixtures.ToWoff(original));
        var decoded = WoffDecoder.ToSfnt(input);

        var before = FontInspector.Read(original);
        var after = FontInspector.Read(decoded);

        Assert.Equal(before.Families, after.Families);
        Assert.Equal(before.GlyphCount, after.GlyphCount);
        Assert.Equal(before.FsType, after.FsType);
    }

    [Fact]
    public void SomethingThatIsNotAWoffIsRefusedWithAReason()
    {
        using var notAFont = new MemoryStream(new byte[64]);

        var ex = Assert.Throws<FontFormatException>(() => WoffDecoder.ToSfnt(notAFont));
        Assert.Contains("WOFF", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ATablePointingOutsideTheFileIsRefused()
    {
        var path = FontFixtures.AnyWindowsFont();
        Assert.NotNull(path);

        var woff = FontFixtures.ToWoff(File.ReadAllBytes(path!));

        // Point the first table's offset far past the end. This is the check
        // that stops a malformed font becoming an out-of-range read.
        BinaryPrimitives.WriteUInt32BigEndian(woff.AsSpan(48), 0x7FFFFFFF);

        using var input = new MemoryStream(woff);
        var ex = Assert.Throws<FontFormatException>(() => WoffDecoder.ToSfnt(input));
        Assert.Contains("outside", ex.Message, StringComparison.Ordinal);
    }
}

public class Woff2DecoderTests
{
    public static TheoryData<string> Fixtures()
    {
        var data = new TheoryData<string>();

        foreach (var path in FontFixtures.Dropped(".woff2"))
        {
            data.Add(path);
        }

        if (data.Count == 0)
        {
            data.Add(string.Empty);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void ARealWoff2DecodesIntoAConsistentFont(string path)
    {
        if (path.Length == 0)
        {
            NoFixtures();
            return;
        }


        using var input = File.OpenRead(path);
        var sfnt = Woff2Decoder.ToSfnt(input);

        var info = FontInspector.Read(sfnt);

        Assert.NotEmpty(info.Families);
        Assert.True(info.GlyphCount > 0, "a font with no glyphs decoded");

        // FontInspector has already checked that head, name, cmap and either
        // glyf+loca or CFF are present and that no table points outside the
        // file, so what is left is whether the reconstruction is coherent.
        var tables = Sfnt.ReadTables(sfnt);
        CheckLocaMatchesGlyf(tables, info.GlyphCount);
        CheckEveryGlyphIsInsideTheFontBoundingBox(tables, info.GlyphCount);
        CheckHmtxHasAMetricPerGlyph(tables, info.GlyphCount);
    }

    [Fact]
    public void ACollectionIsRefusedWithAnExplanationRatherThanADamagedFont()
    {
        // Section 6.6.5 asks for collections; this decoder does not do them,
        // and the one thing it must not do is pretend. See the Phase 2 report.
        var header = new byte[48];
        BinaryPrimitives.WriteUInt32BigEndian(header, 0x774F4632);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), Sfnt.CollectionTag);
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(12), 1);

        using var input = new MemoryStream(header);
        var ex = Assert.Throws<FontFormatException>(() => Woff2Decoder.ToSfnt(input));

        Assert.Equal(Woff2Decoder.CollectionsNotSupported, ex.Message);
    }

    [Fact]
    public void SomethingThatIsNotAWoff2IsRefusedWithAReason()
    {
        using var notAFont = new MemoryStream(new byte[64]);

        var ex = Assert.Throws<FontFormatException>(() => Woff2Decoder.ToSfnt(notAFont));
        Assert.Contains("WOFF 2.0", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ATruncatedWoff2IsRefusedRatherThanReadingPastTheEnd()
    {
        var fixtures = FontFixtures.Dropped(".woff2");
        if (fixtures.Count == 0)
        {
            NoFixtures();
            return;
        }


        var whole = File.ReadAllBytes(fixtures[0]);
        using var input = new MemoryStream(whole.AsSpan(0, whole.Length / 2).ToArray());

        Assert.ThrowsAny<Exception>(() => Woff2Decoder.ToSfnt(input));
    }

    /// <summary>
    /// xUnit 2.9 has no way to skip a test at run time, so a missing fixture
    /// folder makes these tests pass without checking anything. That is
    /// deliberate - a clean clone and the CI runner have no .woff2 files and
    /// should stay green - but it means the real-file WOFF2 coverage only runs
    /// where the fixtures are. The Phase 2 report says so rather than leaving
    /// it to be discovered.
    /// </summary>
    private static void NoFixtures()
    {
    }

    private static void CheckLocaMatchesGlyf(IReadOnlyList<Sfnt.Table> tables, int glyphCount)
    {
        var glyf = Sfnt.Find(tables, "glyf");
        var loca = Sfnt.Find(tables, "loca");
        var head = Sfnt.Find(tables, "head");

        if (glyf is null || loca is null)
        {
            return;       // a CFF font has neither
        }

        Assert.NotNull(head);
        var longFormat = BinaryPrimitives.ReadInt16BigEndian(head!.Data.AsSpan(50)) == 1;
        var entrySize = longFormat ? 4 : 2;

        Assert.Equal((glyphCount + 1) * entrySize, loca.Data.Length);

        uint previous = 0;
        for (var i = 0; i <= glyphCount; i++)
        {
            var offset = longFormat
                ? BinaryPrimitives.ReadUInt32BigEndian(loca.Data.AsSpan(i * 4))
                : (uint)BinaryPrimitives.ReadUInt16BigEndian(loca.Data.AsSpan(i * 2)) * 2;

            Assert.True(offset >= previous, $"loca goes backwards at glyph {i}");
            Assert.True(offset <= glyf.Data.Length, $"loca entry {i} points past the end of glyf");

            previous = offset;
        }

        // The last entry is the length of the table, which is what ties the
        // two reconstructions together.
        Assert.Equal((uint)glyf.Data.Length, previous);
    }

    /// <summary>
    /// The check that would catch a misread triplet table: coordinates decoded
    /// wrongly land outside the bounding box the font declares in head.
    /// </summary>
    private static void CheckEveryGlyphIsInsideTheFontBoundingBox(
        IReadOnlyList<Sfnt.Table> tables,
        int glyphCount)
    {
        var glyf = Sfnt.Find(tables, "glyf");
        var loca = Sfnt.Find(tables, "loca");
        var head = Sfnt.Find(tables, "head");

        if (glyf is null || loca is null || head is null || head.Data.Length < 54)
        {
            return;
        }

        var span = head.Data.AsSpan();
        var fontXMin = BinaryPrimitives.ReadInt16BigEndian(span[36..]);
        var fontYMin = BinaryPrimitives.ReadInt16BigEndian(span[38..]);
        var fontXMax = BinaryPrimitives.ReadInt16BigEndian(span[40..]);
        var fontYMax = BinaryPrimitives.ReadInt16BigEndian(span[42..]);
        var longFormat = BinaryPrimitives.ReadInt16BigEndian(span[50..]) == 1;

        var checked_ = 0;

        for (var i = 0; i < glyphCount; i++)
        {
            var (start, end) = Entry(loca.Data, i, longFormat);
            if (end - start < 10)
            {
                continue;        // an empty glyph has no bounding box
            }

            var glyph = glyf.Data.AsSpan(start, end - start);
            var xMin = BinaryPrimitives.ReadInt16BigEndian(glyph[2..]);
            var yMin = BinaryPrimitives.ReadInt16BigEndian(glyph[4..]);
            var xMax = BinaryPrimitives.ReadInt16BigEndian(glyph[6..]);
            var yMax = BinaryPrimitives.ReadInt16BigEndian(glyph[8..]);

            Assert.True(xMin <= xMax, $"glyph {i} has an inverted x range: {xMin}..{xMax}");
            Assert.True(yMin <= yMax, $"glyph {i} has an inverted y range: {yMin}..{yMax}");

            Assert.True(
                xMin >= fontXMin && xMax <= fontXMax && yMin >= fontYMin && yMax <= fontYMax,
                $"glyph {i} ({xMin},{yMin})-({xMax},{yMax}) lies outside the font's declared "
                + $"box ({fontXMin},{fontYMin})-({fontXMax},{fontYMax})");

            checked_++;
        }

        Assert.True(checked_ > 0, "the font has no glyphs with outlines to check");
    }

    private static void CheckHmtxHasAMetricPerGlyph(IReadOnlyList<Sfnt.Table> tables, int glyphCount)
    {
        var hmtx = Sfnt.Find(tables, "hmtx");
        var hhea = Sfnt.Find(tables, "hhea");

        if (hmtx is null || hhea is null || hhea.Data.Length < 36)
        {
            return;
        }

        var numberOfHMetrics = BinaryPrimitives.ReadUInt16BigEndian(hhea.Data.AsSpan(34));
        var expected = (numberOfHMetrics * 4) + ((glyphCount - numberOfHMetrics) * 2);

        Assert.Equal(expected, hmtx.Data.Length);
    }

    private static (int Start, int End) Entry(byte[] loca, int glyph, bool longFormat) =>
        longFormat
            ? ((int)BinaryPrimitives.ReadUInt32BigEndian(loca.AsSpan(glyph * 4)),
               (int)BinaryPrimitives.ReadUInt32BigEndian(loca.AsSpan((glyph + 1) * 4)))
            : (BinaryPrimitives.ReadUInt16BigEndian(loca.AsSpan(glyph * 2)) * 2,
               BinaryPrimitives.ReadUInt16BigEndian(loca.AsSpan((glyph + 1) * 2)) * 2);
}

public class LimitsTests
{
    [Fact]
    public void ADecodedFontOverTheCapIsRefused()
    {
        Assert.Throws<FileTooLargeException>(() => Limits.CheckDecodedFontSize(Limits.MaxDecodedFontBytes + 1));
        Limits.CheckDecodedFontSize(Limits.MaxDecodedFontBytes);
    }

    [Theory]
    [InlineData("../escape.ttf")]
    [InlineData("..\\escape.ttf")]
    [InlineData("/absolute.ttf")]
    [InlineData("C:\\windows\\fonts\\arial.ttf")]
    [InlineData("fonts/../../escape.ttf")]
    public void AnEntryNameThatClimbsOutOfTheArchiveIsRefused(string name) =>
        Assert.Throws<FileTooLargeException>(() => Limits.CheckEntryName(name));

    [Theory]
    [InlineData("font.ttf")]
    [InlineData("subfolder/font.otf")]
    [InlineData("a.b/c.woff2")]
    public void AnOrdinaryEntryNameIsAccepted(string name) => Limits.CheckEntryName(name);
}
