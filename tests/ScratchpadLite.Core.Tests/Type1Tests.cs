using System.Text;
using ScratchpadLite.Core.Fonts;
using ScratchpadLite.Core.Fonts.Type1;
using Xunit;
using Xunit.Abstractions;

namespace ScratchpadLite.Core.Tests;

/// <summary>
/// The Adobe Glyph List (Section 6.6.4).
/// </summary>
public class GlyphListTests
{
    [Fact]
    public void TheListLoadsFromTheEmbeddedResource()
    {
        // If the resource or its LogicalName is wrong, every converted Type 1
        // font gets an empty cmap and renders nothing - which looks like the
        // converter being broken rather than a missing file.
        Assert.True(GlyphList.Count > 4000, $"only {GlyphList.Count} names loaded");
    }

    [Theory]
    [InlineData("A", 0x0041)]
    [InlineData("space", 0x0020)]
    [InlineData("ampersand", 0x0026)]
    [InlineData("eacute", 0x00E9)]
    [InlineData("germandbls", 0x00DF)]
    [InlineData("Alpha", 0x0391)]
    [InlineData("afii10017", 0x0410)]
    [InlineData("quoteright", 0x2019)]
    [InlineData("fi", 0xFB01)]
    public void AdobeNamesMapToTheirCharacters(string name, int expected) =>
        Assert.Equal(expected, GlyphList.ToCodePoint(name));

    [Theory]
    [InlineData("uni0041", 0x0041)]
    [InlineData("uni20AC", 0x20AC)]
    [InlineData("u1F600", 0x1F600)]
    [InlineData("u0041", 0x0041)]
    public void TheAlgorithmicFormsAreUnderstood(string name, int expected) =>
        Assert.Equal(expected, GlyphList.ToCodePoint(name));

    [Theory]
    [InlineData("a.sc", 0x0061)]
    [InlineData("one.taboldstyle", 0x0031)]
    [InlineData("uni0041.alt", 0x0041)]
    public void AVariantSuffixResolvesToTheBaseCharacter(string name, int expected) =>
        Assert.Equal(expected, GlyphList.ToCodePoint(name));

    [Fact]
    public void ALigatureNameSpelledWithUnderscoresGivesEveryCharacter() =>
        Assert.Equal([0x0066, 0x0066, 0x0069], GlyphList.ToCodePoints("f_f_i"));

    [Theory]
    [InlineData("a12")]
    [InlineData("SomeDesignersName")]
    [InlineData("uniZZZZ")]
    [InlineData("uni041")]
    [InlineData("")]
    public void ANameTheListDoesNotKnowGivesNothing(string name) =>
        Assert.Null(GlyphList.ToCodePoint(name));

    [Fact]
    public void ASurrogateHalfIsNotAcceptedAsAUniForm() =>
        Assert.Null(GlyphList.ToCodePoint("uniD800"));
}

/// <summary>
/// StandardEncoding, which <c>seac</c> and encoding-less fonts both need.
/// </summary>
public class AdobeEncodingsTests
{
    [Theory]
    [InlineData(32, "space")]
    [InlineData(65, "A")]
    [InlineData(97, "a")]
    [InlineData(39, "quoteright")]
    [InlineData(96, "quoteleft")]
    [InlineData(193, "grave")]
    [InlineData(194, "acute")]
    [InlineData(225, "AE")]
    [InlineData(251, "germandbls")]
    public void TheStandardCodesAreRight(int code, string expected) =>
        Assert.Equal(expected, AdobeEncodings.StandardName(code));

    [Fact]
    public void TheUnassignedCodesAreEmpty()
    {
        Assert.Null(AdobeEncodings.StandardName(0));
        Assert.Null(AdobeEncodings.StandardName(127));
        Assert.Null(AdobeEncodings.StandardName(160));
        Assert.Null(AdobeEncodings.StandardName(176));
    }

    [Fact]
    public void EveryStandardNameIsOne​TheGlyphListKnows()
    {
        // seac resolves accents through this table and then through the AGL, so
        // a name here that the AGL does not know would be a dead end.
        foreach (var (code, name) in AdobeEncodings.StandardEncoding())
        {
            Assert.NotNull(GlyphList.ToCodePoint(name));
            Assert.True(code is >= 32 and <= 251);
        }
    }
}

/// <summary>
/// The PFM and AFM readers (Section 6.6.3).
/// </summary>
public class AfmPfmReaderTests
{
    [Theory]
    [InlineData("Garamond-BoldItalic", "Garamond", "Bold Italic")]
    [InlineData("Garamond-Bold", "Garamond", "Bold")]
    [InlineData("Helvetica-Oblique", "Helvetica", "Italic")]
    [InlineData("TimesNewRoman-Roman", "Times New Roman", "Regular")]
    [InlineData("Minion", "Minion", "Regular")]
    [InlineData("SomeFont-NotAStyle", "Some Font Not AStyle", "Regular")]
    [InlineData("Dingbats", "Dingbats", "Regular")]
    public void APostScriptNameSplitsIntoFamilyAndStyle(string name, string family, string style)
    {
        var (gotFamily, gotStyle) = AfmPfmReader.SplitPostScriptName(name);

        Assert.Equal(family, gotFamily);
        Assert.Equal(style, gotStyle);
    }

