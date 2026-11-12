using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;
using ScratchpadLite.Core.Formats;
using ScratchpadLite.Core.Formats.Odt;
using ScratchpadLite.Core.Model;
using Xunit;
using Xunit.Abstractions;

namespace ScratchpadLite.Core.Tests;

/// <summary>
/// OpenDocument text (Section 7.2), checked the same two ways as .docx.
/// </summary>
/// <remarks>
/// There is no OpenXmlValidator for ODT, so the structural half of the check is
/// written out by hand here: the zip rules LibreOffice enforces, every part
/// being well-formed XML, and every style a paragraph or run names actually
/// existing. Those three are what a repair prompt in LibreOffice usually means.
/// <para>
/// The namespace URIs below are spelled out rather than taken from
/// <c>OdtXml</c> on purpose. A test that uses the writer's own constants cannot
/// catch a wrong namespace, because it would be wrong in both places at once.
/// </para>
/// </remarks>
public class OdtTests(ITestOutputHelper output)
{
    private const string OfficeNs = "urn:oasis:names:tc:opendocument:xmlns:office:1.0";
    private const string TextNs = "urn:oasis:names:tc:opendocument:xmlns:text:1.0";
    private const string StyleNs = "urn:oasis:names:tc:opendocument:xmlns:style:1.0";
    private const string TableNs = "urn:oasis:names:tc:opendocument:xmlns:table:1.0";
    private const string ManifestNs = "urn:oasis:names:tc:opendocument:xmlns:manifest:1.0";

    private const string Mimetype = "application/vnd.oasis.opendocument.text";

    private static readonly OdtFormat Format = new() { Styles = StyleSheet.Default };

    // --- the round trip ----------------------------------------------------

    private SpDocument RoundTrip(SpDocument document)
    {
        using var file = Written(document);

        CheckTheZipRules(file);

        file.Position = 0;
        var result = Format.Read(file, "test.odt", CancellationToken.None);

        foreach (var warning in result.Warnings)
        {
            output.WriteLine("warning: " + warning);
        }

        return result.Document;
    }

    private void Same(SpDocument before, SpDocument after)
    {
        var empty = new HashSet<string>();

        var expected = IrNormalizer.Normalize(before) with { FontsUsed = empty };
        var actual = IrNormalizer.Normalize(after) with { FontsUsed = empty };

        if (!IrComparer.Exactly(expected, actual))
        {
            output.WriteLine("=== written ===");
            output.WriteLine(IrNormalizer.Describe(expected));
            output.WriteLine("=== read back ===");
            output.WriteLine(IrNormalizer.Describe(actual));

            Assert.Fail("the document changed on the way through an .odt; see the output above");
        }
    }

    [Fact]
    public void AnEmptyDocumentIsWellFormed()
    {
        using var file = Written(SpDocument.Empty);

        CheckTheZipRules(file);
    }

    [Fact]
    public void PlainTextSurvives()
    {
        var document = Document(Text("Hello, world."));

        Same(document, RoundTrip(document));
    }

    [Theory]
    [InlineData("Normal")]
    [InlineData("Title")]
    [InlineData("Subtitle")]
    [InlineData("Heading 1")]
    [InlineData("Heading 3")]
    [InlineData("Heading 6")]
    [InlineData("Quote")]
    [InlineData("Code")]
    public void EveryParagraphStyleSurvivesByName(string styleName)
    {
        var document = Document(Text("text", styleName: styleName));

        Same(document, RoundTrip(document));
    }

    [Fact]
    public void CharacterFormattingSurvives()
    {
        var document = Document(new Paragraph
        {
            Inlines =
            [
                new TextRun("bold", new CharStyle { Bold = true }),
                new TextRun("italic", new CharStyle { Italic = true }),
                new TextRun("struck", new CharStyle { Strike = true }),
                new TextRun("under", new CharStyle { Underline = UnderlineKind.Single }),
            ],
        });

        Same(document, RoundTrip(document));
    }

