using System.IO;
using System.Windows.Controls;
using ScratchpadLite.App.Editor;
using ScratchpadLite.Core.Formats;
using ScratchpadLite.Core.Model;
using Xunit;
using Xunit.Abstractions;
using IrParagraph = ScratchpadLite.Core.Model.Paragraph;
using IrTable = ScratchpadLite.Core.Model.Table;

namespace ScratchpadLite.App.Tests;

/// <summary>
/// The Phase 6 gate: what the editing commands build survives a save and a
/// reopen, in every format that claims to carry it.
/// </summary>
/// <remarks>
/// The other test files check that a command does the right thing to the
/// document. These check the thing that actually matters to someone using the
/// app: that a list typed by hand is still a list tomorrow. The two are not the
/// same test - a table can be built perfectly and still be written out with
/// the wrong number of cells.
/// <para>
/// Each case is built with the real commands, walked to IR by the real bridge,
/// written by the real format handler, read back, and compared. Only what the
/// fidelity matrix promises for that format is asserted: Markdown has no cell
/// shading, and expecting it there would be testing the matrix rather than the
/// code.
/// </para>
/// </remarks>
[Collection(nameof(AppHost))]
public class EditedContentRoundTripTests(ITestOutputHelper output)
{
    private static readonly StyleSheet Styles = StyleSheet.Default;

    /// <summary>The formats that carry everything in Section 6.9.</summary>
    public static TheoryData<string> FullFormats => [".docx", ".odt"];

    /// <summary>Every format that can hold a list.</summary>
    public static TheoryData<string> ListFormats => [".docx", ".odt", ".md"];

    // ------------------------------------------------------------- lists ----

    [Theory]
    [MemberData(nameof(ListFormats))]
    public void ABulletedListTypedInTheEditorSurvivesASave(string extension)
    {
        var ir = InEditor(editor =>
        {
            Type(editor, "one");
            ListCommands.ToggleBullets(editor);

            NewParagraph(editor);
            Type(editor, "two");
        });

        var after = RoundTrip(ir, extension);
        var items = after.Blocks.OfType<IrParagraph>().Where(p => p.List is not null).ToList();

        Assert.Equal(2, items.Count);
        Assert.All(items, item => Assert.Equal(ListKind.Bullet, item.List!.Kind));
    }

    [Theory]
    [MemberData(nameof(ListFormats))]
    public void ANestedListTypedInTheEditorKeepsItsLevels(string extension)
    {
        var ir = InEditor(editor =>
        {
            Type(editor, "one");
            ListCommands.ToggleBullets(editor);

            NewParagraph(editor);
            Type(editor, "two");
            ListCommands.Indent(editor);

            NewParagraph(editor);
            Type(editor, "three");
        });

        var after = RoundTrip(ir, extension);
        var levels = after.Blocks.OfType<IrParagraph>()
            .Where(p => p.List is not null)
            .Select(p => p.List!.Level)
            .ToList();

        output.WriteLine(string.Join(", ", levels));

        Assert.Equal(3, levels.Count);
        Assert.Equal(0, levels[0]);
        Assert.Equal(1, levels[1]);
        Assert.Equal(1, levels[2]);
    }

    [Theory]
    [MemberData(nameof(ListFormats))]
    public void ANumberedListTypedInTheEditorSurvivesASave(string extension)
    {
        var ir = InEditor(editor =>
        {
            Type(editor, "one");
            ListCommands.ToggleNumbering(editor);

            NewParagraph(editor);
            Type(editor, "two");
        });

        var after = RoundTrip(ir, extension);

        Assert.All(
            after.Blocks.OfType<IrParagraph>().Where(p => p.List is not null),
            item => Assert.Equal(ListKind.Number, item.List!.Kind));
    }

    [Theory]
    [MemberData(nameof(FullFormats))]
    public void RestartedNumberingSurvivesASave(string extension)
    {
        var ir = InEditor(editor =>
        {
            Type(editor, "one");
            ListCommands.ToggleNumbering(editor);

            NewParagraph(editor);
            Type(editor, "two");

            ListCommands.RestartNumbering(editor);
        });

        var after = RoundTrip(ir, extension);
        var items = after.Blocks.OfType<IrParagraph>().Where(p => p.List is not null).ToList();

        Assert.Equal(2, items.Count);

        // The second item is the first of a new list, so a reader sees two
        // lists each starting at one - which is what a restart means in every
        // format.
        Assert.Equal(1, items[^1].List!.Start);
    }

    // ------------------------------------------------------------ tables ----

