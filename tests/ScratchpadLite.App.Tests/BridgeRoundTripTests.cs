using ScratchpadLite.App.Bridge;
using ScratchpadLite.Core.Model;
using Xunit;
using Xunit.Abstractions;

namespace ScratchpadLite.App.Tests;

/// <summary>
/// The most important test in the project (Section 6.4): for any IR document
/// <c>d</c>, <c>Walk(Build(d))</c> equals <c>d</c> after normalisation.
/// </summary>
/// <remarks>
/// Everything about opening and saving files rests on this. A format handler's
/// only job is to turn its file into IR and back; if the bridge between IR and
/// the editor loses something, then every format loses it, and no amount of
/// care in the handlers can put it back. So this is tested feature by feature
/// rather than with one big document, because a single failure then names the
/// feature that broke.
/// <para>
/// Normalisation is what makes the comparison fair rather than loose - see
/// <see cref="IrNormalizer"/> for exactly what it is allowed to discard.
/// </para>
/// </remarks>
[Collection(nameof(AppHost))]
public class BridgeRoundTripTests(ITestOutputHelper output)
{
    private static readonly StyleSheet Styles = StyleSheet.Default;

    /// <summary>Builds a document, walks it back, and compares.</summary>
    private void RoundTrip(SpDocument document)
    {
        var walked = AppHost.Run(() =>
        {
            var flow = FlowBuilder.Build(document, Styles);

            return FlowWalker.Walk(flow, Styles, document.Meta, document.Page);
        });

        // FontsUsed is left out of the comparison because it is derived, not
        // carried: the walker works it out by looking at the runs, for the
        // missing-font bar and the save warning. A document going in need not
        // declare its fonts, and one coming out always does.
        // TheFontsUsedAreCollected covers it on its own.
        var empty = new HashSet<string>();
        var before = IrNormalizer.Normalize(document) with { FontsUsed = empty };
        var after = IrNormalizer.Normalize(walked) with { FontsUsed = empty };

        if (!IrComparer.Exactly(before, after))
        {
            output.WriteLine("=== went in ===");
            output.WriteLine(IrNormalizer.Describe(before));
            output.WriteLine("=== came out ===");
            output.WriteLine(IrNormalizer.Describe(after));

            Assert.Fail("the document did not survive the round trip; see the output above");
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
    public void AnEmptyDocument() => RoundTrip(SpDocument.Empty);

    [Fact]
    public void PlainText() => RoundTrip(Document(Text("Hello, world.")));

    [Fact]
    public void SeveralParagraphs() => RoundTrip(Document(
        Text("First."),
        Text("Second."),
        Text("Third.")));

    [Theory]
    [InlineData("Normal")]
    [InlineData("Title")]
    [InlineData("Subtitle")]
    [InlineData("Heading 1")]
    [InlineData("Heading 2")]
    [InlineData("Heading 3")]
    [InlineData("Heading 4")]
    [InlineData("Heading 5")]
    [InlineData("Heading 6")]
    [InlineData("Quote")]
    [InlineData("Code")]
    public void EveryParagraphStyle(string styleName) =>
        RoundTrip(Document(Text($"A {styleName} paragraph.", styleName: styleName)));

    [Fact]
    public void BoldAndItalic() => RoundTrip(Document(new Paragraph
    {
        Inlines =
        [
            new TextRun("plain ", new CharStyle()),
            new TextRun("bold ", new CharStyle { Bold = true }),
            new TextRun("italic ", new CharStyle { Italic = true }),
            new TextRun("both", new CharStyle { Bold = true, Italic = true }),
        ],
    }));

    [Theory]
    [InlineData(UnderlineKind.Single)]
    [InlineData(UnderlineKind.Double)]
    [InlineData(UnderlineKind.Dotted)]
    [InlineData(UnderlineKind.Dashed)]
    [InlineData(UnderlineKind.Wavy)]
    public void EveryUnderlineKind(UnderlineKind kind) =>
        RoundTrip(Document(Text("underlined", new CharStyle { Underline = kind })));

    [Fact]
    public void StrikethroughAndUnderlineTogether() =>
        RoundTrip(Document(Text(
            "both",
            new CharStyle { Underline = UnderlineKind.Double, Strike = true })));

    [Theory]
    [InlineData(VAlign.Super)]
    [InlineData(VAlign.Sub)]
    public void SuperscriptAndSubscriptKeepTheirTrueSize(VAlign vertical) =>
        // The size that goes in is the size before shrinking, and it has to
        // come back out the same - which is what SpProps.BaseSizePt is for.
        RoundTrip(Document(Text("x", new CharStyle { Vertical = vertical, SizePt = 14 })));

    [Fact]
    public void SmallCaps() => RoundTrip(Document(Text("small caps", new CharStyle { SmallCaps = true })));

    [Fact]
    public void FontAndSize() => RoundTrip(Document(Text(
        "Georgia at 13.5",
        new CharStyle { Family = "Georgia", SizePt = 13.5 })));

    [Fact]
    public void HalfSizesSurviveTheTripThroughDips() =>
        RoundTrip(Document(Text("ten and a half", new CharStyle { SizePt = 10.5 })));

    [Fact]
    public void ColourAndHighlight() => RoundTrip(Document(Text(
        "coloured",
        new CharStyle { Color = "#C00000", Highlight = "#FFFF00" })));

    [Fact]
    public void FormattingThatWpfCannotShowIsKeptAnyway() =>
        // All caps and letter spacing have no WPF equivalent (Section 6.8), so
        // they travel in an attached property and must come back unchanged.
        RoundTrip(Document(Text(
            "preserved",
            new CharStyle { AllCapsPreserved = true, LetterSpacingPtPreserved = 1.5 })));

    [Fact]
    public void ALink() => RoundTrip(Document(Text(
        "a link",
        new CharStyle { Link = "https://example.com/page" })));

    [Fact]
    public void ALinkThatIsNotAValidUri() =>
        // A file can carry anything as a target. It should still come back.
        RoundTrip(Document(Text("odd link", new CharStyle { Link = "not a uri at all" })));

    [Fact]
    public void LineBreaksWithinAParagraph() => RoundTrip(Document(new Paragraph
    {
        Inlines =
        [
            new TextRun("first line", new CharStyle()),
            new LineBreak(),
            new TextRun("second line", new CharStyle()),
        ],
    }));

    [Fact]
    public void Alignment() => RoundTrip(Document(
        Text("left") with { Align = Align.Left },
        Text("centre") with { Align = Align.Center },
        Text("right") with { Align = Align.Right },
        Text("justify") with { Align = Align.Justify }));

    [Fact]
    public void IndentsAndSpacing() => RoundTrip(Document(Text("indented") with
    {
        IndentLeftPt = 36,
        IndentRightPt = 18,
        FirstLinePt = 24,
        SpaceBeforePt = 12,
        SpaceAfterPt = 6,
        LineSpacing = 1.5,
    }));

    [Fact]
    public void APageBreakBefore() =>
        RoundTrip(Document(Text("after the break") with { PageBreakBefore = true }));

    [Fact]
    public void AHorizontalRule() => RoundTrip(Document(
        Text("above"),
        new HorizontalRule(),
        Text("below")));

    [Fact]
    public void ABulletedList() => RoundTrip(Document(
        Text("one") with { List = new ListInfo(ListKind.Bullet, 0) },
        Text("two") with { List = new ListInfo(ListKind.Bullet, 0) },
        Text("three") with { List = new ListInfo(ListKind.Bullet, 0) }));

    [Fact]
    public void ANumberedList() => RoundTrip(Document(
        Text("one") with { List = new ListInfo(ListKind.Number, 0) },
        Text("two") with { List = new ListInfo(ListKind.Number, 0) }));

    [Fact]
    public void ANestedList() => RoundTrip(Document(
        Text("top") with { List = new ListInfo(ListKind.Bullet, 0) },
        Text("nested") with { List = new ListInfo(ListKind.Bullet, 1) },
        Text("deeper") with { List = new ListInfo(ListKind.Bullet, 2) },
        Text("back to top") with { List = new ListInfo(ListKind.Bullet, 0) }));

    [Theory]
    [InlineData("decimal")]
    [InlineData("lowerLetter")]
    [InlineData("upperLetter")]
    [InlineData("lowerRoman")]
    [InlineData("upperRoman")]
    public void EveryNumberFormat(string format) => RoundTrip(Document(
        Text("item") with { List = new ListInfo(ListKind.Number, 0, format) }));

    [Fact]
    public void AListThatStartsAtSeven() => RoundTrip(Document(
        Text("seven") with { List = new ListInfo(ListKind.Number, 0, "decimal", 7) }));

    [Fact]
    public void AListFollowedByAnOrdinaryParagraph() => RoundTrip(Document(
        Text("item") with { List = new ListInfo(ListKind.Bullet, 0) },
        Text("after the list")));

    [Fact]
    public void ABulletedListFollowedByANumberedOneStaysTwoLists() => RoundTrip(Document(
        Text("bullet") with { List = new ListInfo(ListKind.Bullet, 0) },
        Text("number") with { List = new ListInfo(ListKind.Number, 0) },
        Text("bullet again") with { List = new ListInfo(ListKind.Bullet, 0) }));

    [Fact]
    public void ChangingNumberFormatMidRunStartsANewList() => RoundTrip(Document(
        Text("1.") with { List = new ListInfo(ListKind.Number, 0, "decimal") },
        Text("a.") with { List = new ListInfo(ListKind.Number, 0, "lowerLetter") }));

    [Fact]
    public void ATable() => RoundTrip(Document(new Table(
        [
            [new Cell([Text("a")]), new Cell([Text("b")])],
            [new Cell([Text("c")]), new Cell([Text("d")])],
        ],
        ColWidthsPt: null)));

    [Fact]
    public void ATableWithSpansShadingAndWidths() => RoundTrip(Document(new Table(
        [
            [new Cell([Text("wide")], ColSpan: 2, Shading: "#EEEEEE")],
            [new Cell([Text("tall")], RowSpan: 2), new Cell([Text("x")])],
            [new Cell([Text("y")])],
        ],
        ColWidthsPt: [120, 80])));

    [Fact]
    public void ATableWithoutBorders() => RoundTrip(Document(new Table(
        [[new Cell([Text("plain")])]],
        ColWidthsPt: null,
        Borders: false)));

    [Fact]
    public void ATableHoldingFormattedTextAndSeveralParagraphs() => RoundTrip(Document(new Table(
        [
            [
                new Cell([Text("first"), Text("second")]),
                new Cell([Text("bold", new CharStyle { Bold = true })]),
            ],
        ],
        ColWidthsPt: null)));

    [Fact]
    public void AnImageKeepsItsExactBytes()
    {
        // The point: an image must not be re-encoded on the way through, or a
        // screenshot would degrade a little on every save.
        var png = MinimalPng();

        RoundTrip(Document(new Paragraph
        {
            Inlines = [new ImageRun(png, "image/png", 48, 24, "a red dot")],
        }));
    }

    [Fact]
    public void MetadataAndPageSetup() => RoundTrip(new SpDocument(
        [Text("content")],
        new Dictionary<string, string> { ["title"] = "A Title", ["author"] = "Someone" },
        new PageSetup("A4", Landscape: true, 36, 36, 54, 54),
        new HashSet<string>()));

    [Fact]
    public void EverythingAtOnce()
    {
        // One document using every feature, because features can interact even
        // when each works on its own.
        RoundTrip(new SpDocument(
            [
                Text("The Title", styleName: "Title"),
                Text("A subtitle", styleName: "Subtitle"),
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
                Text("code()", styleName: "Code"),
                Text("bullet") with { List = new ListInfo(ListKind.Bullet, 0) },
                Text("nested bullet") with { List = new ListInfo(ListKind.Bullet, 1) },
                Text("numbered") with { List = new ListInfo(ListKind.Number, 0) },
                new HorizontalRule(),
                new Table(
                    [
                        [new Cell([Text("head", new CharStyle { Bold = true })]), new Cell([Text("head 2")])],
                        [new Cell([Text("body")]), new Cell([Text("body 2")])],
                    ],
                    [100, 100]),
                new Paragraph { Inlines = [new ImageRun(MinimalPng(), "image/png", 32, 32)] },
                Text("The end.") with { Align = Align.Center, SpaceBeforePt = 18 },
            ],
            new Dictionary<string, string> { ["title"] = "Everything" },
            new PageSetup("Letter", false, 72, 72, 72, 72),
            new HashSet<string>()));
    }

    [Fact]
    public void TheFontsUsedAreCollected()
    {
        // What FontsUsed is for: the missing-font bar and the save warning need
        // to know which families a document asks for, including the one the
        // paragraph style supplies rather than the run.
        var document = Document(new Paragraph
        {
            Inlines =
            [
                new TextRun("default ", new CharStyle()),
                new TextRun("georgia ", new CharStyle { Family = "Georgia" }),
                new TextRun("verdana", new CharStyle { Family = "Verdana" }),
            ],
        });

        var walked = AppHost.Run(() =>
            FlowWalker.Walk(FlowBuilder.Build(document, Styles), Styles));

        Assert.Contains("Georgia", walked.FontsUsed);
        Assert.Contains("Verdana", walked.FontsUsed);
        Assert.Contains(Styles[StyleSheet.NormalStyleName].Family!, walked.FontsUsed);
    }

    [Fact]
    public void AFontFromTheFontFolderIsRecordedByNameNotByPath()
    {
        // A fonts/ folder font resolves to a family whose Source is a file URI
        // into this PC's cache. Writing that into a saved document would put a
        // path from this machine into the file (Section 6.4).
        var folder = Fonts.FontRegistry.FolderFamilyNames().FirstOrDefault();

        if (folder is null)
        {
            return;        // no font-folder fonts registered in this run
        }

        var document = Document(Text("x", new CharStyle { Family = folder }));

        var walked = AppHost.Run(() =>
            FlowWalker.Walk(FlowBuilder.Build(document, Styles), Styles));

        Assert.Contains(folder, walked.FontsUsed);
        Assert.All(walked.FontsUsed, name => Assert.DoesNotContain("#", name));
        Assert.All(walked.FontsUsed, name => Assert.DoesNotContain("/", name));
    }

    /// <summary>
    /// The smallest valid PNG: one red pixel. Written out by hand so the test
    /// needs no fixture file.
    /// </summary>
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