    [Theory]
    [InlineData(UnderlineKind.Single)]
    [InlineData(UnderlineKind.Double)]
    [InlineData(UnderlineKind.Dotted)]
    [InlineData(UnderlineKind.Dashed)]
    [InlineData(UnderlineKind.Wavy)]
    public void EveryUnderlineKindSurvives(UnderlineKind kind)
    {
        var document = Document(Text("text", new CharStyle { Underline = kind }));

        Same(document, RoundTrip(document));
    }

    [Theory]
    [InlineData(VAlign.Super)]
    [InlineData(VAlign.Sub)]
    public void SuperscriptAndSubscriptSurvive(VAlign vertical)
    {
        var document = Document(Text("x", new CharStyle { Vertical = vertical }));

        Same(document, RoundTrip(document));
    }

    [Fact]
    public void FontsSizesAndColoursSurvive()
    {
        var document = Document(Text(
            "text",
            new CharStyle
            {
                Family = "Georgia",
                SizePt = 13.5,
                Color = "#C00000",
                Highlight = "#FFFF00",
            }));

        Same(document, RoundTrip(document));
    }

    [Fact]
    public void AlignmentIndentsAndSpacingSurvive()
    {
        var document = Document(
            Text("centred") with { Align = Align.Center },
            Text("justified") with { Align = Align.Justify },
            Text("indented") with
            {
                IndentLeftPt = 36,
                IndentRightPt = 18,
                SpaceBeforePt = 12,
                SpaceAfterPt = 6,
                LineSpacing = 1.5,
            });

        Same(document, RoundTrip(document));
    }

    [Fact]
    public void APageBreakSurvives()
    {
        var document = Document(Text("one"), Text("two") with { PageBreakBefore = true });

        Same(document, RoundTrip(document));
    }

    [Fact]
    public void ListsSurvive()
    {
        var document = Document(
            Text("one") with { List = new ListInfo(ListKind.Bullet, 0) },
            Text("nested") with { List = new ListInfo(ListKind.Bullet, 1) },
            Text("two") with { List = new ListInfo(ListKind.Bullet, 0) });

        Same(document, RoundTrip(document));
    }

    [Theory]
    [InlineData("decimal")]
    [InlineData("lowerLetter")]
    [InlineData("upperLetter")]
    [InlineData("lowerRoman")]
    [InlineData("upperRoman")]
    public void EveryNumberFormatSurvives(string format)
    {
        var document = Document(
            Text("item") with { List = new ListInfo(ListKind.Number, 0, format) });

        Same(document, RoundTrip(document));
    }

    [Fact]
    public void ANumberedListThatStartsPartWayThroughSurvives()
    {
        var document = Document(
            Text("seven") with { List = new ListInfo(ListKind.Number, 0, "decimal", 7) });

        Same(document, RoundTrip(document));
    }

    [Fact]
    public void ABulletedListFollowedByANumberedOneStaysTwoLists()
    {
        var document = Document(
            Text("bullet") with { List = new ListInfo(ListKind.Bullet, 0) },
            Text("number") with { List = new ListInfo(ListKind.Number, 0) });

        Same(document, RoundTrip(document));
    }

    [Fact]
    public void ATableSurvives()
    {
        var document = Document(new Table(
            [
                [new Cell([Text("a")]), new Cell([Text("b")])],
                [new Cell([Text("c")]), new Cell([Text("d")])],
            ],
            ColWidthsPt: [120, 80]));

        var table = Assert.IsType<Table>(RoundTrip(document).Blocks[0]);

        Assert.Equal(2, table.Rows.Count);
        Assert.Equal("a", FirstText(table.Rows[0][0]));
        Assert.Equal("d", FirstText(table.Rows[1][1]));
        Assert.NotNull(table.ColWidthsPt);
        Assert.Equal(120, table.ColWidthsPt![0], 1);
    }