    [Theory]
    [MemberData(nameof(FullFormats))]
    public void ATableInsertedInTheEditorSurvivesASave(string extension)
    {
        var ir = InEditor(editor =>
        {
            TableCommands.Insert(editor, rows: 2, columns: 3, borders: true);
            Type(editor, "first cell");
        });

        var table = Assert.IsType<IrTable>(Assert.Single(RoundTrip(ir, extension).Blocks, b => b is IrTable));

        Assert.Equal(2, table.Rows.Count);
        Assert.All(table.Rows, row => Assert.Equal(3, row.Count));
        Assert.Contains("first cell", CellText(table.Rows[0][0]), StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(FullFormats))]
    public void AMergedCellSurvivesASave(string extension)
    {
        var ir = InEditor(editor =>
        {
            TableCommands.Insert(editor, 2, 2, borders: true);

            var rows = FirstTable(editor).RowGroups[0].Rows;

            TableCommands.BeginDrag(editor, rows[0].Cells[0].ContentStart);
            editor.Selection.Select(rows[0].Cells[0].ContentStart, rows[0].Cells[1].ContentEnd);
            TableCommands.EndDrag(editor, rows[0].Cells[1].ContentEnd);

            TableCommands.Merge(editor);
        });

        var table = Assert.IsType<IrTable>(Assert.Single(RoundTrip(ir, extension).Blocks, b => b is IrTable));

        output.WriteLine(IrNormalizer.Describe(IrNormalizer.Normalize(ir)));

        Assert.Equal(2, table.Rows[0][0].ColSpan);
    }

    [Theory]
    [MemberData(nameof(FullFormats))]
    public void AVerticallyMergedCellSurvivesASave(string extension)
    {
        var ir = InEditor(editor =>
        {
            TableCommands.Insert(editor, 2, 2, borders: true);

            var rows = FirstTable(editor).RowGroups[0].Rows;

            TableCommands.BeginDrag(editor, rows[0].Cells[0].ContentStart);
            editor.Selection.Select(rows[0].Cells[0].ContentStart, rows[1].Cells[0].ContentEnd);
            TableCommands.EndDrag(editor, rows[1].Cells[0].ContentEnd);

            TableCommands.Merge(editor);
        });

        var table = Assert.IsType<IrTable>(Assert.Single(RoundTrip(ir, extension).Blocks, b => b is IrTable));

        Assert.Equal(2, table.Rows[0][0].RowSpan);
        Assert.Single(table.Rows[1]);
    }

    [Theory]
    [MemberData(nameof(FullFormats))]
    public void CellShadingSetInTheEditorSurvivesASave(string extension)
    {
        var ir = InEditor(editor =>
        {
            TableCommands.Insert(editor, 1, 2, borders: true);
            TableCommands.SetShading(editor, System.Windows.Media.Colors.Yellow);
        });

        var table = Assert.IsType<IrTable>(Assert.Single(RoundTrip(ir, extension).Blocks, b => b is IrTable));

        Assert.Equal("#FFFF00", table.Rows[0][0].Shading, ignoreCase: true);
    }

    [Theory]
    [MemberData(nameof(FullFormats))]
    public void ATableWithBordersTurnedOffSurvivesASave(string extension)
    {
        var ir = InEditor(editor =>
        {
            TableCommands.Insert(editor, 2, 2, borders: true);
            TableCommands.ToggleBorders(editor);
        });

        var table = Assert.IsType<IrTable>(Assert.Single(RoundTrip(ir, extension).Blocks, b => b is IrTable));

        Assert.False(table.Borders);
    }

    // ------------------------------------------------------------ images ----

    [Theory]
    [MemberData(nameof(FullFormats))]
    public void APictureInsertedInTheEditorKeepsItsBytesThroughASave(string extension)
    {
        var png = MinimalPng();

        var ir = InEditor(editor => ImageSupport.Insert(editor, png, "image/png", "a red dot"));

        var image = Assert.IsType<ImageRun>(
            RoundTrip(ir, extension).Blocks
                .OfType<IrParagraph>()
                .SelectMany(p => p.Inlines)
                .First(i => i is ImageRun));

        // The same bytes, not a re-encode: this is the whole reason the
        // original bytes are carried on the image.
        Assert.Equal(png, image.Data);
        Assert.Equal("a red dot", image.Alt);
    }

