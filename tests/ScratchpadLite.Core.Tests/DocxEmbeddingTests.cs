using System.IO.Compression;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using DocumentFormat.OpenXml.Wordprocessing;
using ScratchpadLite.Core.Fonts;
using ScratchpadLite.Core.Formats;
using ScratchpadLite.Core.Formats.Docx;
using ScratchpadLite.Core.Model;
using Xunit;
using Xunit.Abstractions;

using IrParagraph = ScratchpadLite.Core.Model.Paragraph;

namespace ScratchpadLite.Core.Tests;

/// <summary>
/// Fonts travelling inside a .docx (Section 7.4).
/// </summary>
/// <remarks>
/// <para>The phase gate is a document opening in Word on another PC and
/// showing the font, which no test here can check. What these can check is
/// everything Word would refuse the file for: the obfuscation, the schema,
/// the order of the faces, and the settings flag without which Word drops
/// the fonts on its next save.</para>
///
/// <para>The font bytes are made up rather than real. What is being tested
/// is the packaging, and a made-up font makes the assertions exact - a byte
/// that changed is a byte this code changed.</para>
/// </remarks>
public class DocxEmbeddingTests(ITestOutputHelper output)
{
    private static readonly DocxFormat Format = new() { Styles = StyleSheet.Default };

    /// <summary>Something that passes for a TrueType file: the right tag, then noise.</summary>
    private static byte[] Font(int length = 300, byte seed = 7)
    {
        var bytes = new byte[length];
        bytes[0] = 0x00;
        bytes[1] = 0x01;
        bytes[2] = 0x00;
        bytes[3] = 0x00;

        for (var i = 4; i < length; i++)
        {
            bytes[i] = (byte)((i * seed) % 251);
        }

        return bytes;
    }

    private static SpDocument Document() =>
        new(
            [new IrParagraph { Inlines = [new TextRun("hello", new CharStyle { Family = "Nimbus Roman" })] }],
            new Dictionary<string, string>(),
            new PageSetup(),
            new HashSet<string> { "Nimbus Roman" });

    private static MemoryStream Save(params EmbeddedFont[] fonts)
    {
        var file = new MemoryStream();

        Format.Write(
            Document(), file, "test.docx", CancellationToken.None, new WriteOptions(fonts));

        file.Position = 0;

        return file;
    }

    private void Validate(Stream file)
    {
        var position = file.Position;
        file.Position = 0;

        using (var package = WordprocessingDocument.Open(file, isEditable: false))
        {
            var errors = new OpenXmlValidator().Validate(package).ToList();

            foreach (var error in errors)
            {
                output.WriteLine($"{error.ErrorType} at {error.Path?.XPath}: {error.Description}");
            }

            Assert.Empty(errors);
        }

        file.Position = position;
    }

    // ------------------------------------------------------ obfuscation ----

    [Fact]
    public void ObfuscationChangesTheFirstThirtyTwoBytesAndNothingElse()
    {
        var original = Font();
        var key = Guid.NewGuid();

        var scrambled = DocxFontEmbedder.Obfuscate(original, key);

        Assert.Equal(original.Length, scrambled.Length);
        Assert.False(
            original.AsSpan(0, 32).SequenceEqual(scrambled.AsSpan(0, 32)),
            "the first 32 bytes should have been scrambled");
        Assert.True(
            original.AsSpan(32).SequenceEqual(scrambled.AsSpan(32)),
            "everything past the 32nd byte should be untouched");
    }

    [Fact]
    public void ObfuscationIsItsOwnInverse()
    {
        var original = Font();
        var key = Guid.NewGuid();

        Assert.Equal(original, DocxFontEmbedder.Deobfuscate(DocxFontEmbedder.Obfuscate(original, key), key));
    }

    [Fact]
    public void TheWrongKeyGivesBackSomethingThatIsNotAFont()
    {
        var scrambled = DocxFontEmbedder.Obfuscate(Font(), Guid.NewGuid());

        var wrong = DocxFontEmbedder.Deobfuscate(scrambled, Guid.NewGuid());

        Assert.NotEqual(Font(), wrong);
    }

    [Fact]
    public void AFontShorterThanTheScrambledPrefixIsHandled()
    {
        // Not a real font, but not a crash either: a file this short is
        // already damaged and the reader's own check will refuse it.
        var tiny = new byte[8];
        var key = Guid.NewGuid();

        var scrambled = DocxFontEmbedder.Obfuscate(tiny, key);

        Assert.Equal(8, scrambled.Length);
        Assert.Equal(tiny, DocxFontEmbedder.Deobfuscate(scrambled, key));
    }

