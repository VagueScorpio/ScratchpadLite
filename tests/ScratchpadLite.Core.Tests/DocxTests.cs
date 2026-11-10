using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using ScratchpadLite.Core.Formats;
using ScratchpadLite.Core.Formats.Docx;
using ScratchpadLite.Core.Model;
using Xunit;
using Xunit.Abstractions;

namespace ScratchpadLite.Core.Tests;

/// <summary>
/// Word documents (Section 7.2).
/// </summary>
/// <remarks>
/// Two kinds of check, and both are needed.
/// <list type="number">
///   <item>
///     Every file written is put through the SDK's own
///     <see cref="OpenXmlValidator"/>, as Section 7.2 requires. A file can
///     round-trip through this app perfectly and still be one Word refuses to
///     open, because Word enforces rules about element order and required
///     children that a reader written to match this writer would never notice.
///   </item>
///   <item>
///     The document is written, read back, and compared. That catches the
///     opposite problem: a file that is valid and says the wrong thing.
///   </item>
/// </list>
/// </remarks>
public class DocxTests(ITestOutputHelper output)
{
    private static readonly StyleSheet Styles = StyleSheet.Default;

    private static readonly DocxFormat Format = new() { Styles = Styles };

    /// <summary>
    /// Writes, validates, reads back, and compares. Returns what came back.
    /// </summary>
    private SpDocument RoundTrip(SpDocument document)
    {
        using var file = new MemoryStream();

        Format.Write(document, file, "test.docx", CancellationToken.None);

        file.Position = 0;
        Validate(file);

        file.Position = 0;
        var result = Format.Read(file, "test.docx", CancellationToken.None);

        foreach (var warning in result.Warnings)
        {
            output.WriteLine("warning: " + warning);
        }

        return result.Document;
    }

    /// <summary>
    /// Fails on any validation error, as Section 7.2 requires.
    /// </summary>
    private void Validate(Stream file)
    {
        using var package = WordprocessingDocument.Open(file, isEditable: false);

        var errors = new OpenXmlValidator().Validate(package).ToList();

        foreach (var error in errors)
        {
            output.WriteLine($"{error.ErrorType} at {error.Path?.XPath}: {error.Description}");
        }

        Assert.Empty(errors);
    }

    /// <summary>
    /// Compares what went in with what came out, ignoring the fields a file
    /// cannot carry.
    /// </summary>
    private void Same(SpDocument before, SpDocument after)
    {
        // FontsUsed is derived by the reader, and the dates Word records are
        // not the document's content.
        var empty = new HashSet<string>();

        var expected = IrNormalizer.Normalize(before) with { FontsUsed = empty };
        var actual = IrNormalizer.Normalize(after) with { FontsUsed = empty };

        if (!IrComparer.Exactly(expected, actual))
        {
            output.WriteLine("=== written ===");
            output.WriteLine(IrNormalizer.Describe(expected));
            output.WriteLine("=== read back ===");
            output.WriteLine(IrNormalizer.Describe(actual));

            Assert.Fail("the document changed on the way through a .docx; see the output above");
        }
    }

    private static SpDocument Document(params Block[] blocks) =>
        new(blocks, new Dictionary<string, string>(), new PageSetup(), new HashSet<string>());

    private static Paragraph Text(string text, CharStyle? style = null, string styleName = "Normal") =>
        new()
        {
            StyleName = styleName,
            Inlines = [new TextRun(text, style ?? new CharStyle())],
        };

    [Fact]
    public void AnEmptyDocumentIsValid() => Validate(Written(SpDocument.Empty));

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
        // By name, not by id: Word localises style ids, so matching on them
        // would break the moment a document came from a non-English Word.
        var document = Document(Text($"A {styleName}.", styleName: styleName));

