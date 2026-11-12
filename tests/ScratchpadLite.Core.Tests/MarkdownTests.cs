using System.Text;
using ScratchpadLite.Core.Formats;
using ScratchpadLite.Core.Formats.Markdown;
using ScratchpadLite.Core.Model;
using Xunit;
using Xunit.Abstractions;

namespace ScratchpadLite.Core.Tests;

/// <summary>
/// Markdown (Section 7.2), round-tripped within its capabilities.
/// </summary>
/// <remarks>
/// Markdown carries much less than .docx, so a whole-document comparison is
/// the wrong test: the interesting question is not whether everything survives
/// - it cannot - but whether exactly what the fidelity matrix promises
/// survives and the rest is dropped cleanly rather than mangled.
/// <para>
/// The other half is escaping. Markdown has no way to say "this asterisk is an
/// asterisk" other than a backslash, and getting that wrong is how a document
/// full of prices turns into italics. Those tests read the written text
/// directly rather than going through the reader, because a reader that makes
/// the same mistake in reverse would hide the fault.
/// </para>
/// </remarks>
public class MarkdownTests(ITestOutputHelper output)
{
    private static readonly MarkdownFormat Format = new() { Styles = StyleSheet.Default };

    private SpDocument RoundTrip(SpDocument document)
    {
        var text = Write(document);

        output.WriteLine("=== written ===");
        output.WriteLine(text);

        using var input = new MemoryStream(Encoding.UTF8.GetBytes(text));
        var result = Format.Read(input, "test.md", CancellationToken.None);

        foreach (var warning in result.Warnings)
        {
            output.WriteLine("warning: " + warning);
        }

        return result.Document;
    }

    // --- what survives ------------------------------------------------------

    [Fact]
    public void PlainTextSurvives()
    {
        var after = RoundTrip(Document(Text("Hello, world.")));

        Assert.Equal("Hello, world.", AllText(after));
    }

    [Theory]
    [InlineData("Heading 1", "# ")]
    [InlineData("Heading 2", "## ")]
    [InlineData("Heading 3", "### ")]
    [InlineData("Heading 4", "#### ")]
    [InlineData("Heading 5", "##### ")]
    [InlineData("Heading 6", "###### ")]
    public void EveryHeadingLevelSurvives(string styleName, string marker)
    {
        var document = Document(Text("text", styleName: styleName));
        var text = Write(document);

        Assert.StartsWith(marker, text, StringComparison.Ordinal);

        var after = RoundTrip(document);

        Assert.Equal(styleName, ((Paragraph)after.Blocks[0]).StyleName);
    }

    [Fact]
    public void TheTitleBecomesAHeadingAndComesBackAsOne()
    {
        // Markdown has one heading ladder and the app has Title, Subtitle and
        // six headings on top of Normal. Title has to land somewhere, and the
        // top of the ladder is the only sensible place.
        var document = Document(Text("The Title", styleName: "Title"));
        var text = Write(document);

        Assert.StartsWith("# ", text, StringComparison.Ordinal);

        var after = RoundTrip(document);
        var style = ((Paragraph)after.Blocks[0]).StyleName;

        Assert.True(
            style is "Title" or "Heading 1",
            $"a Title came back as {style}, which is neither of the two it may be");
    }

    [Fact]
    public void BoldItalicAndStrikethroughSurvive()
    {
        var document = Document(new Paragraph
        {
            Inlines =
            [
                new TextRun("bold", new CharStyle { Bold = true }),
                new TextRun(" ", new CharStyle()),
                new TextRun("italic", new CharStyle { Italic = true }),
                new TextRun(" ", new CharStyle()),
                new TextRun("struck", new CharStyle { Strike = true }),
            ],
        });

        var text = Write(document);

        Assert.Contains("**bold**", text, StringComparison.Ordinal);
        Assert.Contains("*italic*", text, StringComparison.Ordinal);
        Assert.Contains("~~struck~~", text, StringComparison.Ordinal);

        var paragraph = Assert.IsType<Paragraph>(RoundTrip(document).Blocks[0]);
        var runs = paragraph.Inlines.OfType<TextRun>().ToList();

        Assert.Contains(runs, r => r.Text == "bold" && r.Style.Bold);
        Assert.Contains(runs, r => r.Text == "italic" && r.Style.Italic);
        Assert.Contains(runs, r => r.Text == "struck" && r.Style.Strike);
    }

