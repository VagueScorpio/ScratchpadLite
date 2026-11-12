using System.IO;
using System.Text;
using ScratchpadLite.App.Formats;
using ScratchpadLite.Core.Formats;
using ScratchpadLite.Core.Model;
using Xunit;
using Xunit.Abstractions;

namespace ScratchpadLite.App.Tests;

/// <summary>
/// RTF through WPF's own converter (Section 7.2), and the Phase 5 check on
/// what that converter keeps.
/// </summary>
/// <remarks>
/// This is the one format the app does not implement, so these tests are not
/// really testing the app's code - they are measuring somebody else's, which is
/// the point. The blueprint asks for RTF files from WordPad, Word and
/// LibreOffice to be tried before deciding whether WPF's converter is good
/// enough, and the answer has to go in the fidelity matrix either way.
/// <para>
/// The fixtures below are RTF written the way each of those three writes it,
/// by hand, because the project cannot ship their files and does not use any
/// of those programs. That makes this a good approximation and not a
/// substitute: a real file from each is still opened by hand at the phase
/// gate. What it does catch is the thing worth catching early - a feature WPF
/// drops entirely - and it catches it on every build rather than once.
/// </para>
/// </remarks>
[Collection(nameof(AppHost))]
public class RtfTests(ITestOutputHelper output)
{
    private static readonly StyleSheet Styles = StyleSheet.Default;

    // --- the app's own round trip -------------------------------------------

    [Fact]
    public void PlainTextSurvives()
    {
        var after = RoundTrip(Document(Text("Hello, world.")));

        Assert.Contains("Hello, world.", AllText(after), StringComparison.Ordinal);
    }

    [Fact]
    public void BoldItalicUnderlineAndStrikeSurvive()
    {
        var document = Document(new Paragraph
        {
            Inlines =
            [
                new TextRun("bold", new CharStyle { Bold = true }),
                new TextRun("italic", new CharStyle { Italic = true }),
                new TextRun("under", new CharStyle { Underline = UnderlineKind.Single }),
                new TextRun("struck", new CharStyle { Strike = true }),
            ],
        });

        var runs = Runs(RoundTrip(document));

        Assert.Contains(runs, r => r.Text == "bold" && r.Style.Bold);
        Assert.Contains(runs, r => r.Text == "italic" && r.Style.Italic);
        Assert.Contains(runs, r => r.Text == "under" && r.Style.Underline != UnderlineKind.None);
        Assert.Contains(runs, r => r.Text == "struck" && r.Style.Strike);
    }

    [Fact]
    public void FontsSizesAndColoursSurvive()
    {
        var document = Document(Text(
            "text",
            new CharStyle
            {
                Family = "Georgia",
                SizePt = 18,
                Color = "#C00000",
                Highlight = "#FFFF00",
            }));

        var run = Assert.Single(Runs(RoundTrip(document)));

        Assert.Equal("Georgia", run.Style.Family);
        Assert.Equal(18, run.Style.SizePt ?? 0, 1);
        Assert.Equal("#C00000", run.Style.Color, ignoreCase: true);
        Assert.Equal("#FFFF00", run.Style.Highlight, ignoreCase: true);
    }

    [Theory]
    [InlineData(UnderlineKind.Single)]
    [InlineData(UnderlineKind.Double)]
    [InlineData(UnderlineKind.Dotted)]
    [InlineData(UnderlineKind.Dashed)]
    [InlineData(UnderlineKind.Wavy)]
    public void AnUnderlineSurvivesThoughNotAlwaysItsKind(UnderlineKind kind)
    {
        // What the matrix's "partly" means, measured rather than assumed. WPF
        // draws the kinds with its own decorations and writes RTF from the
        // decorations, so whether a kind survives depends on whether its RTF
        // keyword exists in the converter at all. Either way the underline is
        // still there, which is the part that matters when reading.
        var document = Document(Text("text", new CharStyle { Underline = kind }));
        var run = Assert.Single(Runs(RoundTrip(document)));

        output.WriteLine($"{kind} came back as {run.Style.Underline}");

        Assert.NotEqual(UnderlineKind.None, run.Style.Underline);
    }