    [Fact]
    public void TheKeyIsTheGuidsBigEndianBytesReversed()
    {
        // Measured against Microsoft's own XpsFont.ObfuscateFontData; the
        // test that holds the two together lives in the App suite, because
        // that implementation is in WPF and this project's Core cannot
        // reference it. This is the same answer written down, so a change
        // here fails here rather than only over there.
        var key = DocxFontEmbedder.KeyBytes(new Guid("AABBCCDD-EEFF-0011-2233-445566778899"));

        Assert.Equal(
            Convert.FromHexString("99887766554433221100FFEEDDCCBBAA"),
            key);
    }

    // --------------------------------------------------------- the file ----

    [Fact]
    public void ADocumentWithNoEmbeddedFontsHasNoFontTable()
    {
        using var file = Save();

        Validate(file);

        using var package = WordprocessingDocument.Open(file, isEditable: false);

        Assert.Null(package.MainDocumentPart!.FontTablePart);
    }

    [Fact]
    public void AnEmbeddedFaceBecomesAnObfuscatedPartInTheFontTable()
    {
        var font = Font();

        using var file = Save(new EmbeddedFont("Nimbus Roman", FontFace.Regular, font));

        Validate(file);

        using var package = WordprocessingDocument.Open(file, isEditable: false);
        var table = package.MainDocumentPart!.FontTablePart;

        Assert.NotNull(table);

        var part = Assert.Single(table!.FontParts);
        Assert.Equal(
            "application/vnd.openxmlformats-officedocument.obfuscatedFont",
            part.ContentType);
        Assert.EndsWith(".odttf", part.Uri.ToString(), StringComparison.Ordinal);

        using var stored = part.GetStream(FileMode.Open, FileAccess.Read);
        using var buffer = new MemoryStream();
        stored.CopyTo(buffer);

        // What is in the package is not the font: that is the whole point of
        // obfuscating it.
        Assert.NotEqual(font, buffer.ToArray());
        Assert.Equal(font.Length, buffer.ToArray().Length);
    }

    [Fact]
    public void TheFontKeyInTheXmlUnscramblesThePart()
    {
        var font = Font();

        using var file = Save(new EmbeddedFont("Nimbus Roman", FontFace.Regular, font));

        using var package = WordprocessingDocument.Open(file, isEditable: false);
        var table = package.MainDocumentPart!.FontTablePart!;

        var entry = Assert.Single(table.Fonts!.Elements<Font>());
        var reference = Assert.Single(entry.Elements<EmbedRegularFont>());

        Assert.Equal("Nimbus Roman", entry.Name?.Value);
        Assert.False(reference.Subsetted?.Value, "the whole font is embedded, not a subset");

        // Word writes the key in braces and upper case, and its reader is
        // what has to parse this.
        var written = reference.FontKey!.Value!;
        Assert.StartsWith("{", written, StringComparison.Ordinal);
        Assert.EndsWith("}", written, StringComparison.Ordinal);
        Assert.Equal(written.ToUpperInvariant(), written);

        var part = (FontPart)table.GetPartById(reference.Id!.Value!);
        using var stored = part.GetStream(FileMode.Open, FileAccess.Read);
        using var buffer = new MemoryStream();
        stored.CopyTo(buffer);

        Assert.Equal(font, DocxFontEmbedder.Deobfuscate(buffer.ToArray(), Guid.Parse(written)));
    }

    [Fact]
    public void SettingsSayTheFontsAreEmbeddedSoWordKeepsThem()
    {
        // Without this, Word opens the document, finds no instruction to
        // embed, and drops every font part the next time it saves.
        using var file = Save(new EmbeddedFont("Nimbus Roman", FontFace.Regular, Font()));

        using var package = WordprocessingDocument.Open(file, isEditable: false);
        var settings = package.MainDocumentPart!.DocumentSettingsPart;

        Assert.NotNull(settings);
        Assert.NotNull(settings!.Settings!.GetFirstChild<EmbedTrueTypeFonts>());
    }