    [Fact]
    public void BoldAndItalicTogetherSurvive()
    {
        var document = Document(Text("both", new CharStyle { Bold = true, Italic = true }));

        var run = Assert.IsType<TextRun>(
            Assert.IsType<Paragraph>(RoundTrip(document).Blocks[0]).Inlines[0]);

        Assert.True(run.Style.Bold);
        Assert.True(run.Style.Italic);
    }

    [Fact]
    public void ListsSurvive()
    {
        var document = Document(
            Text("one") with { List = new ListInfo(ListKind.Bullet, 0) },
            Text("nested") with { List = new ListInfo(ListKind.Bullet, 1) },
            Text("two") with { List = new ListInfo(ListKind.Bullet, 0) });

        var after = RoundTrip(document);
        var paragraphs = after.Blocks.OfType<Paragraph>().ToList();

        Assert.Equal(3, paragraphs.Count);
        Assert.All(paragraphs, p => Assert.NotNull(p.List));
        Assert.Equal(ListKind.Bullet, paragraphs[0].List!.Kind);
        Assert.Equal(0, paragraphs[0].List!.Level);
        Assert.Equal(1, paragraphs[1].List!.Level);
        Assert.Equal(0, paragraphs[2].List!.Level);
    }

    [Fact]
    public void AListNestedUnderANumberedItemStaysNested()
    {
        // The indent has to clear the parent's content column, which for
        // "1. " is one wider than for "- ". Two spaces makes this a second
        // list beside the first rather than a child of it.
        var document = Document(
            Text("one") with { List = new ListInfo(ListKind.Number, 0) },
            Text("nested") with { List = new ListInfo(ListKind.Bullet, 1) },
            Text("deeper") with { List = new ListInfo(ListKind.Number, 2) });

        var after = RoundTrip(document);
        var items = after.Blocks.OfType<Paragraph>().Where(p => p.List is not null).ToList();

        Assert.Equal(3, items.Count);
        Assert.Equal(0, items[0].List!.Level);
        Assert.Equal(1, items[1].List!.Level);
        Assert.Equal(2, items[2].List!.Level);
    }

    [Fact]
    public void ALetteredListBecomesNumbersBecauseMarkdownHasNoLetters()
    {
        // Recorded, because the fidelity matrix has to say so: Markdown has no
        // syntax for a lettered or roman list at all.
        var document = Document(
            Text("a") with { List = new ListInfo(ListKind.Number, 0, "lowerLetter") });

        var paragraph = Assert.IsType<Paragraph>(RoundTrip(document).Blocks[0]);

        Assert.Equal(ListKind.Number, paragraph.List!.Kind);
        Assert.Equal("decimal", paragraph.List!.NumberFormat);
    }

    [Fact]
    public void ANumberedListSurvivesAsANumberedList()
    {
        var document = Document(
            Text("one") with { List = new ListInfo(ListKind.Number, 0) },
            Text("two") with { List = new ListInfo(ListKind.Number, 0) });

        var after = RoundTrip(document);

        Assert.All(
            after.Blocks.OfType<Paragraph>(),
            p => Assert.Equal(ListKind.Number, p.List!.Kind));
    }

    [Fact]
    public void ANumberedListThatStartsPartWayThroughSurvives()
    {
        var document = Document(
            Text("seven") with { List = new ListInfo(ListKind.Number, 0, "decimal", 7) });

        var text = Write(document);

        Assert.Contains("7.", text, StringComparison.Ordinal);

        var paragraph = Assert.IsType<Paragraph>(RoundTrip(document).Blocks[0]);

        Assert.Equal(7, paragraph.List!.Start);
    }

    [Fact]
    public void ABulletedListFollowedByANumberedOneStaysTwoKinds()
    {
        var document = Document(
            Text("bullet") with { List = new ListInfo(ListKind.Bullet, 0) },
            Text("number") with { List = new ListInfo(ListKind.Number, 0) });

        var paragraphs = RoundTrip(document).Blocks.OfType<Paragraph>().ToList();

        Assert.Equal(ListKind.Bullet, paragraphs[0].List!.Kind);
        Assert.Equal(ListKind.Number, paragraphs[1].List!.Kind);
    }

