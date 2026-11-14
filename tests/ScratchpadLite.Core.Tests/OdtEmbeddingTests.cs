using System.IO.Compression;
using System.Xml.Linq;
using ScratchpadLite.Core.Fonts;
using ScratchpadLite.Core.Formats;
using ScratchpadLite.Core.Formats.Odt;
using ScratchpadLite.Core.Model;
using Xunit;

using IrParagraph = ScratchpadLite.Core.Model.Paragraph;

namespace ScratchpadLite.Core.Tests;

/// <summary>
/// Fonts travelling inside an .odt (Section 7.4).
/// </summary>
/// <remarks>
/// ODT does not obfuscate its fonts: the file goes into the package as
/// itself, which the format decided and this program follows. What has to be
/// right is the bookkeeping around it - the declaration that points at the
/// file, and the manifest entry without which a reader does not believe the
/// file is in the package at all.
/// </remarks>
public class OdtEmbeddingTests
{
    private static readonly OdtFormat Format = new() { Styles = StyleSheet.Default };

    private static readonly XNamespace Office = "urn:oasis:names:tc:opendocument:xmlns:office:1.0";
    private static readonly XNamespace Style = "urn:oasis:names:tc:opendocument:xmlns:style:1.0";
    private static readonly XNamespace Svg =
        "urn:oasis:names:tc:opendocument:xmlns:svg-compatible:1.0";
    private static readonly XNamespace Xlink = "http://www.w3.org/1999/xlink";
    private static readonly XNamespace Manifest =
        "urn:oasis:names:tc:opendocument:xmlns:manifest:1.0";

    private static byte[] Font(int length = 300, byte seed = 7, string tag = "\0\u0001\0\0")
    {
        var bytes = new byte[length];

        for (var i = 0; i < tag.Length; i++)
        {
            bytes[i] = (byte)tag[i];
        }

        for (var i = 4; i < length; i++)
        {
            bytes[i] = (byte)((i * seed) % 251);
        }

        return bytes;
    }

    private static MemoryStream Save(params EmbeddedFont[] fonts)
    {
        var document = new SpDocument(
            [new IrParagraph { Inlines = [new TextRun("hello", new CharStyle { Family = "Nimbus Roman" })] }],
            new Dictionary<string, string>(),
            new PageSetup(),
            new HashSet<string> { "Nimbus Roman" });

        var file = new MemoryStream();

        Format.Write(document, file, "test.odt", CancellationToken.None, new WriteOptions(fonts));
        file.Position = 0;

        return file;
    }

    private static XDocument Part(Stream odt, string path)
    {
        var position = odt.Position;
        odt.Position = 0;

        using var zip = new ZipArchive(odt, ZipArchiveMode.Read, leaveOpen: true);
        using var stream = zip.GetEntry(path)!.Open();

        var document = XDocument.Load(stream);
        odt.Position = position;

        return document;
    }

    [Fact]
    public void TheFontFileIsInThePackageAsItself()
    {
        var font = Font();

        using var file = Save(new EmbeddedFont("Nimbus Roman", FontFace.Regular, font));

        using var zip = new ZipArchive(file, ZipArchiveMode.Read, leaveOpen: true);
        var entry = Assert.Single(
            zip.Entries, e => e.FullName.StartsWith("Fonts/", StringComparison.Ordinal));

        using var stream = entry.Open();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);