    [Fact]
    public void APictureSurvivesAMarkdownSaveThroughItsSidecarFolder()
    {
        var png = MinimalPng();
        var ir = InEditor(editor => ImageSupport.Insert(editor, png, "image/png", "a red dot"));

        var folder = Path.Combine(Path.GetTempPath(), "sp-p6-" + Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(folder);

        try
        {
            var path = Path.Combine(folder, "doc.md");
            var handler = FormatRegistry.For(".md")!;

            using (var file = File.Create(path))
            {
                handler.Write(ir, file, path, CancellationToken.None);
            }

            using var read = File.OpenRead(path);
            var after = handler.Read(read, path, CancellationToken.None);

            var image = Assert.IsType<ImageRun>(
                after.Document.Blocks
                    .OfType<IrParagraph>()
                    .SelectMany(p => p.Inlines)
                    .First(i => i is ImageRun));

            Assert.Equal(png, image.Data);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Theory]
    [MemberData(nameof(FullFormats))]
    public void APicturesSizeSetInThePropertiesDialogSurvivesASave(string extension)
    {
        var ir = InEditor(editor =>
        {
            ImageSupport.Insert(editor, MinimalPng(), "image/png");
            ImageSupport.SetProperties(editor, ImageSupport.ImageAt(editor)!, 144, 72, "sized");
        });

        var image = Assert.IsType<ImageRun>(
            RoundTrip(ir, extension).Blocks
                .OfType<IrParagraph>()
                .SelectMany(p => p.Inlines)
                .First(i => i is ImageRun));

        Assert.Equal(144, image.WidthPt, 1);
        Assert.Equal(72, image.HeightPt, 1);
        Assert.Equal("sized", image.Alt);
    }

    // ------------------------------------------------------------- links ----

    [Theory]
    [MemberData(nameof(ListFormats))]
    public void ALinkMadeInTheEditorSurvivesASave(string extension)
    {
        var ir = InEditor(editor =>
        {
            Type(editor, "the site");
            editor.Selection.Select(editor.Document.ContentStart, editor.Document.ContentEnd);

            LinkSupport.InsertOrEdit(editor, "https://example.com/page", null);
        });

        var run = RoundTrip(ir, extension).Blocks
            .OfType<IrParagraph>()
            .SelectMany(p => p.Inlines)
            .OfType<TextRun>()
            .First(r => r.Style.Link is not null);

        Assert.Equal("the site", run.Text);
        Assert.Contains("example.com/page", run.Style.Link!, StringComparison.Ordinal);
    }

    // ----------------------------------------------------- all together ----

    [Theory]
    [MemberData(nameof(FullFormats))]
    public void ADocumentUsingAllOfPhaseSixSurvivesASave(string extension)
    {
        var ir = InEditor(editor =>
        {
            Type(editor, "A bulleted item");
            ListCommands.ToggleBullets(editor);

            NewParagraph(editor);
            Type(editor, "Nested");
            ListCommands.Indent(editor);

            // Out of the list, then the rest.
            editor.CaretPosition = editor.Document.ContentEnd;
            NewParagraph(editor);
            ListCommands.EndListOnEmptyItem(editor);

            Type(editor, "Body text with ");
            editor.Selection.Select(
                editor.CaretPosition.DocumentStart,
                editor.CaretPosition);

            TableCommands.Insert(editor, 2, 2, borders: true);
            Type(editor, "in a cell");

            editor.CaretPosition = editor.Document.ContentEnd;
            ImageSupport.Insert(editor, MinimalPng(), "image/png", "a dot");

            LinkSupport.InsertOrEdit(editor, "https://example.com/", "a link");
        });

        var after = RoundTrip(ir, extension);
        var inlines = after.Blocks.OfType<IrParagraph>().SelectMany(p => p.Inlines).ToList();

        output.WriteLine(IrNormalizer.Describe(IrNormalizer.Normalize(after)));

        Assert.Contains(after.Blocks.OfType<IrParagraph>(), p => p.List is not null);
        Assert.Contains(after.Blocks, b => b is IrTable);
        Assert.Contains(inlines, i => i is ImageRun);
        Assert.Contains(inlines.OfType<TextRun>(), r => r.Style.Link is not null);
    }

    // ---------------------------------------------------------- helpers ----

    /// <summary>
    /// Writes the document in one format, reads it back, and returns what came
    /// back.
    /// </summary>
    private SpDocument RoundTrip(SpDocument document, string extension)
    {
        var handler = FormatRegistry.For(extension)
                      ?? throw new InvalidOperationException($"no handler for {extension}");

        if (handler is Core.Formats.Docx.DocxFormat docx)
        {
            docx.Styles = Styles;
        }

        if (handler is Core.Formats.Odt.OdtFormat odt)
        {
            odt.Styles = Styles;
        }

        using var file = new MemoryStream();

        handler.Write(document, file, "test" + extension, CancellationToken.None);
        file.Position = 0;

        var result = handler.Read(file, "test" + extension, CancellationToken.None);

        foreach (var warning in result.Warnings)
        {
            output.WriteLine($"{extension} warning: {warning}");
        }

        return result.Document;
    }

    private static SpDocument InEditor(Action<RichTextBox> edit) =>
        AppHost.InFocusedEditor(editor =>
        {
            edit(editor);

            return Bridge.FlowWalker.Walk(editor.Document, Styles);
        });

    private static void Type(RichTextBox editor, string text) =>
        editor.CaretPosition.InsertTextInRun(text);

    private static void NewParagraph(RichTextBox editor)
    {
        editor.CaretPosition = editor.Document.ContentEnd;
        editor.CaretPosition = editor.CaretPosition.InsertParagraphBreak();
    }

    private static System.Windows.Documents.Table FirstTable(RichTextBox editor) =>
        editor.Document.Blocks.OfType<System.Windows.Documents.Table>().First();

    private static string CellText(Cell cell) =>
        string.Concat(
                cell.Blocks.OfType<IrParagraph>()
                    .SelectMany(p => p.Inlines)
                    .OfType<TextRun>()
                    .Select(r => r.Text))
            .Trim();

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