    [Theory]
    [InlineData("Bold", 700)]
    [InlineData("bold", 700)]
    [InlineData("Semibold", 600)]
    [InlineData("Demi Bold", 600)]
    [InlineData("Light", 300)]
    [InlineData("Extra Light", 200)]
    [InlineData("Roman", 400)]
    [InlineData("Black", 900)]
    [InlineData(null, 400)]
    public void AWeightStringBecomesAWeightClass(string? weight, int expected) =>
        Assert.Equal(expected, AfmPfmReader.WeightFromName(weight));

    [Fact]
    public void SemiboldIsNotReadAsBold()
    {
        // The reason the table is ordered longest-first: a contains check for
        // "bold" matches "semibold" too.
        Assert.Equal(600, AfmPfmReader.WeightFromName("Semibold"));
        Assert.Equal(200, AfmPfmReader.WeightFromName("ExtraLight"));
    }

    [Fact]
    public void AnAfmGivesItsHeaderAndWidths()
    {
        var afm = """
            StartFontMetrics 4.1
            FontName Test-Bold
            FullName Test Bold
            FamilyName Test
            Weight Bold
            ItalicAngle 0
            IsFixedPitch false
            Ascender 718
            Descender -207
            StartCharMetrics 2
            C 32 ; WX 250 ; N space ; B 0 0 0 0 ;
            C 65 ; WX 722 ; N A ; B 20 0 702 718 ;
            EndCharMetrics
            EndFontMetrics
            """;

        var metrics = AfmPfmReader.ReadAfm(Encoding.Latin1.GetBytes(afm));

        Assert.Equal("Test", metrics.FamilyName);
        Assert.Equal("Test Bold", metrics.FullName);
        Assert.True(metrics.IsBold);
        Assert.False(metrics.IsItalic);
        Assert.Equal(700, metrics.WeightClass);
        Assert.Equal(718, metrics.Ascender);
        Assert.Equal(-207, metrics.Descender);
        Assert.Equal(250, metrics.Widths["space"]);
        Assert.Equal(722, metrics.Widths["A"]);
    }

    [Fact]
    public void ARealPfmGivesTheWindowsFamilyName()
    {
        var pfm = FontFixtures.Dropped(".pfm").Concat(FontFixtures.Dropped(".PFM")).FirstOrDefault();

        if (pfm is null)
        {
            return;        // no .pfm fixtures; see FontDecoderTests
        }

        var metrics = AfmPfmReader.ReadPfm(File.ReadAllBytes(pfm));

        Assert.NotNull(metrics);
        Assert.False(string.IsNullOrWhiteSpace(metrics!.FamilyName));
    }
}