    [Fact]
    public void ASimpleTableSurvives()
    {
        var document = Document(new Table(
            [
                [new Cell([Text("a")]), new Cell([Text("b")])],
                [new Cell([Text("c")]), new Cell([Text("d")])],
            ],
            ColWidthsPt: null));

        var text = Write(document);

        Assert.Contains("| a | b |", text, StringComparison.Ordinal);
        Assert.Contains("---", text, StringComparison.Ordinal);

        var table = Assert.IsType<Table>(RoundTrip(document).Blocks[0]);

        Assert.Equal(2, table.Rows.Count);
        Assert.Equal("a", FirstText(table.Rows[0][0]));
        Assert.Equal("d", FirstText(table.Rows[1][1]));
    }

    [Fact]
    public void APipeInsideACellDoesNotBreakTheTable()
    {
        var document = Document(new Table(
            [[new Cell([Text("a|b")]), new Cell([Text("c")])]],
            ColWidthsPt: null));

        var table = Assert.IsType<Table>(RoundTrip(document).Blocks[0]);

        Assert.Equal(2, table.Rows[0].Count);
        Assert.Equal("a|b", FirstText(table.Rows[0][0]));
    }

    [Fact]
    public void ALinkSurvives()
    {
        var document = Document(Text(
            "a link",
            new CharStyle { Link = "https://example.com/page" }));

        var text = Write(document);

        Assert.Contains("[a link](https://example.com/page)", text, StringComparison.Ordinal);

        var run = Assert.IsType<TextRun>(
            Assert.IsType<Paragraph>(RoundTrip(document).Blocks[0]).Inlines[0]);

        Assert.Equal(IrNormalizer.NormalizeLink("https://example.com/page"), run.Style.Link);
    }

    [Fact]
    public void AHorizontalRuleSurvives()
    {
        var document = Document(Text("above"), new HorizontalRule(), Text("below"));

        Assert.Contains(RoundTrip(document).Blocks, b => b is HorizontalRule);
    }

    [Fact]
    public void AParagraphBreakIsNotLost()
    {
        // Two paragraphs separated by one newline are one paragraph in
        // Markdown, so the writer has to leave a blank line between them.
        var document = Document(Text("first"), Text("second"));

        var after = RoundTrip(document);

        Assert.Equal(2, after.Blocks.OfType<Paragraph>().Count());
    }

    [Fact]
    public void ALineBreakInsideAParagraphSurvives()
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

        var paragraph = Assert.IsType<Paragraph>(RoundTrip(document).Blocks[0]);

