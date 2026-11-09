using System.IO.Compression;
using ScratchpadLite.Core.Fonts;
using ScratchpadLite.Core.Formats;
using ScratchpadLite.Core.Platform;
using Xunit;

namespace ScratchpadLite.Core.Tests;

/// <summary>
/// Classifying a file (Section 6.6.2, step 2).
/// </summary>
public class FontTypeDetectorTests
{
    [Fact]
    public void TheMagicBytesBeatTheExtension()
    {
        // Font bundles downloaded from the web routinely get this wrong, and
        // believing the extension would send a WOFF straight to WPF, which
        // cannot read one.
        var woff = Header(0x77, 0x4F, 0x46, 0x46);

        Assert.Equal(FontFileKind.Woff, FontTypeDetector.Classify("Lying.ttf", woff));
        Assert.Equal(FontFileKind.Woff, FontTypeDetector.Classify("Lying.otf", woff));
    }

    [Theory]
    [InlineData(FontFileKind.Direct, 0x00, 0x01, 0x00, 0x00)]
    [InlineData(FontFileKind.Direct, (byte)'t', (byte)'r', (byte)'u', (byte)'e')]
    [InlineData(FontFileKind.Direct, (byte)'O', (byte)'T', (byte)'T', (byte)'O')]
    [InlineData(FontFileKind.Direct, (byte)'t', (byte)'t', (byte)'c', (byte)'f')]
    [InlineData(FontFileKind.Woff, (byte)'w', (byte)'O', (byte)'F', (byte)'F')]
    [InlineData(FontFileKind.Woff2, (byte)'w', (byte)'O', (byte)'F', (byte)'2')]
    [InlineData(FontFileKind.ZipArchive, (byte)'P', (byte)'K', 0x03, 0x04)]
    public void SignaturesAreRecognised(FontFileKind expected, byte a, byte b, byte c, byte d) =>
        Assert.Equal(expected, FontTypeDetector.Classify("whatever.bin", Header(a, b, c, d)));

    [Fact]
    public void APfbIsRecognisedByItsSegmentHeader() =>
        Assert.Equal(FontFileKind.Type1, FontTypeDetector.Classify("font.pfb", Header(0x80, 0x01, 0x00, 0x00)));

    [Theory]
    [InlineData("font.fon")]
    [InlineData("font.bdf")]
    [InlineData("font.svg")]
    [InlineData("Design.glyphs")]
    [InlineData("Source.ufo")]
    public void FormatsThatCannotWorkAreRefusedByNameWithoutBeingOpened(string name)
    {
        Assert.Equal(FontFileKind.Unsupported, FontTypeDetector.Classify(name, []));

        // The point of refusing by name: the report can explain it without the
        // file ever being read.
        Assert.NotNull(FontTypeDetector.RefusalFor(name));
    }

    [Fact]
    public void AnAppleDoubleFileIsRecognisedByItsNamePrefix() =>
        Assert.Equal(FontFileKind.MacResource, FontTypeDetector.Classify("._Helvetica", Header(0, 5, 22, 7)));

    [Theory]
    [InlineData("OFL.txt")]
    [InlineData("LICENSE")]
    [InlineData("README.md")]
    public void LicenceTextIsKeptForTheLicenceGenerator(string name) =>
        Assert.Equal(FontFileKind.LicenseText, FontTypeDetector.Classify(name, Header(0x41, 0x42, 0x43, 0x44)));

    [Fact]
    public void SomethingThatIsNotAFontAtAllIsIgnoredSilently() =>
        Assert.Equal(FontFileKind.Ignored, FontTypeDetector.Classify("notes.dat", Header(1, 2, 3, 4)));

    private static byte[] Header(params byte[] bytes)
    {
        var header = new byte[FontTypeDetector.HeaderBytes];
        bytes.CopyTo(header, 0);
        return header;
    }
}