        Same(document, RoundTrip(document));
    }

    [Fact]
    public void CharacterFormattingSurvives()
    {
        var document = Document(new Paragraph
        {
            Inlines =
            [
                new TextRun("plain ", new CharStyle()),
                new TextRun("bold ", new CharStyle { Bold = true }),
                new TextRun("italic ", new CharStyle { Italic = true }),
                new TextRun("struck ", new CharStyle { Strike = true }),
                new TextRun("caps ", new CharStyle { SmallCaps = true }),
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
        var document = Document(Text("under", new CharStyle { Underline = kind }));

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
    public void FontsAndSizesSurvive()
    {
        var document = Document(Text(
            "Georgia 13.5",
            new CharStyle { Family = "Georgia", SizePt = 13.5 }));

        Same(document, RoundTrip(document));
    }

    [Fact]
    public void AnArbitraryHighlightColourSurvives()
    {
        // Word's Highlight element allows only sixteen named colours, so the
        // writer uses Shading instead (Section 7.2). This is the test for it.
        var document = Document(Text(
            "highlighted",
            new CharStyle { Color = "#C00000", Highlight = "#7B2D8E" }));

        Same(document, RoundTrip(document));
    }

    [Fact]
    public void FormattingWordHasButWpfDoesNotIsKept()
    {
        // All caps and letter spacing are real Word properties that WPF cannot
        // display (Section 6.8), so they are carried through and written back.
        var document = Document(Text(
            "preserved",
            new CharStyle { AllCapsPreserved = true, LetterSpacingPtPreserved = 1.5 }));

        Same(document, RoundTrip(document));
    }

    [Fact]
    public void AlignmentIndentsAndSpacingSurvive()
    {
        var document = Document(
            Text("centre") with { Align = Align.Center },
            Text("justified") with { Align = Align.Justify },
            Text("indented") with
            {
                IndentLeftPt = 36,
                IndentRightPt = 18,
                FirstLinePt = 24,
                SpaceBeforePt = 12,
                SpaceAfterPt = 6,
                LineSpacing = 1.5,
            },
            Text("hanging") with { FirstLinePt = -18 });

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

        var after = RoundTrip(document);

        // Word needs an empty paragraph after a table or the file will not
        // open, so the reader sees one more block than went in. Everything
        // before it has to match.
        var table = Assert.IsType<Table>(after.Blocks[0]);

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

        // The continuation cell of a vertical merge is not a cell of its own.
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

        // The same bytes, not a re-encode.
        Assert.Equal(png, image.Data);
        Assert.Equal("image/png", image.Mime);
        Assert.Equal(48, image.WidthPt, 1);
        Assert.Equal(24, image.HeightPt, 1);
        Assert.Equal("a red dot", image.Alt);
    }

    [Fact]
    public void ALinkSurvives()
    {
        var document = Document(Text(
            "a link",
            new CharStyle { Link = "https://example.com/page" }));

        var paragraph = Assert.IsType<Paragraph>(RoundTrip(document).Blocks[0]);
        var run = Assert.IsType<TextRun>(paragraph.Inlines[0]);

        Assert.Equal("a link", run.Text);
        Assert.Equal("https://example.com/page", run.Style.Link);

        // The link's blue and underline come from Word's Hyperlink style, so
        // they are the style being a link rather than formatting anyone chose.
        Assert.Null(run.Style.Color);
    }

    [Fact]
    public void ALineBreakSurvives()
    {
        var document = Document(new Paragraph
        {
            Inlines =
            [
                new TextRun("first", new CharStyle()),
                new LineBreak(),
                new TextRun("second", new CharStyle()),
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
    public void LeadingAndTrailingSpacesAreNotDropped()
    {
        // Without xml:space="preserve", Word drops them and words run
        // together.
        var document = Document(new Paragraph
        {
            Inlines =
            [
                new TextRun("one ", new CharStyle()),
                new TextRun(" two", new CharStyle { Bold = true }),
            ],
        });

        var paragraph = Assert.IsType<Paragraph>(RoundTrip(document).Blocks[0]);

        Assert.Equal("one ", ((TextRun)paragraph.Inlines[0]).Text);
        Assert.Equal(" two", ((TextRun)paragraph.Inlines[1]).Text);
    }

    [Fact]
    public void ARunThatIsDeliberatelyNotBoldInsideABoldStyleStaysNotBold()
    {
        // The on/off elements in Word are three-valued: absent means inherit,
        // present means on, present with val="0" means off. Treating absent
        // and off alike makes this run come out bold.
        var document = Document(new Paragraph
        {
            StyleName = "Heading 1",
            Inlines =
            [
                new TextRun("heading ", new CharStyle { Bold = true }),
                new TextRun("not bold", new CharStyle { Bold = false }),
            ],
        });

        var paragraph = Assert.IsType<Paragraph>(RoundTrip(document).Blocks[0]);

        Assert.True(((TextRun)paragraph.Inlines[0]).Style.Bold);
        Assert.False(((TextRun)paragraph.Inlines[1]).Style.Bold);
    }

    [Fact]
    public void EverythingAtOnceIsValidAndSurvives()
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
                new Paragraph { Inlines = [new ImageRun(MinimalPng(), "image/png", 32, 32)] },
                Text("The end.") with { Align = Align.Center },
            ],
            new Dictionary<string, string> { ["title"] = "Everything" },
            new PageSetup(),
            new HashSet<string>());

        var after = RoundTrip(document);

        // Compared feature by feature rather than wholesale, because the empty
        // paragraph Word needs after a table and the one after an image shift
        // the block indexes.
        Assert.Equal("Title", ((Paragraph)after.Blocks[0]).StyleName);
        Assert.Equal("Heading 1", ((Paragraph)after.Blocks[1]).StyleName);

        var mixed = (Paragraph)after.Blocks[2];
        Assert.True(((TextRun)mixed.Inlines[1]).Style.Bold);
        Assert.Equal(UnderlineKind.Wavy, ((TextRun)mixed.Inlines[3]).Style.Underline);
        Assert.Equal(VAlign.Super, ((TextRun)mixed.Inlines[5]).Style.Vertical);
        Assert.Equal(
            IrNormalizer.NormalizeLink("https://example.com"),
            ((TextRun)mixed.Inlines[7]).Style.Link);

        Assert.Equal("Everything", after.Meta["title"]);
        Assert.Contains(after.Blocks, b => b is HorizontalRule);
        Assert.Contains(
            after.Blocks,
            b => b is Paragraph { Inlines: [ImageRun, ..] });
    }

    [Fact]
    public void SomethingThatIsNotADocxIsRefusedRatherThanCrashing()
    {
        using var notADocx = new MemoryStream("this is not a zip at all"u8.ToArray());

        Assert.ThrowsAny<Exception>(
            () => Format.Read(notADocx, "broken.docx", CancellationToken.None));
    }

    private static string FirstText(Cell cell) =>
        cell.Blocks.OfType<Paragraph>()
            .SelectMany(p => p.Inlines)
            .OfType<TextRun>()
            .Select(r => r.Text)
            .FirstOrDefault() ?? "";

    private MemoryStream Written(SpDocument document)
    {
        var file = new MemoryStream();
        Format.Write(document, file, "test.docx", CancellationToken.None);
        file.Position = 0;

        return file;
    }

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

/// <summary>
/// Writes a sample .docx for the hand test at the phase gate.
/// </summary>
/// <remarks>
/// A test rather than a script because it uses the same writer the app does
/// and validates what it produces, so the file handed over for manual QA is
/// known-good before anyone opens it. It lands in the test output folder and
/// the build copies it into dist/.
/// </remarks>
public class SampleDocumentTests(ITestOutputHelper output)
{
    [Fact]
    public void WriteASampleDocumentForTheHandTest()
    {
        var document = new SpDocument(
            [
                Para("Scratchpad Lite - Phase 4 sample", "Title"),
                Para("Written by the app's own DOCX writer", "Subtitle"),
                Para("What this file is for", "Heading 1"),
                new Paragraph
                {
                    Inlines =
                    [
                        new TextRun("This file was produced by ", new CharStyle()),
                        new TextRun("Scratchpad Lite", new CharStyle { Bold = true }),
                        new TextRun(" and should open in Word, LibreOffice and the app itself. "
                                    + "It uses ", new CharStyle()),
                        new TextRun("italic", new CharStyle { Italic = true }),
                        new TextRun(", ", new CharStyle()),
                        new TextRun("a wavy underline", new CharStyle { Underline = UnderlineKind.Wavy }),
                        new TextRun(", ", new CharStyle()),
                        new TextRun("strikethrough", new CharStyle { Strike = true }),
                        new TextRun(", a superscript", new CharStyle()),
                        new TextRun("2", new CharStyle { Vertical = VAlign.Super }),
                        new TextRun(", ", new CharStyle()),
                        new TextRun("Georgia at 14pt", new CharStyle { Family = "Georgia", SizePt = 14 }),
                        new TextRun(", ", new CharStyle()),
                        new TextRun("a colour", new CharStyle { Color = "#C00000" }),
                        new TextRun(", ", new CharStyle()),
                        new TextRun("a highlight", new CharStyle { Highlight = "#FFFF00" }),
                        new TextRun(" and ", new CharStyle()),
                        new TextRun("a link", new CharStyle { Link = "https://example.com/" }),
                        new TextRun(".", new CharStyle()),
                    ],
                },
                Para("Lists", "Heading 2"),
                Para("A bulleted item") with { List = new ListInfo(ListKind.Bullet, 0) },
                Para("Nested one level") with { List = new ListInfo(ListKind.Bullet, 1) },
                Para("Back to the top level") with { List = new ListInfo(ListKind.Bullet, 0) },
                Para("First numbered") with { List = new ListInfo(ListKind.Number, 0) },
                Para("Second numbered") with { List = new ListInfo(ListKind.Number, 0) },
                Para("Lettered") with { List = new ListInfo(ListKind.Number, 1, "lowerLetter") },
                Para("A table", "Heading 2"),
                new Table(
                    [
                        [
                            new Cell([Para("Heading", bold: true)], Shading: "#EEEEEE"),
                            new Cell([Para("Another", bold: true)], Shading: "#EEEEEE"),
                        ],
                        [new Cell([Para("Spans two rows")], RowSpan: 2), new Cell([Para("one")])],
                        [new Cell([Para("two")])],
                    ],
                    [160, 160]),
                Para("Other blocks", "Heading 2"),
                Para("A quotation, indented and in italics.", "Quote"),
                Para("var answer = 42;", "Code"),
                new HorizontalRule(),
                Para("Centred, after the rule.") with { Align = Align.Center },
                Para("Justified text, with space before it and one-and-a-half line spacing, so the "
                     + "paragraph settings have something to show. This sentence is long enough to "
                     + "wrap onto a second line, which is the only way to see line spacing at all.")
                    with { Align = Align.Justify, SpaceBeforePt = 18, LineSpacing = 1.5 },
                Para("On a new page.") with { PageBreakBefore = true },
            ],
            new Dictionary<string, string>
            {
                ["title"] = "Scratchpad Lite Phase 4 sample",
                ["author"] = "Scratchpad Lite",
            },
            new PageSetup(),
            new HashSet<string>());

        var folder = Path.Combine(AppContext.BaseDirectory, "samples");
        Directory.CreateDirectory(folder);

        var path = Path.Combine(folder, "Phase4-sample.docx");

        using (var file = File.Create(path))
        {
            new DocxFormat { Styles = StyleSheet.Default }
                .Write(document, file, path, CancellationToken.None);
        }

        // Validated before it is handed to anyone.
        using (var written = File.OpenRead(path))
        {
            using var package = WordprocessingDocument.Open(written, isEditable: false);
            var errors = new OpenXmlValidator().Validate(package).ToList();

            foreach (var error in errors)
            {
                output.WriteLine($"{error.ErrorType} at {error.Path?.XPath}: {error.Description}");
            }

            Assert.Empty(errors);
        }

        output.WriteLine($"wrote {path} ({new FileInfo(path).Length} bytes)");
    }

    private static Paragraph Para(string text, string styleName = "Normal", bool bold = false) => new()
    {
        StyleName = styleName,
        Inlines = [new TextRun(text, new CharStyle { Bold = bold })],
    };
}