        Assert.Equal(font, buffer.ToArray());
    }

    [Fact]
    public void TheManifestListsTheFont()
    {
        // A file the manifest does not mention is not in the package as far
        // as a reader is concerned, however plainly it is sitting in the zip.
        using var file = Save(new EmbeddedFont("Nimbus Roman", FontFace.Regular, Font()));

        var entries = Part(file, "META-INF/manifest.xml").Root!
            .Elements(Manifest + "file-entry")
            .Select(e => e.Attribute(Manifest + "full-path")!.Value)
            .ToList();

        Assert.Contains(entries, p => p.StartsWith("Fonts/", StringComparison.Ordinal));
    }

    [Fact]
    public void TheRegularFaceIsDeclaredUnderTheFamilysOwnName()
    {
        // Every run in the document refers to the family through
        // style:font-name, so the regular face has to be declared under that
        // name or nothing points at the embedded file.
        using var file = Save(new EmbeddedFont("Nimbus Roman", FontFace.Regular, Font()));

        var declaration = Part(file, "content.xml").Root!
            .Element(Office + "font-face-decls")!
            .Elements(Style + "font-face")
            .Single(e => e.Attribute(Style + "name")?.Value == "Nimbus Roman");

        var uri = declaration
            .Element(Svg + "font-face-src")!
            .Element(Svg + "font-face-uri")!;

        Assert.Equal("Nimbus Roman", declaration.Attribute(Svg + "font-family")?.Value);
        Assert.StartsWith("Fonts/", uri.Attribute(Xlink + "href")!.Value, StringComparison.Ordinal);
        Assert.Equal("truetype", uri.Element(Svg + "font-face-format")?.Attribute(Svg + "string")?.Value);
    }

    [Fact]
    public void EachOtherFaceGetsItsOwnNameAndSaysWhatItIs()
    {
        using var file = Save(
            new EmbeddedFont("Nimbus Roman", FontFace.Regular, Font(seed: 3)),
            new EmbeddedFont("Nimbus Roman", FontFace.BoldItalic, Font(seed: 5)));

        var declarations = Part(file, "styles.xml").Root!
            .Element(Office + "font-face-decls")!
            .Elements(Style + "font-face")
            .Where(e => e.Attribute(Svg + "font-family")?.Value == "Nimbus Roman")
            .ToList();

        Assert.Equal(2, declarations.Count);

        var boldItalic = declarations.Single(e => e.Attribute(Svg + "font-weight")?.Value == "bold");

        Assert.Equal("Nimbus Roman BoldItalic", boldItalic.Attribute(Style + "name")?.Value);
        Assert.Equal("italic", boldItalic.Attribute(Svg + "font-style")?.Value);
    }

    [Fact]
    public void AnOpenTypeFontIsDeclaredAsOpenType()
    {
        using var file = Save(
            new EmbeddedFont("Nimbus Sans", FontFace.Regular, Font(tag: "OTTO")) { Extension = ".otf" });

        var format = Part(file, "content.xml").Root!
            .Element(Office + "font-face-decls")!
            .Elements(Style + "font-face")
            .Single(e => e.Attribute(Svg + "font-family")?.Value == "Nimbus Sans")
            .Element(Svg + "font-face-src")!
            .Element(Svg + "font-face-uri")!
            .Element(Svg + "font-face-format")!;

        Assert.Equal("opentype", format.Attribute(Svg + "string")?.Value);
    }

    [Fact]
    public void AnEmbeddedFontComesBackByteForByte()
    {
        var regular = Font(seed: 3);
        var bold = Font(length: 512, seed: 5);

        using var file = Save(
            new EmbeddedFont("Nimbus Roman", FontFace.Regular, regular),
            new EmbeddedFont("Nimbus Roman", FontFace.Bold, bold));

        var result = Format.Read(file, "test.odt", CancellationToken.None);

        Assert.Equal(2, result.EmbeddedFonts.Count);

        var back = result.EmbeddedFonts.ToDictionary(f => f.Face);

        Assert.Equal(regular, back[FontFace.Regular].Bytes);
        Assert.Equal(bold, back[FontFace.Bold].Bytes);
        Assert.All(result.EmbeddedFonts, f => Assert.Equal("Nimbus Roman", f.Family));
    }

    [Fact]
    public void AFontDeclaredInBothPartsIsReadOnce()
    {
        // This writer declares its fonts in content.xml and styles.xml both,
        // because a reader may look in either. They are the same file and
        // must not come back twice.
        using var file = Save(new EmbeddedFont("Nimbus Roman", FontFace.Regular, Font()));

        Assert.Single(Format.Read(file, "test.odt", CancellationToken.None).EmbeddedFonts);
    }

    [Fact]
    public void ADocumentWithNoEmbeddedFontsReadsBackWithNone()
    {
        using var file = Save();

        Assert.Empty(Format.Read(file, "test.odt", CancellationToken.None).EmbeddedFonts);
    }

    [Fact]
    public void AFontFileThatIsNotAFontIsLeftOutWithAWarning()
    {
        using var file = Save(new EmbeddedFont("Nimbus Roman", FontFace.Regular, Font()));

        using (var zip = new ZipArchive(file, ZipArchiveMode.Update, leaveOpen: true))
        {
            var entry = zip.Entries.First(e => e.FullName.StartsWith("Fonts/", StringComparison.Ordinal));
            using var stream = entry.Open();
            stream.SetLength(0);
            stream.Write(new byte[64]);
        }

        file.Position = 0;
        var result = Format.Read(file, "test.odt", CancellationToken.None);

        Assert.Empty(result.EmbeddedFonts);
        Assert.Contains(result.Warnings, w => w.Contains("not a font", StringComparison.Ordinal));
    }

    [Fact]
    public void ADeclarationPointingOutOfThePackageIsRefused()
    {
        // Section 7.5: the href comes from someone else's file, and
        // "../../../windows/fonts/arial.ttf" is a path, not a font.
        using var file = Save(new EmbeddedFont("Nimbus Roman", FontFace.Regular, Font()));

        var content = Part(file, "content.xml");
        var uri = content.Descendants(Svg + "font-face-uri").First();
        uri.Attribute(Xlink + "href")!.Value = "../../../Windows/Fonts/arial.ttf";

        using (var zip = new ZipArchive(file, ZipArchiveMode.Update, leaveOpen: true))
        {
            var entry = zip.GetEntry("content.xml")!;
            using var stream = entry.Open();
            stream.SetLength(0);
            content.Save(stream, SaveOptions.DisableFormatting);
        }

        file.Position = 0;
        var result = Format.Read(file, "test.odt", CancellationToken.None);

        // The one in styles.xml still points at the real file, so the font
        // comes back - but by its own path, not by the planted one.
        Assert.All(result.EmbeddedFonts, f => Assert.Equal("Nimbus Roman", f.Family));
        Assert.DoesNotContain(result.EmbeddedFonts, f => f.Bytes.Length == 0);
    }
}