    [Fact]
    public void TheFourFacesAreWrittenInTheOrderTheSchemaDemands()
    {
        // w:font's children are a sequence, not a set: regular, bold, italic,
        // bold italic. Handed over in the wrong order, Word calls the file
        // corrupt.
        using var file = Save(
            new EmbeddedFont("Nimbus Roman", FontFace.BoldItalic, Font(seed: 3)),
            new EmbeddedFont("Nimbus Roman", FontFace.Italic, Font(seed: 5)),
            new EmbeddedFont("Nimbus Roman", FontFace.Bold, Font(seed: 11)),
            new EmbeddedFont("Nimbus Roman", FontFace.Regular, Font(seed: 13)));

        Validate(file);

        using var package = WordprocessingDocument.Open(file, isEditable: false);
        var entry = Assert.Single(package.MainDocumentPart!.FontTablePart!.Fonts!.Elements<Font>());

        Assert.Equal(
            ["embedRegular", "embedBold", "embedItalic", "embedBoldItalic"],
            entry.ChildElements.Select(c => c.LocalName).ToArray());
    }

    [Fact]
    public void TwoFamiliesGetTwoEntriesAndFourParts()
    {
        using var file = Save(
            new EmbeddedFont("Nimbus Roman", FontFace.Regular, Font(seed: 3)),
            new EmbeddedFont("Nimbus Roman", FontFace.Bold, Font(seed: 5)),
            new EmbeddedFont("Nimbus Sans", FontFace.Regular, Font(seed: 7)),
            new EmbeddedFont("Nimbus Sans", FontFace.Bold, Font(seed: 11)));

        Validate(file);

        using var package = WordprocessingDocument.Open(file, isEditable: false);
        var table = package.MainDocumentPart!.FontTablePart!;

        Assert.Equal(2, table.Fonts!.Elements<Font>().Count());
        Assert.Equal(4, table.FontParts.Count());

        // A key per face, not one for the document: a part lifted out is not
        // unscrambled by the key beside a different one.
        var keys = table.Fonts.Elements<Font>()
            .SelectMany(f => f.ChildElements.OfType<FontRelationshipType>())
            .Select(r => r.FontKey!.Value!)
            .ToList();

        Assert.Equal(4, keys.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    // ------------------------------------------------------- round trip ----

    [Fact]
    public void AnEmbeddedFontComesBackByteForByte()
    {
        var regular = Font(seed: 3);
        var bold = Font(length: 512, seed: 5);

        using var file = Save(
            new EmbeddedFont("Nimbus Roman", FontFace.Regular, regular),
            new EmbeddedFont("Nimbus Roman", FontFace.Bold, bold));

        var result = Format.Read(file, "test.docx", CancellationToken.None);

        Assert.Equal(2, result.EmbeddedFonts.Count);

        var back = result.EmbeddedFonts.ToDictionary(f => f.Face);

        Assert.Equal(regular, back[FontFace.Regular].Bytes);
        Assert.Equal(bold, back[FontFace.Bold].Bytes);
        Assert.All(result.EmbeddedFonts, f => Assert.Equal("Nimbus Roman", f.Family));
    }

    [Fact]
    public void ADocumentWithNoEmbeddedFontsReadsBackWithNone()
    {
        using var file = Save();

        Assert.Empty(Format.Read(file, "test.docx", CancellationToken.None).EmbeddedFonts);
    }

    [Fact]
    public void AFaceWhoseBytesAreDamagedIsLeftOutWithAWarning()
    {
        // The font part is replaced with rubbish after the fact, which is
        // what a truncated download or a hand-edited package looks like.
        using var file = Save(new EmbeddedFont("Nimbus Roman", FontFace.Regular, Font()));

        using (var zip = new ZipArchive(file, ZipArchiveMode.Update, leaveOpen: true))
        {
            var entry = zip.Entries.First(e => e.FullName.EndsWith(".odttf", StringComparison.Ordinal));
            using var stream = entry.Open();
            stream.SetLength(0);
            stream.Write(new byte[64]);
        }

        file.Position = 0;
        var result = Format.Read(file, "test.docx", CancellationToken.None);

        Assert.Empty(result.EmbeddedFonts);
        Assert.Contains(result.Warnings, w => w.Contains("unscrambled", StringComparison.Ordinal));
    }

    [Fact]
    public void TheDocumentItselfStillReadsWhenItsFontsDoNot()
    {
        using var file = Save(new EmbeddedFont("Nimbus Roman", FontFace.Regular, Font()));

        using (var zip = new ZipArchive(file, ZipArchiveMode.Update, leaveOpen: true))
        {
            var entry = zip.Entries.First(e => e.FullName.EndsWith(".odttf", StringComparison.Ordinal));
            using var stream = entry.Open();
            stream.SetLength(0);
        }

        file.Position = 0;
        var result = Format.Read(file, "test.docx", CancellationToken.None);

        // A font that will not load must not cost the user their text.
        Assert.Equal("hello", Assert.IsType<IrParagraph>(result.Document.Blocks[0]).Inlines
            .OfType<TextRun>().Single().Text);
    }
}