    [Fact]
    public void ATableWithSpansAndShadingSurvives()
    {
        var document = Document(new Table(
            [
                [new Cell([Text("wide")], ColSpan: 2, Shading: "#EEEEEE")],
                [new Cell([Text("tall")], RowSpan: 2), new Cell([Text("x")])],
                [new Cell([Text("y")])],
            ],
            ColWidthsPt: null));

        var table = Assert.IsType<Table>(RoundTrip(document).Blocks[0]);

        Assert.Equal(2, table.Rows[0][0].ColSpan);
        Assert.Equal("#EEEEEE", table.Rows[0][0].Shading);
        Assert.Equal(2, table.Rows[1][0].RowSpan);

        // ODT writes a covered-table-cell for the rest of a vertical merge, and
        // the reader must not hand it back as a cell.
        Assert.Single(table.Rows[2]);
    }

    [Fact]
    public void AnImageSurvivesWithItsExactBytes()
    {
        var png = MinimalPng();
        var document = Document(new Paragraph
        {
            Inlines = [new ImageRun(png, "image/png", 48, 24, "a red dot")],
        });

        var paragraph = Assert.IsType<Paragraph>(RoundTrip(document).Blocks[0]);
        var image = Assert.IsType<ImageRun>(paragraph.Inlines[0]);

        Assert.Equal(png, image.Data);
        Assert.Equal("image/png", image.Mime);
        Assert.Equal(48, image.WidthPt, 1);
        Assert.Equal(24, image.HeightPt, 1);
        Assert.Equal("a red dot", image.Alt);
    }

    [Fact]
    public void AnImageIsListedInTheManifest()
    {
        // LibreOffice will open a file whose picture is missing from the
        // manifest, and show a blank frame instead of the picture.
        var document = Document(new Paragraph
        {
            Inlines = [new ImageRun(MinimalPng(), "image/png", 32, 32)],
        });

        using var file = Written(document);
        using var zip = new ZipArchive(file, ZipArchiveMode.Read);

        var picture = Assert.Single(
            zip.Entries,
            e => e.FullName.StartsWith("Pictures/", StringComparison.Ordinal));
        var manifest = Xml(zip, "META-INF/manifest.xml");

        var listed = manifest.Root!
            .Elements(XName.Get("file-entry", ManifestNs))
            .Select(e => e.Attribute(XName.Get("full-path", ManifestNs))?.Value)
            .ToList();

        Assert.Contains(picture.FullName, listed);
        Assert.Contains("/", listed);
        Assert.Contains("content.xml", listed);
    }

    [Fact]
    public void ALinkSurvives()
    {
        var document = Document(Text(
            "a link",
            new CharStyle { Link = "https://example.com/page" }));

        var paragraph = Assert.IsType<Paragraph>(RoundTrip(document).Blocks[0]);
        var run = Assert.IsType<TextRun>(paragraph.Inlines[0]);

        Assert.Equal(
            IrNormalizer.NormalizeLink("https://example.com/page"),
            run.Style.Link);
    }

    [Fact]
    public void ALineBreakSurvives()
    {
        var document = Document(new Paragraph
        {
            Inlines =
            [
                new TextRun("before", new CharStyle()),
                new LineBreak(),
                new TextRun("after", new CharStyle()),
            ],
        });

        Same(document, RoundTrip(document));
    }

    [Fact]
    public void AHorizontalRuleSurvives()
    {
        var document = Document(Text("above"), new HorizontalRule(), Text("below"));

        Same(document, RoundTrip(document));
    }

    [Fact]
    public void PageSetupAndMetadataSurvive()
    {
        var document = new SpDocument(
            [Text("content")],
            new Dictionary<string, string> { ["title"] = "A Title", ["author"] = "Someone" },
            new PageSetup("A4", Landscape: true, 36, 36, 54, 54),
            new HashSet<string>());

        var after = RoundTrip(document);

        Assert.Equal("A4", after.Page.Paper);
        Assert.True(after.Page.Landscape);
        Assert.Equal(36, after.Page.MarginTopPt, 1);
        Assert.Equal(54, after.Page.MarginLeftPt, 1);
        Assert.Equal("A Title", after.Meta["title"]);
        Assert.Equal("Someone", after.Meta["author"]);
    }

    [Fact]
    public void RunsOfSpacesAreNotCollapsed()
    {
        // ODT folds whitespace like HTML does, so a run of spaces has to go out
        // as <text:s text:c="n"/> or the words move.
        var document = Document(Text("one     two"));

        // On the way back the spaces arrive as their own run, so the text is
        // compared after the normalizer has merged them again.
        Assert.Equal("one     two", AllText(RoundTrip(document)));
    }