    [Theory]
    [InlineData(VAlign.Super)]
    [InlineData(VAlign.Sub)]
    public void SuperscriptAndSubscriptAreLostOnSaveWhichIsWpfsLimitAndNotTheApps(VAlign vertical)
    {
        // A finding, written down as a test so it cannot quietly change.
        //
        // The blueprint's fidelity matrix has RTF keeping super- and subscript.
        // It does not: WPF's RTF writer has no \super or \sub at all, and
        // writes only the smaller font size the editor uses to draw them. So
        // the text stays small and drops back onto the baseline.
        //
        // Nothing in this app can fix that short of writing RTF by hand, which
        // the blueprint decided against for good reasons. The matrix in
        // USER_GUIDE.md records it instead.
        var document = Document(Text("x", new CharStyle { Vertical = vertical }));
        var after = RoundTrip(document);
        var run = Assert.Single(Runs(after));

        Assert.Equal(VAlign.Normal, run.Style.Vertical);

        // The size does survive, which is why it still looks roughly right.
        Assert.NotNull(run.Style.SizePt);
        Assert.True(
            run.Style.SizePt < StyleSheet.Default[StyleSheet.NormalStyleName].SizePt,
            "even the reduced size was lost, which would make the text look wrong as well");
    }

    [Fact]
    public void ListsSurvive()
    {
        var document = Document(
            Text("one") with { List = new ListInfo(ListKind.Bullet, 0) },
            Text("two") with { List = new ListInfo(ListKind.Bullet, 0) });

        var after = RoundTrip(document);

        Assert.All(
            after.Blocks.OfType<Paragraph>().Where(p => AllText(p).Length > 0),
            p => Assert.NotNull(p.List));
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

        var table = Assert.IsType<Table>(
            Assert.Single(RoundTrip(document).Blocks, b => b is Table));

        Assert.Equal(2, table.Rows.Count);
        Assert.Equal("a", CellText(table.Rows[0][0]));
        Assert.Equal("d", CellText(table.Rows[1][1]));
    }