/// <summary>
/// The index and the cache (Sections 6.6.1 and 6.6.2).
/// </summary>
public class FontIndexTests
{
    [Fact]
    public void AnIndexRoundTripsThroughItsFile()
    {
        var dir = Directory.CreateTempSubdirectory("sp-index-");
        try
        {
            var path = Path.Combine(dir.FullName, "index.json");
            var index = new FontIndex();

            index.Put(Entry("Inter.woff2", "abc123", ["Inter"]) with
            {
                Kind = FontFileKind.Woff2,
                Status = FontStatus.Converted,
                HasLatinLetters = true,
                GlyphCount = 2926,
                FsType = 0x0004,
                Message = "Converted (WOFF2 -> OpenType)",
            });

            index.Save(path);

            var loaded = FontIndex.Load(path);
            var entry = loaded.Find("Inter.woff2");

            Assert.NotNull(entry);
            Assert.False(loaded.WasRebuilt);
            Assert.Equal(["Inter"], entry!.Families);
            Assert.Equal(FontStatus.Converted, entry.Status);
            Assert.Equal(FontFileKind.Woff2, entry.Kind);
            Assert.True(entry.HasLatinLetters);
            Assert.Equal(2926, entry.GlyphCount);
            Assert.Equal(0x0004, entry.FsType);
            Assert.True(entry.IsUsable);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void AnIndexFromAnotherVersionIsRebuiltRatherThanHalfUnderstood()
    {
        var dir = Directory.CreateTempSubdirectory("sp-index-ver-");
        try
        {
            var path = Path.Combine(dir.FullName, "index.json");
            File.WriteAllText(path, """{ "version": 9999, "entries": [] }""");

            Assert.True(FontIndex.Load(path).WasRebuilt);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void ADamagedIndexCostsOneSlowLaunchRatherThanAnError()
    {
        var dir = Directory.CreateTempSubdirectory("sp-index-bad-");
        try
        {
            var path = Path.Combine(dir.FullName, "index.json");
            File.WriteAllText(path, "{ not json");

            var index = FontIndex.Load(path);

            Assert.True(index.WasRebuilt);
            Assert.Empty(index.Entries);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void AFileIsUnchangedOnlyWhenBothSizeAndTimestampMatch()
    {
        var when = new DateTime(2026, 11, 8, 12, 0, 0, DateTimeKind.Utc);
        var index = new FontIndex();

        index.Put(Entry("Arial.ttf", "hash", ["Arial"]) with { Length = 1000, ModifiedUtc = when });

        Assert.True(index.IsUnchanged(Found("Arial.ttf", 1000, when)));
        Assert.False(index.IsUnchanged(Found("Arial.ttf", 1001, when)));
        Assert.False(index.IsUnchanged(Found("Arial.ttf", 1000, when.AddSeconds(1))));
        Assert.False(index.IsUnchanged(Found("Other.ttf", 1000, when)));
    }

    [Fact]
    public void PruningReleasesACacheFileOnlyWhenNothingElseUsesIt()
    {
        var index = new FontIndex();

        // The same font dropped in twice shares one cache file, so removing
        // one copy must not delete the file the other is still using.
        index.Put(Entry("one/Inter.ttf", "shared", ["Inter"]));
        index.Put(Entry("two/Inter.ttf", "shared", ["Inter"]));
        index.Put(Entry("Alone.ttf", "lonely", ["Alone"]));

        var released = index.PruneMissing(["two/Inter.ttf"]);

        Assert.Equal(["lonely.ttf"], released);
        Assert.Null(index.Find("one/Inter.ttf"));
        Assert.NotNull(index.Find("two/Inter.ttf"));

        Assert.Equal(["shared.ttf"], index.PruneMissing([]));
    }

    private static FontIndex.Entry Entry(string path, string sha1, string[] families) =>
        new(path, 1000, DateTime.UnixEpoch, sha1, sha1 + ".ttf", families, null)
        {
            Status = FontStatus.Loaded,
            Kind = FontFileKind.Direct,
        };

    private static FontScanner.Found Found(string path, long length, DateTime when) =>
        new(path, FontFileKind.Direct, length, when) { RelativePath = path };
}

/// <summary>
/// The whole pipeline, against real fonts in a real folder (Section 6.6).
/// </summary>
/// <remarks>
/// This is the test that matters most in Phase 2: it builds a font folder the
/// way a user would - a TrueType font, a WOFF, a zip of fonts, a bitmap font,
/// a file that is not a font at all - and checks that the right things end up
/// usable and the rest end up explained.
/// </remarks>
public class FontPreparerTests : IDisposable
{
    private readonly DirectoryInfo _folder = Directory.CreateTempSubdirectory("sp-fonts-");

    public FontPreparerTests() => AppPaths.Initialize();

    public void Dispose()
    {
        try
        {
            _folder.Delete(recursive: true);
        }
        catch (IOException)
        {
            // A cache file WPF has open cannot be deleted; harmless here.
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void ARealFontFolderIsPreparedAndIndexed()
    {
        var source = FontFixtures.AnyWindowsFont();
        Assert.NotNull(source);

        var original = File.ReadAllBytes(source!);
        var expected = FontInspector.Read(original);

        File.WriteAllBytes(Path.Combine(_folder.FullName, "Plain.ttf"), original);
        File.WriteAllBytes(Path.Combine(_folder.FullName, "Packed.woff"), FontFixtures.ToWoff(original));
        File.WriteAllText(Path.Combine(_folder.FullName, "notes.dat"), "not a font");
        File.WriteAllBytes(Path.Combine(_folder.FullName, "Old.fon"), [1, 2, 3, 4]);
        File.WriteAllText(Path.Combine(_folder.FullName, "OFL.txt"), "a licence");

        var index = new FontIndex();
        var ready = new List<FontIndex.Entry>();

        var result = FontPreparer.CheckFolder(
            _folder.FullName, index, null, ready.Add, CancellationToken.None);

        Assert.True(result.Prepared >= 1, "the plain font should have been prepared");

        // The TrueType font loads as it is; the WOFF is converted. Both give
        // the same family, so one is marked a duplicate of the other.
        var plain = index.Find("Plain.ttf");
        var packed = index.Find("Packed.woff");

        Assert.NotNull(plain);
        Assert.NotNull(packed);
        Assert.Equal(expected.Families[0], plain!.Families[0]);
        Assert.Equal(expected.Families[0], packed!.Families[0]);

        // The .ttf wins over the .woff of the same font, because it needs no
        // conversion (Section 6.6.7).
        Assert.Equal(FontStatus.Loaded, plain.Status);
        Assert.Equal(FontStatus.Duplicate, packed.Status);
        Assert.Contains("Duplicate of Plain.ttf", packed.Message, StringComparison.Ordinal);

        // A bitmap font is reported with a reason rather than failing silently.
        var bitmap = index.Find("Old.fon");
        Assert.NotNull(bitmap);
        Assert.Equal(FontStatus.Unsupported, bitmap!.Status);
        Assert.Contains("Bitmap fonts", bitmap.Message, StringComparison.Ordinal);

        // A licence file is kept as a row, and something that is not a font at
        // all is not in the index at all.
        Assert.NotNull(index.Find("OFL.txt"));
        Assert.Null(index.Find("notes.dat"));

        // The cached file exists and holds a loadable font.
        var cached = FontCache.PathFor(plain.Sha1, Path.GetExtension(plain.CachedFile));
        Assert.True(File.Exists(cached), $"{cached} should have been written");
        Assert.Equal(expected.GlyphCount, FontInspector.Read(File.ReadAllBytes(cached)).GlyphCount);
    }

    [Fact]
    public void ASecondPassOverAnUnchangedFolderPreparesNothing()
    {
        // The whole point of the index: an unchanged folder costs a directory
        // listing and nothing else.
        var source = FontFixtures.AnyWindowsFont();
        Assert.NotNull(source);

        File.Copy(source!, Path.Combine(_folder.FullName, "Plain.ttf"));

        var index = new FontIndex();
        var first = FontPreparer.CheckFolder(_folder.FullName, index, null, null, CancellationToken.None);
        var second = FontPreparer.CheckFolder(_folder.FullName, index, null, null, CancellationToken.None);

        Assert.Equal(1, first.Prepared);
        Assert.Equal(0, second.Prepared);
        Assert.True(second.Unchanged >= 1);
    }

    [Fact]
    public void AFontThatChangesIsPreparedAgain()
    {
        var fonts = FontFixtures.WindowsFonts().Take(2).ToList();
        Assert.True(fonts.Count == 2, "this test needs two different Windows fonts");

        var path = Path.Combine(_folder.FullName, "Changing.ttf");
        File.Copy(fonts[0], path);

        var index = new FontIndex();
        FontPreparer.CheckFolder(_folder.FullName, index, null, null, CancellationToken.None);
        var before = index.Find("Changing.ttf")!.Families[0];

        // Replaced with a different font under the same name, which is what
        // happens when someone updates a font in the folder.
        File.Copy(fonts[1], path, overwrite: true);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(5));

        var again = FontPreparer.CheckFolder(_folder.FullName, index, null, null, CancellationToken.None);

        Assert.Equal(1, again.Prepared);
        Assert.NotEqual(before, index.Find("Changing.ttf")!.Families[0]);
    }

    [Fact]
    public void FontsInsideAZipArePreparedWithoutBeingExtracted()
    {
        var source = FontFixtures.AnyWindowsFont();
        Assert.NotNull(source);

        var zipPath = Path.Combine(_folder.FullName, "pack.zip");

        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            zip.CreateEntryFromFile(source!, "inner/Font.ttf");

            using var writer = new StreamWriter(zip.CreateEntry("inner/OFL.txt").Open());
            writer.Write("a licence");
        }

        var index = new FontIndex();
        FontPreparer.CheckFolder(_folder.FullName, index, null, null, CancellationToken.None);

        var member = index.Find("pack.zip!inner/Font.ttf");

        Assert.NotNull(member);
        Assert.True(member!.IsUsable);
        Assert.NotNull(index.Find("pack.zip"));

        // Nothing was unpacked into the font folder.
        Assert.False(Directory.Exists(Path.Combine(_folder.FullName, "inner")));
        Assert.Equal(["pack.zip"], Directory.GetFiles(_folder.FullName).Select(Path.GetFileName));
    }

    [Fact]
    public void ARemovedFontLeavesTheIndexAndReleasesItsCacheFile()
    {
        var source = FontFixtures.AnyWindowsFont();
        Assert.NotNull(source);

        var path = Path.Combine(_folder.FullName, "Temporary.ttf");
        File.Copy(source!, path);

        var index = new FontIndex();
        FontPreparer.CheckFolder(_folder.FullName, index, null, null, CancellationToken.None);
        Assert.NotNull(index.Find("Temporary.ttf"));

        File.Delete(path);
        var after = FontPreparer.CheckFolder(_folder.FullName, index, null, null, CancellationToken.None);

        Assert.Null(index.Find("Temporary.ttf"));
        Assert.True(after.Removed >= 1);
    }

    [Fact]
    public void ABrokenFontBecomesOneRowAndNothingElse()
    {
        // Valid signature, nothing behind it - which is what a truncated
        // download looks like.
        var broken = new byte[200];
        broken[1] = 0x01;
        File.WriteAllBytes(Path.Combine(_folder.FullName, "Truncated.ttf"), broken);

        var index = new FontIndex();
        var result = FontPreparer.CheckFolder(
            _folder.FullName, index, null, null, CancellationToken.None);

        var entry = index.Find("Truncated.ttf");

        Assert.NotNull(entry);
        Assert.False(entry!.IsUsable);
        Assert.NotEqual(FontStatus.Loaded, entry.Status);
        Assert.False(string.IsNullOrEmpty(entry.Message));
        Assert.Equal(0, result.Prepared);
    }

    [Fact]
    public void ProgressIsReportedWhileFontsArePrepared()
    {
        var source = FontFixtures.AnyWindowsFont();
        Assert.NotNull(source);

        foreach (var i in Enumerable.Range(0, 3))
        {
            File.Copy(source!, Path.Combine(_folder.FullName, $"Copy{i}.ttf"));
        }

        var progress = new List<FontPreparer.Progress>();
        FontPreparer.CheckFolder(
            _folder.FullName, new FontIndex(), progress.Add, null, CancellationToken.None);

        Assert.NotEmpty(progress);
        Assert.All(progress, p => Assert.Equal(3, p.Total));
        Assert.Equal(3, progress[^1].Done);
    }
}

public class ZipFontSourceTests
{
    [Fact]
    public void AZipBombIsRefused()
    {
        var dir = Directory.CreateTempSubdirectory("sp-bomb-");
        try
        {
            var path = Path.Combine(dir.FullName, "bomb.zip");

            using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                // Ten megabytes of zeros compresses to almost nothing, which is
                // exactly the shape of the attack.
                using var entry = zip.CreateEntry("huge.ttf", CompressionLevel.SmallestSize).Open();
                entry.Write(new byte[10 * 1024 * 1024]);
            }

            Assert.Throws<FileTooLargeException>(
                () => ZipFontSource.Read(path, CancellationToken.None));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void AZipInsideAZipIsNotFollowed()
    {
        var dir = Directory.CreateTempSubdirectory("sp-nested-");
        try
        {
            var inner = Path.Combine(dir.FullName, "inner.zip");
            using (var zip = ZipFile.Open(inner, ZipArchiveMode.Create))
            {
                using var entry = zip.CreateEntry("x.txt").Open();
                entry.Write("hello"u8);
            }

            var outer = Path.Combine(dir.FullName, "outer.zip");
            using (var zip = ZipFile.Open(outer, ZipArchiveMode.Create))
            {
                zip.CreateEntryFromFile(inner, "inner.zip");
            }

            Assert.Empty(ZipFontSource.Read(outer, CancellationToken.None));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }
}

public class SubstitutionsTests
{
    private static readonly HashSet<string> Windows =
        new(["Arial", "Times New Roman", "Courier New", "Calibri", "Cambria", "Verdana", "Consolas", "Segoe UI"],
            StringComparer.OrdinalIgnoreCase);

    [Theory]
    [InlineData("Helvetica", "Arial")]
    [InlineData("Helvetica Neue", "Arial")]
    [InlineData("Liberation Sans", "Arial")]
    [InlineData("Times", "Times New Roman")]
    [InlineData("Liberation Serif", "Times New Roman")]
    [InlineData("Courier", "Courier New")]
    [InlineData("Carlito", "Calibri")]
    [InlineData("Caladea", "Cambria")]
    [InlineData("DejaVu Sans", "Verdana")]
    [InlineData("Menlo", "Consolas")]
    [InlineData("Monaco", "Consolas")]
    [InlineData("San Francisco", "Segoe UI")]
    [InlineData("-apple-system", "Segoe UI")]
    public void TheBlueprintTableIsHonoured(string requested, string expected) =>
        Assert.Equal(expected, Substitutions.Resolve(requested, Windows));

    [Fact]
    public void AFontThatIsAvailableIsNeverSubstituted()
    {
        // The rule the whole feature rests on: a substitute is only for a font
        // that is actually missing.
        Assert.Null(Substitutions.Resolve("Arial", Windows));
        Assert.Null(Substitutions.Resolve("arial", Windows));
    }

    [Fact]
    public void AFontWithNoSubstituteGivesNull() =>
        Assert.Null(Substitutions.Resolve("Some Designer Font", Windows));

    [Fact]
    public void ACssFontStackIsTriedNameByName()
    {
        // Documents converted from HTML carry these, and the first name that
        // this PC can show is the right answer.
        Assert.Equal("Arial", Substitutions.Resolve("Helvetica Neue, Helvetica, Arial, sans-serif", Windows));
        Assert.Equal("Consolas", Substitutions.Resolve("'Fira Code', monospace", Windows));
    }

    [Fact]
    public void TheGenericCssFamiliesFallBackSensibly()
    {
        Assert.Equal("Arial", Substitutions.Resolve("sans-serif", Windows));
        Assert.Equal("Times New Roman", Substitutions.Resolve("serif", Windows));
        Assert.Equal("Consolas", Substitutions.Resolve("monospace", Windows));
    }

    [Fact]
    public void ASubstituteThatIsItselfMissingIsNotOffered() =>
        Assert.Null(Substitutions.Resolve("Helvetica", new HashSet<string>(StringComparer.OrdinalIgnoreCase)));
}

/// <summary>
/// FONT_LICENSES.txt (Section 6.6.6).
/// </summary>
public class FontLicensesTests
{
    [Fact]
    public void AnEmptyFontFolderSaysSoRatherThanProducingAnEmptyFile()
    {
        var dir = Directory.CreateTempSubdirectory("sp-lic-empty-");
        try
        {
            var text = FontLicenses.Generate(dir.FullName);

            Assert.Contains("No fonts are bundled", text, StringComparison.Ordinal);
            Assert.Contains("regenerated by build/build.ps1", text, StringComparison.Ordinal);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void AFontsLicenceNamesAreReadOutOfItsOwnNameTable()
    {
        var source = FontFixtures.AnyWindowsFont();
        Assert.NotNull(source);

        var dir = Directory.CreateTempSubdirectory("sp-lic-");
        try
        {
            File.Copy(source!, Path.Combine(dir.FullName, "Bundled.ttf"));
            File.WriteAllText(Path.Combine(dir.FullName, "OFL.txt"), "the licence text");

            var expected = FontInspector.Read(File.ReadAllBytes(source!));
            var text = FontLicenses.Generate(dir.FullName);

            Assert.Contains(expected.Families[0], text, StringComparison.Ordinal);
            Assert.Contains("Bundled.ttf", text, StringComparison.Ordinal);
            Assert.Contains("Embedding:", text, StringComparison.Ordinal);

            // A licence file sitting beside the fonts is listed too, so nobody
            // has to go looking for it.
            Assert.Contains("OFL.txt", text, StringComparison.Ordinal);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void AFontWithNoLicenceInformationIsFlaggedAtTheTop()
    {
        var source = FontFixtures.AnyWindowsFont();
        Assert.NotNull(source);

        var dir = Directory.CreateTempSubdirectory("sp-lic-flag-");
        try
        {
            // A Microsoft font's own copyright says "All rights reserved",
            // which is one of the phrases worth a second look before shipping
            // it inside a zip - exactly what the flag list is for.
            File.Copy(source!, Path.Combine(dir.FullName, "Questionable.ttf"));

            var text = FontLicenses.Generate(dir.FullName);
            var info = FontInspector.Read(File.ReadAllBytes(source!));

            var shouldFlag =
                (info.FsType & 0x0200) != 0
                || string.IsNullOrWhiteSpace(info.Copyright)
                || $"{info.Copyright} {info.LicenseDescription}"
                    .Contains("all rights reserved", StringComparison.OrdinalIgnoreCase);

            if (shouldFlag)
            {
                Assert.Contains("NEEDS A DECISION BEFORE SHIPPING", text, StringComparison.Ordinal);
            }

            // Flagging never blocks: the family is still described in full.
            Assert.Contains("Questionable.ttf", text, StringComparison.Ordinal);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }
}

/// <summary>
/// The Phase 3 formats through the folder pipeline (Section 6.6).
/// </summary>
public class Phase3PipelineTests : IDisposable
{
    private readonly DirectoryInfo _folder = Directory.CreateTempSubdirectory("sp-p3-");

    public Phase3PipelineTests() => AppPaths.Initialize();

    public void Dispose()
    {
        try
        {
            _folder.Delete(recursive: true);
        }
        catch (IOException)
        {
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void AType1FontInTheFolderIsConvertedAndIndexed()
    {
        var source = FontFixtures.Dropped(".pfa").FirstOrDefault();

        if (source is null)
        {
            return;        // see tests/Fixtures/Fonts/README.txt
        }

        var afm = Path.ChangeExtension(source, ".afm");

        File.Copy(source, Path.Combine(_folder.FullName, Path.GetFileName(source)));

        if (File.Exists(afm))
        {
            File.Copy(afm, Path.Combine(_folder.FullName, Path.GetFileName(afm)));
        }

        var index = new FontIndex();
        FontPreparer.CheckFolder(_folder.FullName, index, null, null, CancellationToken.None);

        var entry = index.Find(Path.GetFileName(source));

        Assert.NotNull(entry);
        Assert.True(entry!.IsUsable, $"the Type 1 font was {entry.Status}: {entry.Message}");
        Assert.Equal(FontStatus.Converted, entry.Status);
        Assert.Contains("Type 1", entry.Message, StringComparison.Ordinal);
        Assert.NotEmpty(entry.Families);

        // The cached file is a real OpenType font with CFF outlines.
        var cached = FontCache.PathFor(entry.Sha1, Path.GetExtension(entry.CachedFile));
        Assert.True(File.Exists(cached));

        var info = FontInspector.Read(File.ReadAllBytes(cached));
        Assert.True(info.IsCff);
        Assert.True(info.GlyphCount > 100);

        // Section 6.6.4: Type 1 has no embedding flag, so Preview and Print.
        Assert.Equal(0x0004, info.FsType);
    }

    [Fact]
    public void AnEotInTheFolderIsRecoveredAndIndexed()
    {
        var source = FontFixtures.Dropped(".eot").FirstOrDefault();

        if (source is null)
        {
            return;
        }

        File.Copy(source, Path.Combine(_folder.FullName, "Web.eot"));

        var index = new FontIndex();
        FontPreparer.CheckFolder(_folder.FullName, index, null, null, CancellationToken.None);

        var entry = index.Find("Web.eot");

        Assert.NotNull(entry);
        Assert.True(entry!.IsUsable, $"the EOT font was {entry.Status}: {entry.Message}");
        Assert.Contains("EOT", entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AMultipleMasterFontIsReportedInTheIndexRatherThanCrashingThePass()
    {
        var source = FontFixtures.Dropped(".PFB")
            .FirstOrDefault(p => Path.GetFileName(p).StartsWith("Z", StringComparison.OrdinalIgnoreCase));

        if (source is null)
        {
            return;
        }

        File.Copy(source, Path.Combine(_folder.FullName, "Master.pfb"));

        var plain = FontFixtures.AnyWindowsFont();
        Assert.NotNull(plain);
        File.Copy(plain!, Path.Combine(_folder.FullName, "Plain.ttf"));

        var index = new FontIndex();
        var result = FontPreparer.CheckFolder(
            _folder.FullName, index, null, null, CancellationToken.None);

        var entry = index.Find("Master.pfb");

        Assert.NotNull(entry);
        Assert.False(entry!.IsUsable);
        Assert.Contains("Multiple Master", entry.Message, StringComparison.Ordinal);

        // And the pass carried on: the ordinary font beside it still got done.
        Assert.True(index.Find("Plain.ttf")!.IsUsable);
        Assert.True(result.Prepared >= 1);
    }

    [Fact]
    public void AType1FontInsideAZipFindsItsMetricsFileInTheSameArchive()
    {
        var source = FontFixtures.Dropped(".pfa").FirstOrDefault();
        var afm = source is null ? null : Path.ChangeExtension(source, ".afm");

        if (source is null || afm is null || !File.Exists(afm))
        {
            return;
        }

        var zipPath = Path.Combine(_folder.FullName, "pack.zip");

        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            zip.CreateEntryFromFile(source, Path.GetFileName(source));
            zip.CreateEntryFromFile(afm, Path.GetFileName(afm));
        }

        var index = new FontIndex();
        FontPreparer.CheckFolder(_folder.FullName, index, null, null, CancellationToken.None);

        var entry = index.Find($"pack.zip!{Path.GetFileName(source)}");

        Assert.NotNull(entry);
        Assert.True(entry!.IsUsable, $"the zipped Type 1 font was {entry.Status}: {entry.Message}");

        // "Beside the font" means another entry of the same archive, which is
        // the whole reason ForEachMember hands over a sibling reader.
        Assert.NotEmpty(entry.Families);
    }
}

/// <summary>
/// What the Font Report says about a Type 1 font's metrics file
/// (Section 6.6.3).
/// </summary>
public class MetricsFileReportingTests : IDisposable
{
    private readonly DirectoryInfo _folder = Directory.CreateTempSubdirectory("sp-metrics-");

    public MetricsFileReportingTests() => AppPaths.Initialize();

    public void Dispose()
    {
        try
        {
            _folder.Delete(recursive: true);
        }
        catch (IOException)
        {
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void AMetricsFileWithItsFontBesideItIsNotReportedAsAProblem()
    {
        var source = FontFixtures.Dropped(".pfa").FirstOrDefault();
        var afm = source is null ? null : Path.ChangeExtension(source, ".afm");

        if (source is null || afm is null || !File.Exists(afm))
        {
            return;
        }

        File.Copy(source, Path.Combine(_folder.FullName, Path.GetFileName(source)));
        File.Copy(afm, Path.Combine(_folder.FullName, Path.GetFileName(afm)));

        var index = new FontIndex();
        FontPreparer.CheckFolder(_folder.FullName, index, null, null, CancellationToken.None);

        var entry = index.Find(Path.GetFileName(afm));

        Assert.NotNull(entry);

        // Telling someone the outline file is missing when it is sitting right
        // next to the metrics file sends them looking for what they have.
        Assert.DoesNotContain("missing", entry!.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(Path.GetFileName(source), entry.Message, StringComparison.Ordinal);
        Assert.Equal(FontStatus.Skipped, entry.Status);
    }

    [Fact]
    public void AMetricsFileOnItsOwnSaysTheOutlineIsMissing()
    {
        File.WriteAllText(
            Path.Combine(_folder.FullName, "Orphan.afm"),
            "StartFontMetrics 4.1\nFontName Orphan\nEndFontMetrics\n");

        var index = new FontIndex();
        FontPreparer.CheckFolder(_folder.FullName, index, null, null, CancellationToken.None);

        var entry = index.Find("Orphan.afm");

        Assert.NotNull(entry);
        Assert.Equal(FontStatus.Unsupported, entry!.Status);
        Assert.Contains("missing", entry.Message, StringComparison.OrdinalIgnoreCase);
    }
}
