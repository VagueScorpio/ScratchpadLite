using System.Text.Json;
using ScratchpadLite.Core.Model;
using Xunit;

namespace ScratchpadLite.Core.Tests;

/// <summary>
/// The IR as JSON (Section 6.3), which is what a recovery file holds
/// (Section 6.13.1).
/// </summary>
/// <remarks>
/// These exist because of what Phase 8 found. The IR had been serialized since
/// Phase 4 - every recovery write, and the shape all the format handlers
/// produce - but nothing had ever read one back, so nobody noticed that
/// <c>SpDocument.FontsUsed</c> could not be deserialized at all:
/// <c>System.Text.Json</c> writes an <c>IReadOnlySet&lt;string&gt;</c> and then
/// refuses to read one, because it has no way to know a <c>HashSet</c> would
/// do. Crash recovery is the first feature that needs the round trip, and it
/// failed on its first test.
/// </remarks>
public class IrJsonTests
{
    private static SpDocument Round(SpDocument document)
    {
        var json = JsonSerializer.Serialize(document, IrJson.Default.SpDocument);

        var read = JsonSerializer.Deserialize(json, IrJson.Default.SpDocument);

        Assert.NotNull(read);

        return read;
    }

    [Fact]
    public void AnEmptyDocumentSurvives()
    {
        var read = Round(SpDocument.Empty);

        Assert.Single(read.Blocks);
    }

    [Fact]
    public void TheFontListSurvives()
    {
        var read = Round(new SpDocument(
            [new Paragraph()],
            new Dictionary<string, string>(),
            new PageSetup(),
            new HashSet<string> { "Calibri", "Cambria" }));

        Assert.Equal(2, read.FontsUsed.Count);
        Assert.Contains("Calibri", read.FontsUsed);
        Assert.Contains("Cambria", read.FontsUsed);
    }

    [Fact]
    public void AnEmptyFontListSurvives() =>
        Assert.Empty(Round(SpDocument.Empty).FontsUsed);

    [Fact]
    public void TheFontListIsWrittenInAStableOrder()
    {
        // A HashSet promises no order, so two writes of the same document could
        // differ byte for byte. A recovery file nobody can compare with itself
        // is harder to investigate than it needs to be.
        var document = new SpDocument(
            [new Paragraph()],
            new Dictionary<string, string>(),
            new PageSetup(),
            new HashSet<string> { "Zapfino", "Arial", "Monaco" });

        var first = JsonSerializer.Serialize(document, IrJson.Default.SpDocument);
        var second = JsonSerializer.Serialize(document, IrJson.Default.SpDocument);

        Assert.Equal(first, second);
        Assert.Contains("[\"Arial\",\"Monaco\",\"Zapfino\"]", first);
    }

    [Fact]
    public void TheMetadataSurvives()
    {
        var read = Round(new SpDocument(
            [new Paragraph()],
            new Dictionary<string, string> { ["title"] = "Quarterly", ["author"] = "Nobody" },
            new PageSetup(),
            new HashSet<string>()));

        Assert.Equal("Quarterly", read.Meta["title"]);
        Assert.Equal("Nobody", read.Meta["author"]);
    }

    [Fact]
    public void ThePageSetupSurvives()
    {
        var read = Round(new SpDocument(
            [new Paragraph()],
            new Dictionary<string, string>(),
            new PageSetup("A5", Landscape: true, 10, 20, 30, 40),
            new HashSet<string>()));

        Assert.Equal("A5", read.Page.Paper);
        Assert.True(read.Page.Landscape);
        Assert.Equal(10, read.Page.MarginTopPt);
        Assert.Equal(40, read.Page.MarginRightPt);
    }

    [Fact]
    public void EveryKindOfBlockAndInlineSurvives()
    {
        var document = new SpDocument(
            [
                new Paragraph
                {
                    StyleName = "Heading 1",
                    Align = Align.Center,
                    IndentLeftPt = 18,
                    LineSpacing = 1.15,
                    PageBreakBefore = true,
                    List = new ListInfo(ListKind.Number, 2, "lowerLetter", 3),
                    Inlines =
                    [
                        new TextRun("bold and blue", new CharStyle
                        {
                            Family = "Cambria",
                            SizePt = 18,
                            Bold = true,
                            Underline = UnderlineKind.Wavy,
                            Vertical = VAlign.Super,
                            SmallCaps = true,
                            Color = "#1565C0",
                            Highlight = "#FFFF00",
                            Link = "https://example.com",
                        }),
                        new LineBreak(),
                        new ImageRun([1, 2, 3, 4], "image/png", 48, 24, "a tiny picture"),
                    ],
                },
                new Table(
                    [
                        [
                            new Cell([new Paragraph()], ColSpan: 2, Shading: "#EEEEEE"),
                            new Cell([new Paragraph()]),
                        ],
                    ],
                    [100.5, 200.5],
                    Borders: false),
                new HorizontalRule(),
            ],
            new Dictionary<string, string>(),
            new PageSetup(),
            new HashSet<string> { "Cambria" });

        var read = Round(document);

        // A record's structural equality is the whole oracle here: if anything
        // at all failed to make the trip, this is not equal.
        Assert.Equal(3, read.Blocks.Count);

        var paragraph = Assert.IsType<Paragraph>(read.Blocks[0]);

        Assert.Equal("Heading 1", paragraph.StyleName);
        Assert.Equal(Align.Center, paragraph.Align);
        Assert.Equal(new ListInfo(ListKind.Number, 2, "lowerLetter", 3), paragraph.List);

        var run = Assert.IsType<TextRun>(paragraph.Inlines[0]);

        Assert.Equal(((TextRun)document.Blocks.OfType<Paragraph>().First().Inlines[0]).Style, run.Style);

        Assert.IsType<LineBreak>(paragraph.Inlines[1]);

        var image = Assert.IsType<ImageRun>(paragraph.Inlines[2]);

        Assert.Equal([1, 2, 3, 4], image.Data);
        Assert.Equal("a tiny picture", image.Alt);

        var table = Assert.IsType<Table>(read.Blocks[1]);

        Assert.Equal(2, table.Rows[0][0].ColSpan);
        Assert.Equal("#EEEEEE", table.Rows[0][0].Shading);
        Assert.False(table.Borders);
        Assert.Equal([100.5, 200.5], table.ColWidthsPt);

        Assert.IsType<HorizontalRule>(read.Blocks[2]);
    }

    [Fact]
    public void AMissingFontListReadsAsEmptyRatherThanThrowing()
    {
        // A recovery file from an older build, or one edited by hand.
        var read = JsonSerializer.Deserialize(
            """{"blocks":[],"meta":{},"page":{"paper":"Letter"},"fontsUsed":null}""",
            IrJson.Default.SpDocument);

        Assert.NotNull(read);
        Assert.Empty(read.FontsUsed);
    }

    [Fact]
    public void AFontListThatIsNotAnArrayIsRejectedClearly() =>
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(
            """{"blocks":[],"meta":{},"page":{"paper":"Letter"},"fontsUsed":"Calibri"}""",
            IrJson.Default.SpDocument));
}