    [Fact]
    public void ATabSurvives()
    {
        var document = Document(Text("before	after"));

        Assert.Equal("before	after", AllText(RoundTrip(document)));
    }

    [Fact]
    public void EverythingAtOnceSurvives()
    {
        var document = new SpDocument(
            [
                Text("The Title", styleName: "Title"),
                Text("Heading", styleName: "Heading 1"),
                new Paragraph
                {
                    Inlines =
                    [
                        new TextRun("Mixed ", new CharStyle()),
                        new TextRun("bold", new CharStyle { Bold = true }),
                        new TextRun(", ", new CharStyle()),
                        new TextRun("wavy", new CharStyle { Underline = UnderlineKind.Wavy }),
                        new TextRun(", ", new CharStyle()),
                        new TextRun("x", new CharStyle { Vertical = VAlign.Super }),
                        new TextRun(" and a ", new CharStyle()),
                        new TextRun("link", new CharStyle { Link = "https://example.com" }),
                        new TextRun(".", new CharStyle()),
                    ],
                },
                Text("quoted", styleName: "Quote"),
                Text("bullet") with { List = new ListInfo(ListKind.Bullet, 0) },
                Text("numbered") with { List = new ListInfo(ListKind.Number, 0) },
                new HorizontalRule(),
                new Table(
                    [[new Cell([Text("one")]), new Cell([Text("two")])]],
                    ColWidthsPt: [100, 100]),
                new Paragraph { Inlines = [new ImageRun(MinimalPng(), "image/png", 32, 32)] },
                Text("The end.") with { Align = Align.Center },
            ],
            new Dictionary<string, string> { ["title"] = "Everything" },
            new PageSetup(),
            new HashSet<string>());

        var after = RoundTrip(document);

        Assert.Equal("Title", ((Paragraph)after.Blocks[0]).StyleName);
        Assert.Equal("Heading 1", ((Paragraph)after.Blocks[1]).StyleName);

        var mixed = (Paragraph)after.Blocks[2];
        Assert.True(((TextRun)mixed.Inlines[1]).Style.Bold);
        Assert.Equal(UnderlineKind.Wavy, ((TextRun)mixed.Inlines[3]).Style.Underline);
        Assert.Equal(VAlign.Super, ((TextRun)mixed.Inlines[5]).Style.Vertical);

        Assert.Equal("Everything", after.Meta["title"]);
        Assert.Contains(after.Blocks, b => b is HorizontalRule);
        Assert.Contains(after.Blocks, b => b is Table);
        Assert.Contains(after.Blocks, b => b is Paragraph { Inlines: [ImageRun, ..] });
    }

    // --- the structural rules ----------------------------------------------

    [Fact]
    public void TheMimetypeEntryIsFirstAndStored()
    {
        using var file = Written(Document(Text("anything")));
        using var zip = new ZipArchive(file, ZipArchiveMode.Read);

        // Both halves matter. LibreOffice will open a file whose mimetype entry
        // is compressed, but a desktop that sniffs the file by reading the
        // fixed offset the spec promises will not know what it is.
        Assert.Equal("mimetype", zip.Entries[0].FullName);
        Assert.Equal(zip.Entries[0].Length, zip.Entries[0].CompressedLength);

        using var reader = new StreamReader(zip.Entries[0].Open());

        Assert.Equal(Mimetype, reader.ReadToEnd());
    }

    [Fact]
    public void TheMimetypeIsAtTheOffsetTheSpecPromises()
    {
        // The strongest form of the rule, and the reason for the one above:
        // the media type is meant to be readable from byte 38 of the file
        // without unzipping anything.
        using var file = Written(Document(Text("anything")));
        var bytes = file.ToArray();

        Assert.Equal(Mimetype, System.Text.Encoding.ASCII.GetString(bytes, 38, Mimetype.Length));
    }