        Assert.Contains(paragraph.Inlines, i => i is LineBreak);
    }

    [Fact]
    public void CodeStyleBecomesAFencedBlockAndComesBack()
    {
        var document = Document(Text("var answer = 42;", styleName: "Code"));
        var text = Write(document);

        Assert.Contains("```", text, StringComparison.Ordinal);

        var paragraph = Assert.IsType<Paragraph>(RoundTrip(document).Blocks[0]);

        Assert.Equal("Code", paragraph.StyleName);
        Assert.Equal("var answer = 42;", AllText(RoundTrip(document)));
    }

    [Fact]
    public void QuoteStyleBecomesABlockQuoteAndComesBack()
    {
        var document = Document(Text("quoted", styleName: "Quote"));
        var text = Write(document);

        Assert.StartsWith(">", text, StringComparison.Ordinal);

        Assert.Equal("Quote", ((Paragraph)RoundTrip(document).Blocks[0]).StyleName);
    }

    // --- what is dropped, and dropped cleanly -------------------------------

    [Fact]
    public void FormattingMarkdownCannotCarryIsDroppedNotMangled()
    {
        var document = Document(Text(
            "text",
            new CharStyle
            {
                Underline = UnderlineKind.Wavy,
                Family = "Georgia",
                SizePt = 18,
                Color = "#C00000",
                Highlight = "#FFFF00",
                Vertical = VAlign.Super,
            }));

        var text = Write(document);

        // No stray markers, no HTML, no leftovers: the text and nothing else.
        Assert.Equal("text", text.Trim());

        var run = Assert.IsType<TextRun>(
            Assert.IsType<Paragraph>(RoundTrip(document).Blocks[0]).Inlines[0]);

        Assert.Equal("text", run.Text);
        Assert.Equal(UnderlineKind.None, run.Style.Underline);
        Assert.Null(run.Style.Family);
        Assert.Null(run.Style.SizePt);
        Assert.Null(run.Style.Color);
        Assert.Null(run.Style.Highlight);
        Assert.Equal(VAlign.Normal, run.Style.Vertical);
    }

    [Fact]
    public void TheSaveWarningSaysWhatAMarkdownSaveWouldLose()
    {
        var document = Document(Text(
            "text",
            new CharStyle { Bold = true, Underline = UnderlineKind.Single, SizePt = 18 }));

        var used = IrFeatures.Used(document);
        var lost = FormatRegistry.WhatWouldBeLost(used, Format.Capabilities);

        // Bold is kept, so warning about it would be crying wolf; the size and
        // the underline are not.
        Assert.DoesNotContain(lost, l => l.Contains("bold", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("underline", lost);
        Assert.Contains(lost, l => l.Contains("font", StringComparison.OrdinalIgnoreCase));
    }

    // --- escaping -----------------------------------------------------------

    [Theory]
    [InlineData("2 * 3 * 4")]
    [InlineData("snake_case_name")]
    [InlineData("a [bracket] and a (paren)")]
    [InlineData("# not a heading")]
    [InlineData("- not a list")]
    [InlineData("1. not a numbered list")]
    [InlineData("> not a quote")]
    [InlineData("a `backtick` and a \\backslash")]
    [InlineData("~~not struck~~")]
    [InlineData("<html> & \"quotes\"")]
    [InlineData("| a pipe")]
    public void TextThatLooksLikeMarkupComesBackAsItself(string text)
    {
        var document = Document(Text(text));
        var written = Write(document);

        output.WriteLine($"written: {written.TrimEnd()}");

        var after = RoundTrip(document);

        Assert.Equal(text, AllText(after));

        // And it is still one plain run: escaping must not turn the text into
        // formatting that happens to read the same.
        var paragraph = Assert.IsType<Paragraph>(after.Blocks[0]);

        Assert.All(
            paragraph.Inlines.OfType<TextRun>(),
            run =>
            {
                Assert.False(run.Style.Bold);
                Assert.False(run.Style.Italic);
                Assert.False(run.Style.Strike);
            });
    }

    [Fact]
    public void AnUnderscoreInsideAWordIsNotEscapedNeedlessly()
    {
        // Correct either way, but a document full of \_ is unreadable as text,
        // and being readable as text is the only reason to use Markdown.
        var document = Document(Text("snake_case_name"));

        Assert.DoesNotContain("\\_", Write(document), StringComparison.Ordinal);
    }

    [Fact]
    public void AUrlWithSpacesAndBracketsStaysOneLink()
    {
        var document = Document(Text(
            "link",
            new CharStyle { Link = "https://example.com/a path (with parens)" }));

        var run = Assert.IsType<TextRun>(
            Assert.IsType<Paragraph>(RoundTrip(document).Blocks[0]).Inlines[0]);

        Assert.NotNull(run.Style.Link);
        Assert.Contains("example.com", run.Style.Link!, StringComparison.Ordinal);
        Assert.Equal("link", run.Text);
    }

    // --- images and the sidecar --------------------------------------------

    [Fact]
    public void AnImageGoesToASidecarFolderAndComesBackWithItsBytes()
    {
        var folder = Path.Combine(
            Path.GetTempPath(),
            "sp-md-" + Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(folder);

        try
        {
            var path = Path.Combine(folder, "doc.md");
            var png = MinimalPng();

            var document = Document(new Paragraph
            {
                Inlines = [new ImageRun(png, "image/png", 48, 24, "a red dot")],
            });

            using (var file = File.Create(path))
            {
                Format.Write(document, file, path, CancellationToken.None);
            }

            var sidecar = Path.Combine(folder, ImageFolderFor(path));

            Assert.True(Directory.Exists(sidecar), "no sidecar folder was written");

            var written = Directory.GetFiles(sidecar);

            Assert.Single(written);
            Assert.Equal(png, File.ReadAllBytes(written[0]));

            // And the link in the text resolves back to those bytes.
            using var read = File.OpenRead(path);
            var after = Format.Read(read, path, CancellationToken.None);

            var image = Assert.IsType<ImageRun>(
                Assert.IsType<Paragraph>(after.Document.Blocks[0]).Inlines[0]);

            Assert.Equal(png, image.Data);
            Assert.Equal("a red dot", image.Alt);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void ASecondSaveDoesNotLeaveTheFirstSavesImagesBehind()
    {
        var folder = Path.Combine(
            Path.GetTempPath(),
            "sp-md-" + Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(folder);

        try
        {
            var path = Path.Combine(folder, "doc.md");

            var three = Document(new Paragraph
            {
                Inlines =
                [
                    new ImageRun(MinimalPng(), "image/png", 10, 10),
                    new ImageRun(MinimalPng(), "image/png", 10, 10),
                    new ImageRun(MinimalPng(), "image/png", 10, 10),
                ],
            });

            using (var file = File.Create(path))
            {
                Format.Write(three, file, path, CancellationToken.None);
            }

            var sidecar = Path.Combine(folder, ImageFolderFor(path));

            Assert.Equal(3, Directory.GetFiles(sidecar).Length);

            // Something the user put there, which must survive the next save.
            var theirs = Path.Combine(sidecar, "notes.txt");
            File.WriteAllText(theirs, "mine");

            var one = Document(new Paragraph
            {
                Inlines = [new ImageRun(MinimalPng(), "image/png", 10, 10)],
            });

            using (var file = File.Create(path))
            {
                Format.Write(one, file, path, CancellationToken.None);
            }

            Assert.Single(Directory.GetFiles(sidecar, "image*.*"));
            Assert.True(File.Exists(theirs), "a file the user put in the folder was deleted");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void AnImageOnTheWebIsNotFetched()
    {
        // An opened document is untrusted (Section 7.5): a Markdown file that
        // names a picture on a web server must not make the app go and get it.
        const string text = "![remote](https://example.com/picture.png)\n";

        using var input = new MemoryStream(Encoding.UTF8.GetBytes(text));
        var result = Format.Read(input, "test.md", CancellationToken.None);

        Assert.DoesNotContain(
            result.Document.Blocks.OfType<Paragraph>().SelectMany(p => p.Inlines),
            i => i is ImageRun);

        Assert.NotEmpty(result.Warnings);
    }

    [Fact]
    public void AnImagePathThatClimbsOutOfTheFolderIsRefused()
    {
        var folder = Path.Combine(
            Path.GetTempPath(),
            "sp-md-" + Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(folder);

        try
        {
            var path = Path.Combine(folder, "doc.md");

            File.WriteAllText(path, "![out](../../../Windows/System32/drivers/etc/hosts)\n");

            using var read = File.OpenRead(path);
            var result = Format.Read(read, path, CancellationToken.None);

            Assert.DoesNotContain(
                result.Document.Blocks.OfType<Paragraph>().SelectMany(p => p.Inlines),
                i => i is ImageRun);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    // --- robustness ---------------------------------------------------------

    [Fact]
    public void AnEmptyFileReadsAsAnEmptyDocument()
    {
        using var input = new MemoryStream([]);
        var result = Format.Read(input, "test.md", CancellationToken.None);

        Assert.NotNull(result.Document);
    }

    [Fact]
    public void MarkdownFeaturesTheAppHasNoPlaceForDoNotCrashIt()
    {
        const string text = """
            ---
            title: front matter
            ---

            # Heading

            A footnote[^1] and an inline `code span` and a <span>tag</span>.

            [^1]: the note itself

            - [ ] a task item
            - [x] a finished one

            Term
            : a definition

            $$ x^2 $$
            """;

        using var input = new MemoryStream(Encoding.UTF8.GetBytes(text));
        var result = Format.Read(input, "test.md", CancellationToken.None);

        Assert.NotEmpty(result.Document.Blocks);
        Assert.Contains("Heading", AllText(result.Document), StringComparison.Ordinal);
    }

    [Fact]
    public void AListNestedDeeperThanTheIrAllowsIsClampedNotRefused()
    {
        var text = new StringBuilder();

        for (var level = 0; level < 14; level++)
        {
            text.Append(new string(' ', level * 2)).Append("- item\n");
        }

        using var input = new MemoryStream(Encoding.UTF8.GetBytes(text.ToString()));
        var result = Format.Read(input, "test.md", CancellationToken.None);

        var items = result.Document.Blocks.OfType<Paragraph>()
            .Where(p => p.List is not null)
            .ToList();

        Assert.NotEmpty(items);
        Assert.All(items, p => Assert.InRange(p.List!.Level, 0, 8));
    }

    [Fact]
    public void APathologicallyNestedFileIsRefusedCleanly()
    {
        // Markdig stops rather than overflowing the stack, and that has to
        // reach the user as a bad file (Section 7.5) rather than as an
        // ArgumentException, which the open dialog can only report as "could
        // not be opened".
        var text = new StringBuilder();

        for (var level = 0; level < 400; level++)
        {
            text.Append(new string(' ', level * 2)).Append("- item\n");
        }

        using var input = new MemoryStream(Encoding.UTF8.GetBytes(text.ToString()));

        var error = Record.Exception(
            () => Format.Read(input, "test.md", CancellationToken.None));

        // Either it reads it or it refuses it clearly; what it must not do is
        // throw something the open flow cannot explain.
        if (error is not null)
        {
            Assert.IsType<InvalidDataException>(error);
            output.WriteLine(error.Message);
        }
    }

    [Fact]
    public void EverythingAtOnceSurvivesWhatItCan()
    {
        var document = new SpDocument(
            [
                Text("The Title", styleName: "Title"),
                Text("Heading", styleName: "Heading 2"),
                new Paragraph
                {
                    Inlines =
                    [
                        new TextRun("Mixed ", new CharStyle()),
                        new TextRun("bold", new CharStyle { Bold = true }),
                        new TextRun(" and a ", new CharStyle()),
                        new TextRun("link", new CharStyle { Link = "https://example.com/" }),
                        new TextRun(".", new CharStyle()),
                    ],
                },
                Text("quoted", styleName: "Quote"),
                Text("bullet") with { List = new ListInfo(ListKind.Bullet, 0) },
                Text("numbered") with { List = new ListInfo(ListKind.Number, 0) },
                new HorizontalRule(),
                new Table(
                    [[new Cell([Text("one")]), new Cell([Text("two")])]],
                    ColWidthsPt: null),
                Text("The end."),
            ],
            new Dictionary<string, string>(),
            new PageSetup(),
            new HashSet<string>());

        var after = RoundTrip(document);

        Assert.Equal("Heading 2", after.Blocks.OfType<Paragraph>()
            .First(p => AllText(p) == "Heading").StyleName);
        Assert.Contains(after.Blocks, b => b is Table);
        Assert.Contains(after.Blocks, b => b is HorizontalRule);
        Assert.Contains(
            after.Blocks.OfType<Paragraph>().SelectMany(p => p.Inlines).OfType<TextRun>(),
            r => r.Text == "bold" && r.Style.Bold);
        Assert.Contains(
            after.Blocks.OfType<Paragraph>().SelectMany(p => p.Inlines).OfType<TextRun>(),
            r => r.Style.Link is not null);
    }

    // --- helpers ------------------------------------------------------------

    /// <summary>
    /// Where the writer puts a document's pictures. Spelled out here rather
    /// than taken from the writer, so a change to the naming shows up as a
    /// failing test instead of agreeing with itself.
    /// </summary>
    private static string ImageFolderFor(string markdownPath) =>
        Path.GetFileNameWithoutExtension(markdownPath) + "_images";

    private static string Write(SpDocument document)
    {
        using var output = new MemoryStream();

        // A path in the temp folder, so a document with no image writes no
        // folder anywhere near the test output.
        Format.Write(
            document,
            output,
            Path.Combine(Path.GetTempPath(), "sp-md-write", "test.md"),
            CancellationToken.None);

        return Encoding.UTF8.GetString(output.ToArray());
    }

    private static string AllText(SpDocument document) =>
        string.Join(
            "\n",
            IrNormalizer.Normalize(document).Blocks.OfType<Paragraph>().Select(AllText));

    private static string AllText(Paragraph paragraph) =>
        string.Concat(paragraph.Inlines.OfType<TextRun>().Select(r => r.Text));

    private static SpDocument Document(params Block[] blocks) =>
        new(blocks, new Dictionary<string, string>(), new PageSetup(), new HashSet<string>());

    private static Paragraph Text(string text, CharStyle? style = null, string styleName = "Normal") =>
        new()
        {
            StyleName = styleName,
            Inlines = [new TextRun(text, style ?? new CharStyle())],
        };

    /// <summary>
    /// All of a cell's text, joined. Not the first run: an escape inside a
    /// cell splits it into several, so "a|b" arrives as three.
    /// </summary>
    private static string FirstText(Cell cell) =>
        string.Concat(
            cell.Blocks.OfType<Paragraph>()
                .SelectMany(p => p.Inlines)
                .OfType<TextRun>()
                .Select(r => r.Text));

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