    [Fact]
    public void ATableKeepsItsCellsWhetherOrNotItKeepsItsMerges()
    {
        // Measured for the matrix. WPF's table model has no merged cells, so
        // the question is only whether the text in them survives: a lost
        // merge is a layout change, a lost cell is lost writing.
        var document = Document(new Table(
            [
                [new Cell([Text("wide")], ColSpan: 2)],
                [new Cell([Text("tall")], RowSpan: 2), new Cell([Text("x")])],
                [new Cell([Text("y")])],
            ],
            ColWidthsPt: null));

        var table = Assert.IsType<Table>(
            Assert.Single(RoundTrip(document).Blocks, b => b is Table));

        var text = string.Join(
            " ",
            table.Rows.SelectMany(r => r).Select(CellText).Where(t => t.Length > 0));

        output.WriteLine($"merges: first cell spans {table.Rows[0][0].ColSpan} columns, "
                         + $"second row's first spans {table.Rows[1][0].RowSpan} rows");
        output.WriteLine($"text: {text}");

        foreach (var expected in new[] { "wide", "tall", "x", "y" })
        {
            Assert.Contains(expected, text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void AnImageSurvives()
    {
        var document = Document(new Paragraph
        {
            Inlines = [new ImageRun(MinimalPng(), "image/png", 48, 24)],
        });

        var after = RoundTrip(document);

        Assert.Contains(
            after.Blocks.OfType<Paragraph>().SelectMany(p => p.Inlines),
            i => i is ImageRun);
    }

    [Fact]
    public void ALinkSurvives()
    {
        var document = Document(Text(
            "a link",
            new CharStyle { Link = "https://example.com/page" }));

        var run = Assert.Single(Runs(RoundTrip(document)), r => r.Text == "a link");

        Assert.NotNull(run.Style.Link);
        Assert.Contains("example.com", run.Style.Link!, StringComparison.Ordinal);
    }

    [Fact]
    public void AlignmentAndIndentsSurvive()
    {
        var document = Document(
            Text("centred") with { Align = Align.Center },
            Text("indented") with { IndentLeftPt = 36 });

        var after = RoundTrip(document);
        var paragraphs = after.Blocks.OfType<Paragraph>().ToList();

        Assert.Contains(paragraphs, p => p.Align == Align.Center);
        Assert.Contains(paragraphs, p => p.IndentLeftPt > 1);
    }

    // --- the heading guess --------------------------------------------------

    [Fact]
    public void AHeadingComesBackWithItsStyleNameGuessedFromItsFormatting()
    {
        // WPF's converter does not write paragraph style names, so this is a
        // guess by size and weight (Section 7.2). It is the one piece of
        // app code in the RTF path, and the only reason headings survive at
        // all.
        var document = Document(Text("A Heading", styleName: "Heading 1"));

        var after = RoundTrip(document);
        var paragraph = Assert.Single(
            after.Blocks.OfType<Paragraph>(),
            p => AllText(p) == "A Heading");

        Assert.Equal("Heading 1", paragraph.StyleName);
    }

    [Fact]
    public void BodyTextIsNotPromotedToAHeading()
    {
        // The other half of the guess, and the one that matters more: ordinary
        // text, and ordinary bold text, must stay Normal. A guess that turns
        // every bold word into a heading would ruin a document on open.
        var document = Document(
            Text("plain"),
            Text("bold", new CharStyle { Bold = true }));

        var after = RoundTrip(document);

        Assert.All(
            after.Blocks.OfType<Paragraph>().Where(p => AllText(p).Length > 0),
            p => Assert.Equal(StyleSheet.NormalStyleName, p.StyleName));
    }

    [Fact]
    public void ASavedHeadingIsFormattingNotAStyleName()
    {
        // What the fidelity matrix has to say: a heading saved to .rtf is
        // 20-point bold text, not a named style. Saving and reopening keeps
        // the look, and the name only comes back because of the guess above.
        var rtf = Save(Document(Text("A Heading", styleName: "Heading 1")));

        output.WriteLine(rtf);

        Assert.DoesNotContain("Heading 1", rtf, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(@"\b", rtf, StringComparison.Ordinal);
    }

    // --- files from the other three ----------------------------------------

    [Fact]
    public void AFileTheWayWordPadWritesItOpens()
    {
        // WordPad's output: no style table, colours in a \colortbl, text in
        // \par-separated paragraphs. The oldest and plainest of the three.
        const string rtf = """
            {\rtf1\ansi\ansicpg1252\deff0\deflang1033
            {\fonttbl{\f0\fnil\fcharset0 Calibri;}{\f1\fnil\fcharset0 Georgia;}}
            {\colortbl ;\red192\green0\blue0;\red255\green255\blue0;}
            \viewkind4\uc1\pard\sa200\sl276\slmult1\f0\fs22 Plain text.\par
            \b Bold\b0  and \i italic\i0  and \ul underlined\ulnone .\par
            \cf1 Red text\cf0  and \highlight2 highlighted\highlight0 .\par
            \f1\fs36 Georgia at eighteen.\f0\fs22\par
            \pard{\pntext\f0 \'B7\tab}{\*\pn\pnlvlblt\pnf0\pnindent0{\pntxtb\'B7}}\fi-360\li720 A bullet\par
            \pard\sa200\sl276\slmult1\par
            }
            """;

        var document = Open(rtf);
        var text = AllText(document);
        var runs = Runs(document);

        output.WriteLine(text);

        Assert.Contains("Plain text.", text, StringComparison.Ordinal);
        Assert.Contains(runs, r => r.Text.Contains("Bold", StringComparison.Ordinal) && r.Style.Bold);
        Assert.Contains(runs, r => r.Text.Contains("italic", StringComparison.Ordinal) && r.Style.Italic);
        Assert.Contains(
            runs,
            r => r.Text.Contains("underlined", StringComparison.Ordinal)
                 && r.Style.Underline != UnderlineKind.None);
        Assert.Contains(runs, r => r.Style.Color is not null);
        Assert.Contains(runs, r => r.Style.Family is not null);
        Assert.Contains("A bullet", text, StringComparison.Ordinal);
    }

    [Fact]
    public void AFileTheWayWordWritesItOpens()
    {
        // Word's output: a stylesheet with named styles, \s1 on the heading,
        // a \listtable, and a table built from \trowd rows.
        const string rtf = """
            {\rtf1\ansi\ansicpg1252\uc1\deff0\deflang1033
            {\fonttbl{\f0\fswiss\fcharset0 Calibri;}{\f1\froman\fcharset0 Cambria;}}
            {\colortbl;\red0\green0\blue0;\red31\green73\blue125;}
            {\stylesheet{\ql\f0\fs22 Normal;}{\s1\ql\keepn\f1\fs40\b\cf2 heading 1;}}
            \pard\plain\s1\f1\fs40\b\cf2 A Word Heading\par
            \pard\plain\ql\f0\fs22 Body text with a {\field{\*\fldinst{HYPERLINK "https://example.com/"}}{\fldrslt{\cf2\ul link}}}.\par
            \trowd\trgaph108\cellx4320\cellx8640
            \pard\intbl\f0\fs22 Cell one\cell
            \pard\intbl Cell two\cell
            \row
            \trowd\trgaph108\cellx4320\cellx8640
            \pard\intbl Cell three\cell
            \pard\intbl Cell four\cell
            \row
            \pard\plain\ql\f0\fs22 After the table.\par
            }
            """;

        var document = Open(rtf);
        var text = AllText(document);

        output.WriteLine(text);
        output.WriteLine(IrNormalizer.Describe(IrNormalizer.Normalize(document)));

        Assert.Contains("A Word Heading", text, StringComparison.Ordinal);
        Assert.Contains("After the table.", text, StringComparison.Ordinal);

        // The heading's formatting has to arrive even though its style name
        // cannot, because the guess works from the formatting.
        var heading = Assert.Single(
            document.Blocks.OfType<Paragraph>(),
            p => AllText(p).Contains("A Word Heading", StringComparison.Ordinal));

        Assert.True(
            heading.StyleName != StyleSheet.NormalStyleName
            || heading.Inlines.OfType<TextRun>().Any(r => r.Style.Bold),
            "a Word heading arrived as neither a style nor bold text");

        var table = Assert.IsType<Table>(Assert.Single(document.Blocks, b => b is Table));

        Assert.Equal(2, table.Rows.Count);
        Assert.Equal(2, table.Rows[0].Count);
        Assert.Contains("Cell one", CellText(table.Rows[0][0]), StringComparison.Ordinal);

        Assert.Contains(Runs(document), r => r.Style.Link is not null);
    }

    [Fact]
    public void AFileTheWayLibreOfficeWritesItOpens()
    {
        // LibreOffice's output: \pard\plain before every paragraph, styles by
        // number, lists through \listtext, and *-prefixed destinations it
        // expects a reader to skip.
        const string rtf = """
            {\rtf1\ansi\deff3\adeflang1025
            {\fonttbl{\f0\froman\fprq2\fcharset0 Liberation Serif;}{\f3\froman\fprq2\fcharset0 Liberation Serif;}}
            {\colortbl;\red0\green0\blue0;\red255\green0\blue0;}
            {\stylesheet{\s0\snext0 Default Paragraph Style;}{\s15\sbasedon0\snext16\b\fs32 Heading;}}
            {\*\listtable{\list\listtemplateid1{\listlevel\levelnfc23{\leveltext\'01\'b7;}}\listid1}}
            {\info{\title A LibreOffice file}}
            \pard\plain\s15\b\fs32 A LibreOffice Heading\par
            \pard\plain\s0\fs24 Ordinary text, then \b bold\b0  then \cf2 red\cf1 .\par
            \pard\plain\s0\fi-360\li720{\listtext\f0 \'b7\tab}First item\par
            \pard\plain\s0\fi-360\li720{\listtext\f0 \'b7\tab}Second item\par
            \pard\plain\s0\fs24\par
            }
            """;

        var document = Open(rtf);
        var text = AllText(document);
        var runs = Runs(document);

        output.WriteLine(text);

        Assert.Contains("A LibreOffice Heading", text, StringComparison.Ordinal);
        Assert.Contains("First item", text, StringComparison.Ordinal);
        Assert.Contains("Second item", text, StringComparison.Ordinal);
        Assert.Contains(runs, r => r.Text.Contains("bold", StringComparison.Ordinal) && r.Style.Bold);
        Assert.Contains(runs, r => r.Style.Color is not null);
    }

    [Fact]
    public void AnImageInAFileFromAnotherProgramOpens()
    {
        // A \pict in hex, which is how all three write a picture into RTF.
        var hex = Convert.ToHexString(MinimalPng()).ToLowerInvariant();

        var rtf = $@"{{\rtf1\ansi\deff0{{\fonttbl{{\f0\fnil Calibri;}}}}
\pard\f0\fs22 Before the picture.\par
\pard{{\pict\pngblip\picw1\pich1\picwgoal720\pichgoal360 {hex}}}\par
\pard After the picture.\par
}}";

        var document = Open(rtf);

        output.WriteLine(AllText(document));

        Assert.Contains("Before the picture.", AllText(document), StringComparison.Ordinal);

        // Recorded either way: whether WPF's converter keeps a \pict is a
        // fidelity-matrix fact, not something this app controls.
        var kept = document.Blocks.OfType<Paragraph>()
            .SelectMany(p => p.Inlines)
            .Any(i => i is ImageRun);

        output.WriteLine($"WPF kept the \\pict: {kept}");

        Assert.True(kept, "WPF's RTF converter dropped a \\pngblip picture");
    }

    // --- robustness ---------------------------------------------------------

    [Fact]
    public void SomethingThatIsNotRtfIsRefusedRatherThanCrashing()
    {
        var error = Record.Exception(() => Open("this is not RTF at all"));

        Assert.IsType<InvalidDataException>(error);
    }

    [Fact]
    public void TruncatedRtfIsRefusedOrReadButNeverCrashes()
    {
        // A download that stopped half way. WPF's converter is tolerant, so
        // this may well succeed; what it must not do is throw something the
        // open flow cannot explain.
        const string rtf = @"{\rtf1\ansi\deff0{\fonttbl{\f0\fnil Calibri;}}\pard\f0\fs22 Half a document";

        var error = Record.Exception(() => Open(rtf));

        if (error is not null)
        {
            Assert.IsType<InvalidDataException>(error);
        }
    }

    [Fact]
    public void TheRegistryKnowsRtfIsHandledOnTheFlowDocument()
    {
        var info = FormatRegistry.InfoFor(".rtf");

        Assert.NotNull(info);
        Assert.True(info!.HandledOnTheFlowDocument);

        // And the capabilities the save warning uses match what these tests
        // just showed survives.
        Assert.True(info.Capabilities.HasFlag(Capabilities.CharFormat));
        Assert.True(info.Capabilities.HasFlag(Capabilities.Colors));
        Assert.True(info.Capabilities.HasFlag(Capabilities.Tables));
        Assert.True(info.Capabilities.HasFlag(Capabilities.Images));

        // Headings are formatting only, so the warning must say they go.
        Assert.False(info.Capabilities.HasFlag(Capabilities.Headings));
        Assert.False(info.Capabilities.HasFlag(Capabilities.PageSetup));
    }

    [Fact]
    public void TheSaveWarningMentionsHeadingsAndPageSetup()
    {
        var document = new SpDocument(
            [Text("A Heading", styleName: "Heading 1")],
            new Dictionary<string, string>(),
            new PageSetup("A4", Landscape: true, 36, 36, 36, 36),
            new HashSet<string>());

        var used = IrFeatures.Used(document);
        var lost = FormatRegistry.WhatWouldBeLost(used, FormatRegistry.InfoFor(".rtf")!.Capabilities);

        Assert.Contains("heading styles", lost);
    }

    // --- helpers ------------------------------------------------------------

    /// <summary>
    /// Saves to RTF and reads it back, both on the UI thread, which is the only
    /// place WPF's converter runs.
    /// </summary>
    private SpDocument RoundTrip(SpDocument document)
    {
        var (ir, rtf) = AppHost.Run(() =>
        {
            var format = new RtfFormat(Styles);
            var flow = Services.DocumentIo.ToFlowDocument(document, Styles);

            using var saved = new MemoryStream();
            format.Save(flow, saved);

            var text = Encoding.UTF8.GetString(saved.ToArray());

            saved.Position = 0;

            return (format.ReadToIr(saved), text);
        });

        // Always logged: when one of these fails the question is what WPF's
        // converter wrote, and that is not guessable from the IR.
        output.WriteLine("=== the .rtf ===");
        output.WriteLine(rtf);
        output.WriteLine("=== read back ===");
        output.WriteLine(IrNormalizer.Describe(IrNormalizer.Normalize(ir)));

        return ir;
    }

    private static string Save(SpDocument document) =>
        AppHost.Run(() =>
        {
            var flow = Services.DocumentIo.ToFlowDocument(document, Styles);

            using var saved = new MemoryStream();
            new RtfFormat(Styles).Save(flow, saved);

            return Encoding.UTF8.GetString(saved.ToArray());
        });

    private static SpDocument Open(string rtf) =>
        AppHost.Run(() =>
        {
            using var input = new MemoryStream(Encoding.UTF8.GetBytes(rtf));

            return new RtfFormat(Styles).ReadToIr(input);
        });

    private static List<TextRun> Runs(SpDocument document) =>
        IrNormalizer.Normalize(document).Blocks
            .OfType<Paragraph>()
            .SelectMany(p => p.Inlines)
            .OfType<TextRun>()
            .Where(r => r.Text.Trim().Length > 0)
            .ToList();

    private static string AllText(SpDocument document) =>
        string.Join("\n", document.Blocks.OfType<Paragraph>().Select(AllText));

    private static string AllText(Paragraph paragraph) =>
        string.Concat(paragraph.Inlines.OfType<TextRun>().Select(r => r.Text)).Trim();

    private static string CellText(Cell cell) =>
        string.Concat(
                cell.Blocks.OfType<Paragraph>()
                    .SelectMany(p => p.Inlines)
                    .OfType<TextRun>()
                    .Select(r => r.Text))
            .Trim();

    private static SpDocument Document(params Block[] blocks) =>
        new(blocks, new Dictionary<string, string>(), new PageSetup(), new HashSet<string>());

    private static Paragraph Text(string text, CharStyle? style = null, string styleName = "Normal") =>
        new()
        {
            StyleName = styleName,
            Inlines = [new TextRun(text, style ?? new CharStyle())],
        };

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