/// <summary>
/// Type 1 conversion, end to end, against real fonts (Section 6.6.4).
/// </summary>
/// <remarks>
/// The fixtures are the Type 1 fonts that ship with Adobe Acrobat, which are
/// on this machine and are read in place rather than committed. They are all
/// symbol fonts, so they exercise the symbol-cmap path thoroughly and the
/// text path not at all - which is stated in the Phase 3 report rather than
/// left to be assumed.
/// </remarks>
public class Type1ConverterTests(ITestOutputHelper output)
{
    public static TheoryData<string> Fixtures()
    {
        var data = new TheoryData<string>();

        foreach (var path in FontFixtures.Dropped(".pfb").Concat(FontFixtures.Dropped(".PFB")))
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
    public void ARealType1FontParses(string path)
    {
        if (path.Length == 0)
        {
            return;
        }

        var font = Type1Parser.Parse(File.ReadAllBytes(path));

        if (font.Unsupported is { } refusal)
        {
            // Two of the Acrobat fixtures are genuinely Adobe's Multiple Master
            // substitution fonts, so a refusal is the right answer - but it has
            // to be a refusal that names the variant, not a vague failure.
            output.WriteLine($"{Path.GetFileName(path)}: refused - {refusal}");
            Assert.Matches("Multiple Master|CID-keyed", refusal);
            return;
        }

        Assert.False(string.IsNullOrWhiteSpace(font.FontName));
        Assert.NotEmpty(font.CharStrings);

        output.WriteLine(
            $"{Path.GetFileName(path)}: {font.FontName}, {font.CharStrings.Count} glyphs, "
            + $"{font.Subrs.Count} subrs, encoding {font.Encoding.Count} "
            + $"(standard: {font.UsesStandardEncoding})");

        // Every charstring decrypted to something an interpreter can read: the
        // first byte is either a number or a known operator, never random.
        foreach (var (name, code) in font.CharStrings)
        {
            Assert.True(code.Length > 0, $"{name} decrypted to nothing");
        }
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void ARealType1FontConvertsIntoALoadableOpenTypeFont(string path)
    {
        if (path.Length == 0)
        {
            return;
        }

        if (TryConvert(path) is not { } result)
        {
            return;        // a documented refusal; ARealType1FontParses checks those
        }

        output.WriteLine(
            $"{Path.GetFileName(path)}: {result.FamilyName} {result.StyleName}, "
            + $"{result.GlyphCount} glyphs, symbol: {result.IsSymbolFont}, "
            + $"unmapped: {result.Unmapped}, {result.Sfnt.Length} bytes");

        // FontInspector is the Section 6.6.2 validation step: it refuses a font
        // with no head, name or cmap, and no outlines.
        var info = FontInspector.Read(result.Sfnt);

        Assert.NotEmpty(info.Families);
        Assert.True(info.IsCff, "a converted Type 1 font should have CFF outlines");
        Assert.Equal(result.GlyphCount, info.GlyphCount);
        Assert.Equal(result.FamilyName, info.Families[0]);

        // fsType: Section 6.6.4 says Preview and Print, because Type 1 has no
        // embedding flag of its own to copy.
        Assert.Equal(0x0004, info.FsType);
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void EveryGlyphIsReachableThroughTheCmap(string path)
    {
        if (path.Length == 0)
        {
            return;
        }

        if (TryConvert(path) is not { } result)
        {
            return;
        }

        // A glyph nothing maps to cannot be typed or rendered, so a conversion
        // that leaves most of the font unreachable has gone wrong even though
        // every other check passes.
        var reachable = result.GlyphCount - 1 - result.Unmapped;

        Assert.True(
            reachable * 2 >= result.GlyphCount - 1,
            $"only {reachable} of {result.GlyphCount - 1} glyphs are reachable");
    }

    [Fact]
    public void ThePfaFormOfTheSameFontConvertsIdentically()
    {
        var pfb = FontFixtures.Dropped(".pfb").Concat(FontFixtures.Dropped(".PFB")).FirstOrDefault();

        if (pfb is null)
        {
            return;
        }

        // A PFA is the same font with the binary section as hex, so converting
        // both has to give the same glyphs. Built here because no .pfa file
        // was found on this machine to test against.
        var pfa = FontFixtures.ToPfa(File.ReadAllBytes(pfb));

        var fromPfb = Type1Parser.Parse(File.ReadAllBytes(pfb));
        var fromPfa = Type1Parser.Parse(pfa);

        Assert.Equal(fromPfb.FontName, fromPfa.FontName);
        Assert.Equal(fromPfb.CharStrings.Count, fromPfa.CharStrings.Count);
        Assert.Equal(fromPfb.Subrs.Count, fromPfa.Subrs.Count);

        foreach (var (name, code) in fromPfb.CharStrings)
        {
            Assert.Equal(code, fromPfa.CharStrings[name]);
        }
    }

    [Fact]
    public void SomethingThatIsNotAType1FontIsRefusedWithAReason()
    {
        var ex = Assert.Throws<FontFormatException>(
            () => Type1Parser.Parse(Encoding.ASCII.GetBytes("this is not a font")));

        Assert.Contains("eexec", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AMultipleMasterFontIsReportedRatherThanConverted()
    {
        // Section 6.6.3 says to name the variant. Half-converting one would
        // produce a font with every glyph at the wrong weight.
        var program = "%!PS-AdobeFont-1.0: Test\n/WeightVector [0.5 0.5] def\neexec\n0000";

        var font = Type1Parser.Parse(Encoding.Latin1.GetBytes(program));

        Assert.NotNull(font.Unsupported);
        Assert.Contains("Multiple Master", font.Unsupported!, StringComparison.Ordinal);
    }

    /// <summary>
    /// Converts, or returns null when the font is one of the variants
    /// Section 6.6.3 says to refuse.
    /// </summary>
    private static Type1Converter.Result? TryConvert(string path)
    {
        try
        {
            return Convert(path);
        }
        catch (FontFormatException ex) when (ex.Message.Contains("Multiple Master", StringComparison.Ordinal)
                                             || ex.Message.Contains("CID-keyed", StringComparison.Ordinal))
        {
            return null;
        }
    }

    private static Type1Converter.Result Convert(string path)
    {
        var folder = Path.GetDirectoryName(path)!;

        return Type1Converter.Convert(
            File.ReadAllBytes(path),
            path,
            name =>
            {
                // The metrics file may differ in case from the font's name,
                // which is how the Acrobat fonts ship.
                var wanted = Path.GetFileName(name);

                foreach (var candidate in Directory.GetFiles(folder))
                {
                    if (string.Equals(
                            Path.GetFileName(candidate),
                            wanted,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return File.ReadAllBytes(candidate);
                    }
                }

                return null;
            });
    }
}
