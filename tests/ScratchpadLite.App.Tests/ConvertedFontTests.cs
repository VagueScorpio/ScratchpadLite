using System.IO;
using System.Windows;
using System.Windows.Media;
using ScratchpadLite.Core.Fonts;
using ScratchpadLite.Core.Fonts.Type1;
using Xunit;
using Xunit.Abstractions;

namespace ScratchpadLite.App.Tests;

/// <summary>
/// The Phase 3 converters, through WPF (Section 6.6.4, step 7).
/// </summary>
/// <remarks>
/// This is the test that decides whether the converters work. A font can
/// parse, validate and look entirely reasonable and still not load:
/// DirectWrite is stricter than the specification in places, and it is what
/// will be asked to draw the text.
/// <para>
/// Everything goes through <c>new GlyphTypeface(uri)</c>. The other route -
/// <c>Fonts.GetFontFamilies(uri)</c>, then a Typeface, then FormattedText -
/// is unreliable in a test process: it throws FileFormatException or a
/// NullReferenceException from inside WPF's font cache after the first font,
/// and does it for an untouched arial.ttf as readily as for a converted one.
/// Hours went into a conversion bug that turned out to be that. GlyphTypeface
/// is also the stronger check, because it can be asked for the character map,
/// the advance widths and an actual glyph outline.
/// </para>
/// </remarks>
[Collection(nameof(AppHost))]
public class ConvertedFontTests(ITestOutputHelper output)
{
    /// <summary>What WPF made of a font.</summary>
    private sealed record Loaded(int Glyphs, int CmapEntries, int NonZeroWidths, Rect LargestOutline);

    private static IReadOnlyList<string> Fixtures(string extension)
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Fonts");

        return Directory.Exists(dir) ? Directory.GetFiles(dir, $"*{extension}") : [];
    }

    [Fact]
    public void WpfReadsEveryConvertedType1Font()
    {
        var fixtures = Fixtures(".pfa").Concat(Fixtures(".pfb")).ToList();

        if (fixtures.Count == 0)
        {
            return;        // see tests/Fixtures/Fonts/README.txt
        }

        var converted = 0;

        foreach (var path in fixtures)
        {
            byte[] sfnt;

            try
            {
                sfnt = ConvertType1(path);
            }
            catch (FontFormatException ex)
                when (ex.Message.Contains("Multiple Master", StringComparison.Ordinal)
                      || ex.Message.Contains("CID-keyed", StringComparison.Ordinal))
            {
                output.WriteLine($"{Path.GetFileName(path)}: refused as expected");
                continue;
            }

            var loaded = AppHost.Run(() => Load(sfnt));

            output.WriteLine(
                $"{Path.GetFileName(path)}: {loaded.Glyphs} glyphs, {loaded.CmapEntries} cmap entries, "
                + $"{loaded.NonZeroWidths} non-zero widths, outline {loaded.LargestOutline}");

            Assert.True(loaded.Glyphs > 1, "the font has no glyphs beyond .notdef");

            // The cmap is the whole point of the Adobe Glyph List work: without
            // it the glyphs exist and no character reaches them.
            Assert.True(loaded.CmapEntries > 1, "nothing in the font is reachable by character");

            // All-zero widths renders as nothing visible, which a glyph count
            // alone would not catch.
            Assert.True(loaded.NonZeroWidths > 1, "every advance width was zero");

            // And the charstrings really do describe shapes.
            Assert.True(
                loaded.LargestOutline.Width > 0 && loaded.LargestOutline.Height > 0,
                "no glyph produced an outline with any area");

            converted++;
        }

        Assert.True(converted > 0, "no Type 1 fixture converted into a font WPF would read");
    }

    [Fact]
    public void WpfReadsEveryFontRecoveredFromAnEot()
    {
        var fixtures = Fixtures(".eot");

        if (fixtures.Count == 0)
        {
            return;
        }

        var recovered = 0;

        foreach (var path in fixtures)
        {
            byte[] sfnt;

            try
            {
                using var input = File.OpenRead(path);
                sfnt = EotDecoder.ToSfnt(input);
            }
            catch (FontFormatException ex) when (ex.Message.Contains("MicroType", StringComparison.Ordinal))
            {
                output.WriteLine($"{Path.GetFileName(path)}: compressed, refused as expected");
                continue;
            }

            var loaded = AppHost.Run(() => Load(sfnt));

            output.WriteLine(
                $"{Path.GetFileName(path)}: {loaded.Glyphs} glyphs, {loaded.CmapEntries} cmap entries");

            Assert.True(loaded.Glyphs > 1);
            Assert.True(loaded.CmapEntries > 1);
            Assert.True(loaded.NonZeroWidths > 1);

            recovered++;
        }

        Assert.True(recovered > 0, "no EOT fixture produced a font WPF would read");
    }

    private static byte[] ConvertType1(string path)
    {
        var folder = Path.GetDirectoryName(path)!;

        return Type1Converter.Convert(
            File.ReadAllBytes(path),
            path,
            name =>
            {
                var wanted = Path.GetFileName(name);

                foreach (var candidate in Directory.GetFiles(folder))
                {
                    if (string.Equals(
                            Path.GetFileName(candidate), wanted, StringComparison.OrdinalIgnoreCase))
                    {
                        return File.ReadAllBytes(candidate);
                    }
                }

                return null;
            }).Sfnt;
    }

    private static Loaded Load(byte[] sfnt)
    {
        // Each font goes in a directory of its own, and is left there. Two
        // reasons, both learned the hard way:
        //
        // WPF reads a font file lazily, so deleting it after loading produces
        // "Unable to find the specified file" from inside DirectWrite later on.
        //
        // And WPF's font cache is keyed by directory. With several fonts in one
        // folder, the first loads and every one after it throws - either
        // FileFormatException or a NullReferenceException from inside the cache -
        // whether or not the fonts are valid.
        var folder = Path.Combine(Path.GetTempPath(), $"sp-conv-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);

        var file = Path.Combine(folder, "font.otf");
        File.WriteAllBytes(file, sfnt);

        var glyphTypeface = new GlyphTypeface(new Uri(file));

        var widths = glyphTypeface.AdvanceWidths.Count(w => w.Value > 0);
        var largest = Rect.Empty;

        // Walked by glyph index rather than sampled from the character map,
        // whose enumeration order starts with spaces and punctuation - a
        // sample of the first forty found nothing but blank glyphs. A couple
        // of hundred indices is enough to find a letter in any font and cheap
        // enough not to rasterise a CJK font in full.
        var largestArea = 0.0;

        for (ushort glyph = 1; glyph < Math.Min(200, glyphTypeface.GlyphCount); glyph++)
        {
            var bounds = glyphTypeface.GetGlyphOutline(glyph, 32, 32).Bounds;
            var area = bounds.IsEmpty ? 0 : bounds.Width * bounds.Height;

            if (area > largestArea)
            {
                largestArea = area;
                largest = bounds;
            }
        }

        return new Loaded(
            glyphTypeface.GlyphCount,
            glyphTypeface.CharacterToGlyphMap.Count,
            widths,
            largest);
    }
}