    [Fact]
    public void EveryPartIsPresentAndWellFormed()
    {
        using var file = Written(Document(Text("anything")));
        using var zip = new ZipArchive(file, ZipArchiveMode.Read);

        foreach (var required in new[]
                 {
                     "META-INF/manifest.xml", "content.xml", "styles.xml", "meta.xml",
                 })
        {
            Assert.NotNull(zip.GetEntry(required));

            // Throws on anything malformed, which is the check.
            var xml = Xml(zip, required);

            Assert.NotNull(xml.Root);
        }
    }

    [Fact]
    public void EveryStyleAParagraphOrRunNamesExists()
    {
        // A dangling style name is the other common cause of a repair prompt:
        // the file parses, and then the name resolves to nothing.
        var document = new SpDocument(
            [
                Text("The Title", styleName: "Title"),
                Text("Heading", styleName: "Heading 2"),
                Text("centred", new CharStyle { Bold = true, Family = "Georgia", SizePt = 14 })
                    with { Align = Align.Center, SpaceBeforePt = 18 },
                Text("bullet") with { List = new ListInfo(ListKind.Bullet, 0) },
                Text("numbered") with { List = new ListInfo(ListKind.Number, 1, "lowerRoman") },
                new Table(
                    [[new Cell([Text("a")], Shading: "#EEEEEE"), new Cell([Text("b")])]],
                    ColWidthsPt: [90, 90]),
                new Paragraph { Inlines = [new ImageRun(MinimalPng(), "image/png", 20, 20)] },
            ],
            new Dictionary<string, string>(),
            new PageSetup(),
            new HashSet<string>());

        using var file = Written(document);
        using var zip = new ZipArchive(file, ZipArchiveMode.Read);

        var content = Xml(zip, "content.xml");
        var styles = Xml(zip, "styles.xml");

        var declared = Declared(content).Concat(Declared(styles)).ToHashSet(StringComparer.Ordinal);

        output.WriteLine($"{declared.Count} styles declared");

        var referenced = content.Descendants()
            .SelectMany(e => e.Attributes())
            .Where(a => a.Name.NamespaceName is TextNs or TableNs or StyleNs
                        && a.Name.LocalName is "style-name" or "text-style-name"
                            or "default-cell-style-name" or "parent-style-name")
            .Select(a => a.Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Assert.NotEmpty(referenced);

        foreach (var name in referenced)
        {
            Assert.True(declared.Contains(name), $"content.xml names a style that no part declares: {name}");
        }

        // And the lists point at a list style that exists too.
        var listStyles = content.Descendants(XName.Get("list", TextNs))
            .Select(e => e.Attribute(XName.Get("style-name", TextNs))?.Value)
            .Where(n => n is not null)
            .Distinct(StringComparer.Ordinal);

        foreach (var name in listStyles)
        {
            Assert.True(declared.Contains(name!), $"a list names a style that no part declares: {name}");
        }
    }

    [Fact]
    public void NoPartDeclaresTheSameStyleTwice()
    {
        // Two automatic styles with one name is how a de-duplicating style
        // table goes wrong, and LibreOffice silently keeps whichever it read
        // last.
        var document = Document(
            Text("one", new CharStyle { Bold = true }),
            Text("two", new CharStyle { Bold = true }),
            Text("three", new CharStyle { Italic = true }));

        using var file = Written(document);
        using var zip = new ZipArchive(file, ZipArchiveMode.Read);

        foreach (var part in new[] { "content.xml", "styles.xml" })
        {
            var names = Declared(Xml(zip, part)).ToList();

            Assert.Equal(names.Count, names.Distinct(StringComparer.Ordinal).Count());
        }
    }

    [Fact]
    public void TwoRunsWithTheSameFormattingShareOneStyle()
    {
        // Not a correctness rule, a size one: a page of bold text should not
        // produce a page of identical style definitions.
        var document = Document(
            Text("one", new CharStyle { Bold = true }),
            Text("two", new CharStyle { Bold = true }),
            Text("three", new CharStyle { Bold = true }));

        using var file = Written(document);
        using var zip = new ZipArchive(file, ZipArchiveMode.Read);

        var content = Xml(zip, "content.xml");

        var textStyles = content.Descendants(XName.Get("style", StyleNs))
            .Count(e => e.Attribute(XName.Get("family", StyleNs))?.Value == "text");

        Assert.Equal(1, textStyles);
    }

    [Fact]
    public void SomethingThatIsNotAnOdtIsRefusedRatherThanCrashing()
    {
        using var notAnOdt = new MemoryStream("this is not a zip at all"u8.ToArray());

        Assert.ThrowsAny<Exception>(
            () => Format.Read(notAnOdt, "broken.odt", CancellationToken.None));
    }

    [Fact]
    public void AZipThatIsNotAnOdtIsRefusedRatherThanCrashing()
    {
        using var file = new MemoryStream();

        using (var zip = new ZipArchive(file, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var writer = new StreamWriter(zip.CreateEntry("hello.txt").Open());
            writer.Write("not a document");
        }

        file.Position = 0;

        Assert.ThrowsAny<Exception>(() => Format.Read(file, "broken.odt", CancellationToken.None));
    }

    // --- helpers ------------------------------------------------------------

    /// <summary>
    /// The zip-level rules, checked on every file any test in here writes
    /// rather than only in the tests named after them.
    /// </summary>
    private void CheckTheZipRules(MemoryStream file)
    {
        file.Position = 0;

        using var zip = new ZipArchive(file, ZipArchiveMode.Read, leaveOpen: true);

        Assert.Equal("mimetype", zip.Entries[0].FullName);
        Assert.Equal(zip.Entries[0].Length, zip.Entries[0].CompressedLength);
        Assert.NotNull(zip.GetEntry("content.xml"));

        file.Position = 0;
    }

    /// <summary>All the text of a document, runs joined back together.</summary>
    private static string AllText(SpDocument document) =>
        string.Concat(
            IrNormalizer.Normalize(document).Blocks
                .OfType<Paragraph>()
                .SelectMany(p => p.Inlines)
                .OfType<TextRun>()
                .Select(r => r.Text));

    private static IEnumerable<string> Declared(XDocument part) =>
        part.Descendants()
            .Where(e => e.Name == XName.Get("style", StyleNs)
                        || e.Name == XName.Get("list-style", TextNs))
            .Select(e => e.Attribute(XName.Get("name", StyleNs))?.Value)
            .Where(n => !string.IsNullOrEmpty(n))
            .Select(n => n!);

    private static XDocument Xml(ZipArchive zip, string path)
    {
        using var stream = zip.GetEntry(path)!.Open();

        // No DTD, no external resolution: the same settings the reader uses.
        using var reader = XmlReader.Create(
            stream,
            new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });

        return XDocument.Load(reader);
    }

    private static MemoryStream Written(SpDocument document)
    {
        var file = new MemoryStream();
        Format.Write(document, file, "test.odt", CancellationToken.None);
        file.Position = 0;

        return file;
    }

    private static SpDocument Document(params Block[] blocks) =>
        new(blocks, new Dictionary<string, string>(), new PageSetup(), new HashSet<string>());

    private static Paragraph Text(string text, CharStyle? style = null, string styleName = "Normal") =>
        new()
        {
            StyleName = styleName,
            Inlines = [new TextRun(text, style ?? new CharStyle())],
        };

    private static string FirstText(Cell cell) =>
        cell.Blocks.OfType<Paragraph>()
            .SelectMany(p => p.Inlines)
            .OfType<TextRun>()
            .Select(r => r.Text)
            .FirstOrDefault() ?? "";

    private static byte[] MinimalPng() =>
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
        0x08, 0x02, 0x00, 0x00, 0x00, 0x90, 0x77, 0x53,
        0xDE, 0x00, 0x00, 0x00, 0x0C, 0x49, 0x44, 0x41,
        0x54, 0x08, 0xD7, 0x63, 0xF8, 0xCF, 0xC0, 0x00,
        0x00, 0x03, 0x01, 0x01, 0x00, 0x18, 0xDD, 0x8D,
        0xB0, 0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E,
        0x44, 0xAE, 0x42, 0x60, 0x82,
    ];
}
