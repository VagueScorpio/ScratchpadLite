using ScratchpadLite.Core.Formats.Markdown;
using ScratchpadLite.Core.Formats.Odt;
using ScratchpadLite.Core.Model;
using Xunit;
using Xunit.Abstractions;

namespace ScratchpadLite.Core.Tests;

/// <summary>
/// Writes a sample .odt and .md for the Phase 5 hand test.
/// </summary>
/// <remarks>
/// The check that matters most for ODT cannot be automated in this project: a
/// file this app writes has to open in LibreOffice and in Word without a repair
/// prompt, and neither program is used by the app or by its tests. So the files
/// are produced here, by the same writer the app uses, and opened by hand at
/// the phase gate.
/// <para>
/// A test rather than a script so that it uses the real writer and runs on
/// every build. Written next to the test binary, and the path is printed.
/// </para>
/// </remarks>
public class SampleFilesTests(ITestOutputHelper output)
{
    [Fact]
    public void WriteSamplesForTheHandTest()
    {
        var document = Sample();
        var folder = Path.Combine(AppContext.BaseDirectory, "samples");

        Directory.CreateDirectory(folder);

        var odt = Path.Combine(folder, "Phase5-sample.odt");

        using (var file = File.Create(odt))
        {
            new OdtFormat { Styles = StyleSheet.Default }
                .Write(document, file, odt, CancellationToken.None);
        }

        var md = Path.Combine(folder, "Phase5-sample.md");

        using (var file = File.Create(md))
        {
            new MarkdownFormat { Styles = StyleSheet.Default }
                .Write(document, file, md, CancellationToken.None);
        }

        output.WriteLine($"wrote {odt} ({new FileInfo(odt).Length} bytes)");
        output.WriteLine($"wrote {md} ({new FileInfo(md).Length} bytes)");
        output.WriteLine("Open both by hand: the .odt in LibreOffice and Word, the .md anywhere.");

        // Both are read back before anyone is asked to open them, so a file
        // handed over for manual QA is at least known to be self-consistent.
        using (var written = File.OpenRead(odt))
        {
            var back = new OdtFormat { Styles = StyleSheet.Default }
                .Read(written, odt, CancellationToken.None);

            Assert.NotEmpty(back.Document.Blocks);
            Assert.Contains(back.Document.Blocks, b => b is Table);
            Assert.Contains(back.Document.Blocks, b => b is HorizontalRule);
        }

        using (var written = File.OpenRead(md))
        {
            var back = new MarkdownFormat { Styles = StyleSheet.Default }
                .Read(written, md, CancellationToken.None);

            Assert.NotEmpty(back.Document.Blocks);
            Assert.Contains(back.Document.Blocks, b => b is Table);
        }
    }

    private static SpDocument Sample() => new(
        [
            Para("Scratchpad Lite - Phase 5 sample", "Title"),
            Para("Written by the app's own ODT and Markdown writers", "Subtitle"),
            Para("What this file is for", "Heading 1"),
            new Paragraph
            {
                Inlines =
                [
                    new TextRun("This file was produced by ", new CharStyle()),
                    new TextRun("Scratchpad Lite", new CharStyle { Bold = true }),
                    new TextRun(" and should open without a repair prompt. It uses ", new CharStyle()),
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
            Para("Text that looks like markup", "Heading 2"),
            Para("Markdown has to escape all of this: # not a heading, 1. not a list, "
                 + "- not a bullet, 2 * 3 * 4, snake_case_name, a | pipe and ~~not struck~~."),
            Para("Lists", "Heading 2"),
            Para("A bulleted item") with { List = new ListInfo(ListKind.Bullet, 0) },
            Para("Nested one level") with { List = new ListInfo(ListKind.Bullet, 1) },
            Para("Back to the top level") with { List = new ListInfo(ListKind.Bullet, 0) },
            Para("First numbered") with { List = new ListInfo(ListKind.Number, 0) },
            Para("Second numbered") with { List = new ListInfo(ListKind.Number, 0) },
            Para("Lettered, starting at three")
                with { List = new ListInfo(ListKind.Number, 0, "lowerLetter", 3) },
            Para("Roman") with { List = new ListInfo(ListKind.Number, 1, "lowerRoman") },
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
            ["title"] = "Scratchpad Lite Phase 5 sample",
            ["author"] = "Scratchpad Lite",
        },
        new PageSetup(),
        new HashSet<string>());

    private static Paragraph Para(string text, string styleName = "Normal", bool bold = false) => new()
    {
        StyleName = styleName,
        Inlines = [new TextRun(text, new CharStyle { Bold = bold })],
    };
}
