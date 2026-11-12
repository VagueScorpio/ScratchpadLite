using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Documents;
using ScratchpadLite.App.Editor;
using ScratchpadLite.Core.Model;
using Xunit;
using Xunit.Abstractions;
using IrParagraph = ScratchpadLite.Core.Model.Paragraph;
using IrTable = ScratchpadLite.Core.Model.Table;
using WpfList = System.Windows.Documents.List;
using WpfParagraph = System.Windows.Documents.Paragraph;
using WpfTable = System.Windows.Documents.Table;

namespace ScratchpadLite.App.Tests;

/// <summary>
/// The list and table editing commands (Section 6.9).
/// </summary>
/// <remarks>
/// Each test drives the commands the way the menus do, then checks the
/// document, and in most cases also walks it into IR - because what a command
/// builds matters less than what a save would write. A table that looks right
/// on screen and comes out with the wrong number of cells is still a bug, and
/// the IR is where that shows.
/// <para>
/// All of it runs on the Application thread, since a FlowDocument belongs to
/// the thread that made it.
/// </para>
/// </remarks>
[Collection(nameof(AppHost))]
public class ListAndTableTests(ITestOutputHelper output)
{
    private static readonly StyleSheet Styles = StyleSheet.Default;

    // ----------------------------------------------------------- lists ------

    [Fact]
    public void TogglingBulletsMakesAList()
    {
        var ir = InEditor(editor =>
        {
            Type(editor, "one");
            ListCommands.ToggleBullets(editor);
        });

        var paragraph = Assert.IsType<IrParagraph>(ir.Blocks[0]);

        Assert.NotNull(paragraph.List);
        Assert.Equal(ListKind.Bullet, paragraph.List!.Kind);
        Assert.Equal(0, paragraph.List!.Level);
    }

    [Fact]
    public void TogglingBulletsAgainTakesTheListOff()
    {
        var ir = InEditor(editor =>
        {
            Type(editor, "one");
            ListCommands.ToggleBullets(editor);
            ListCommands.ToggleBullets(editor);
        });

        Assert.Null(Assert.IsType<IrParagraph>(ir.Blocks[0]).List);
    }

    [Fact]
    public void TogglingNumberingMakesANumberedList()
    {
        var ir = InEditor(editor =>
        {
            Type(editor, "one");
            ListCommands.ToggleNumbering(editor);
        });

        Assert.Equal(ListKind.Number, Assert.IsType<IrParagraph>(ir.Blocks[0]).List!.Kind);
    }

    [Fact]
    public void EachLevelOfABulletedListGetsItsOwnMarker()
    {
        // The reason ListCommands wraps WPF's commands at all: WPF keeps one
        // marker for a whole nested list, and three levels of identical discs
        // cannot be told apart.
        var markers = AppHost.InFocusedEditor(editor =>
        {
            Type(editor, "one");
            ListCommands.ToggleBullets(editor);

            NewParagraph(editor);
            Type(editor, "two");
            ListCommands.Indent(editor);

            NewParagraph(editor);
            Type(editor, "three");
            ListCommands.Indent(editor);

            return AllLists(editor).Select(l => l.MarkerStyle).ToList();
        });

        output.WriteLine(string.Join(", ", markers));

        Assert.Equal(3, markers.Count);
        Assert.Equal(TextMarkerStyle.Disc, markers[0]);
        Assert.Equal(TextMarkerStyle.Circle, markers[1]);
        Assert.Equal(TextMarkerStyle.Square, markers[2]);
    }

    [Fact]
    public void TabIndentsAndShiftTabOutdents()
    {
        var levels = AppHost.InFocusedEditor(editor =>
        {
            Type(editor, "one");
            ListCommands.ToggleBullets(editor);

            NewParagraph(editor);
            Type(editor, "two");

            var before = Deepest(editor);

            Assert.True(ListCommands.Indent(editor));
            var indented = Deepest(editor);

            Assert.True(ListCommands.Outdent(editor));
            var back = Deepest(editor);

            return (before, indented, back);
        });

        Assert.Equal(0, levels.before);
        Assert.Equal(1, levels.indented);
        Assert.Equal(0, levels.back);
    }

    [Fact]
    public void TabOutsideAListIsNotTakenOver()
    {
        // So Tab still inserts a tab in ordinary text.
        var handled = AppHost.InFocusedEditor(editor =>
        {
            Type(editor, "plain");

            return ListCommands.Indent(editor) || ListCommands.Outdent(editor);
        });

        Assert.False(handled);
    }

    [Fact]
    public void EnterOnAnEmptyItemEndsTheList()
    {
        var ir = InEditor(editor =>
        {
            Type(editor, "one");
            ListCommands.ToggleBullets(editor);

            // A second, empty item, which is where Enter is pressed.
            NewParagraph(editor);

            Assert.True(ListCommands.EndListOnEmptyItem(editor));
        });

        var paragraphs = ir.Blocks.OfType<IrParagraph>().ToList();

        Assert.NotNull(paragraphs[0].List);
        Assert.Null(paragraphs[^1].List);
    }

    [Fact]
    public void EnterOnAnEmptyNestedItemStepsOutOneLevel()
    {
        var ir = InEditor(editor =>
        {
            Type(editor, "one");
            ListCommands.ToggleBullets(editor);

            NewParagraph(editor);
            Type(editor, "two");
            ListCommands.Indent(editor);

            NewParagraph(editor);

            Assert.True(ListCommands.EndListOnEmptyItem(editor));
        });

        // Still a list, one level shallower, rather than body text.
        var last = ir.Blocks.OfType<IrParagraph>().Last();

        Assert.NotNull(last.List);
        Assert.Equal(0, last.List!.Level);
    }

    [Fact]
    public void EnterOnAnItemWithTextIsLeftAlone()
    {
        var handled = AppHost.InFocusedEditor(editor =>
        {
            Type(editor, "one");
            ListCommands.ToggleBullets(editor);

            return ListCommands.EndListOnEmptyItem(editor);
        });

        Assert.False(handled);
    }

    [Fact]
    public void RestartingNumberingSplitsTheListAndStartsAgainAtOne()
    {
        var ir = InEditor(editor =>
        {
            foreach (var text in new[] { "one", "two", "three" })
            {
                if (text != "one")
                {
                    NewParagraph(editor);
                }

                Type(editor, text);
            }

            editor.Selection.Select(editor.Document.ContentStart, editor.Document.ContentEnd);
            ListCommands.ToggleNumbering(editor);

            // The caret on the third item, which is where numbering restarts.
            editor.CaretPosition = ItemParagraph(editor, 2).ContentStart;

            ListCommands.RestartNumbering(editor);
        });

        var items = ir.Blocks.OfType<IrParagraph>().Where(p => p.List is not null).ToList();

        Assert.Equal(3, items.Count);
        Assert.Equal(1, items[0].List!.Start);
        Assert.Equal(1, items[2].List!.Start);

        output.WriteLine(IrNormalizer.Describe(IrNormalizer.Normalize(ir)));
    }

    [Fact]
    public void ContinuingNumberingJoinsTheListBackTogether()
    {
        var count = AppHost.InFocusedEditor(editor =>
        {
            foreach (var text in new[] { "one", "two" })
            {
                if (text != "one")
                {
                    NewParagraph(editor);
                }

                Type(editor, text);
            }

            editor.Selection.Select(editor.Document.ContentStart, editor.Document.ContentEnd);
            ListCommands.ToggleNumbering(editor);

            editor.CaretPosition = ItemParagraph(editor, 1).ContentStart;

            ListCommands.RestartNumbering(editor);
            var afterSplit = AllLists(editor).Count;

            // The caret has to be in the second list for Continue to have
            // something to join back.
            editor.CaretPosition = AllLists(editor)[^1].ListItems.FirstListItem!
                .Blocks.OfType<WpfParagraph>().First().ContentStart;
            ListCommands.ContinueNumbering(editor);

            return (afterSplit, afterJoin: AllLists(editor).Count);
        });

        Assert.Equal(2, count.afterSplit);
        Assert.Equal(1, count.afterJoin);
    }

    // ---------------------------------------------------------- tables ------

    [Fact]
    public void InsertingATableGivesTheRightShape()
    {
        var ir = InEditor(editor => TableCommands.Insert(editor, rows: 3, columns: 4, borders: true));

        var table = Assert.IsType<IrTable>(Assert.Single(ir.Blocks, b => b is IrTable));

        Assert.Equal(3, table.Rows.Count);
        Assert.All(table.Rows, row => Assert.Equal(4, row.Count));
        Assert.True(table.Borders);
    }

    [Fact]
    public void ATableIsFollowedByAParagraph()
    {
        // Word will not open a .docx whose body ends with a table, and a table
        // at the very end of a document cannot be typed past.
        var ir = InEditor(editor => TableCommands.Insert(editor, 2, 2, borders: true));

        var tableAt = ir.Blocks.ToList().FindIndex(b => b is IrTable);

        Assert.True(tableAt >= 0);
        Assert.True(
            ir.Blocks.Count > tableAt + 1 && ir.Blocks[tableAt + 1] is IrParagraph,
            "a table was left as the last block of the document");
    }

    [Fact]
    public void TheCaretLandsInTheFirstCell()
    {
        var inTable = AppHost.InFocusedEditor(editor =>
        {
            TableCommands.Insert(editor, 2, 2, borders: true);

            return TableCommands.CaretIsInTable(editor);
        });

        Assert.True(inTable);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void InsertingARowAddsOne(bool above)
    {
        var ir = InEditor(editor =>
        {
            TableCommands.Insert(editor, 2, 3, borders: true);
            TableCommands.InsertRow(editor, above);
        });

        var table = Assert.IsType<IrTable>(Assert.Single(ir.Blocks, b => b is IrTable));

        Assert.Equal(3, table.Rows.Count);
        Assert.All(table.Rows, row => Assert.Equal(3, row.Count));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void InsertingAColumnAddsOneToEveryRow(bool left)
    {
        var ir = InEditor(editor =>
        {
            TableCommands.Insert(editor, 2, 2, borders: true);
            TableCommands.InsertColumn(editor, left);
        });

        var table = Assert.IsType<IrTable>(Assert.Single(ir.Blocks, b => b is IrTable));

        Assert.All(table.Rows, row => Assert.Equal(3, row.Count));
    }

    [Fact]
    public void InsertingAColumnKeepsTheTextWhereItWas()
    {
        var ir = InEditor(editor =>
        {
            TableCommands.Insert(editor, 1, 2, borders: true);
            Type(editor, "first");

            TableCommands.InsertColumn(editor, left: true);
        });

        var table = Assert.IsType<IrTable>(Assert.Single(ir.Blocks, b => b is IrTable));
        var texts = table.Rows[0].Select(CellText).ToList();

        output.WriteLine(string.Join(" | ", texts.Select(t => $"\"{t}\"")));

        Assert.Equal(3, texts.Count);
        Assert.Equal("", texts[0]);
        Assert.Equal("first", texts[1]);
    }

    [Fact]
    public void DeletingARowRemovesOne()
    {
        var ir = InEditor(editor =>
        {
            TableCommands.Insert(editor, 3, 2, borders: true);
            TableCommands.DeleteRow(editor);
        });

        Assert.Equal(2, Assert.IsType<IrTable>(Assert.Single(ir.Blocks, b => b is IrTable)).Rows.Count);
    }

    [Fact]
    public void DeletingAColumnRemovesOne()
    {
        var ir = InEditor(editor =>
        {
            TableCommands.Insert(editor, 2, 3, borders: true);
            TableCommands.DeleteColumn(editor);
        });

        var table = Assert.IsType<IrTable>(Assert.Single(ir.Blocks, b => b is IrTable));

        Assert.All(table.Rows, row => Assert.Equal(2, row.Count));
    }

    [Fact]
    public void DeletingTheLastRowDeletesTheTable()
    {
        // Rather than leaving a table with no rows in it, which neither WPF nor
        // any format can describe.
        var ir = InEditor(editor =>
        {
            TableCommands.Insert(editor, 1, 2, borders: true);
            TableCommands.DeleteRow(editor);
        });

        Assert.DoesNotContain(ir.Blocks, b => b is IrTable);
    }

    [Fact]
    public void DeletingTheTableLeavesSomewhereToType()
    {
        var ir = InEditor(editor =>
        {
            TableCommands.Insert(editor, 2, 2, borders: true);
            TableCommands.DeleteTable(editor);
        });

        Assert.DoesNotContain(ir.Blocks, b => b is IrTable);
        Assert.NotEmpty(ir.Blocks.OfType<IrParagraph>());
    }

    [Fact]
    public void MergingTwoCellsKeepsBothTextsInOne()
    {
        var ir = InEditor(editor =>
        {
            TableCommands.Insert(editor, 1, 2, borders: true);

            var table = FirstTable(editor);
            var row = table.RowGroups[0].Rows[0];

            Fill(row.Cells[0], "left");
            Fill(row.Cells[1], "right");

            editor.Selection.Select(row.Cells[0].ContentStart, row.Cells[1].ContentEnd);

            Assert.True(TableCommands.CanMerge(editor));
            TableCommands.Merge(editor);
        });

        var merged = Assert.IsType<IrTable>(Assert.Single(ir.Blocks, b => b is IrTable));
        var cell = Assert.Single(merged.Rows[0]);

        Assert.Equal(2, cell.ColSpan);
        Assert.Contains("left", CellText(cell), StringComparison.Ordinal);
        Assert.Contains("right", CellText(cell), StringComparison.Ordinal);
    }

    [Fact]
    public void SplittingAMergedCellPutsTheCellsBack()
    {
        var ir = InEditor(editor =>
        {
            TableCommands.Insert(editor, 2, 2, borders: true);

            var table = FirstTable(editor);
            var row = table.RowGroups[0].Rows[0];

            editor.Selection.Select(row.Cells[0].ContentStart, row.Cells[1].ContentEnd);
            TableCommands.Merge(editor);

            editor.CaretPosition = FirstTable(editor).RowGroups[0].Rows[0].Cells[0].ContentStart;

            Assert.True(TableCommands.CanSplit(editor));
            TableCommands.Split(editor);
        });

        var table = Assert.IsType<IrTable>(Assert.Single(ir.Blocks, b => b is IrTable));

        Assert.All(table.Rows, row => Assert.Equal(2, row.Count));
        Assert.All(table.Rows.SelectMany(r => r), cell => Assert.Equal(1, cell.ColSpan));
    }

    [Fact]
    public void MergingDownKeepsTheRowsBelowTheRightWidth()
    {
        // The case the layout grid exists for: a cell spanning two rows means
        // the row below holds one cell fewer, and getting that wrong shows up
        // as a table whose rows have different widths.
        var ir = InEditor(editor =>
        {
            TableCommands.Insert(editor, 2, 2, borders: true);

            var rows = FirstTable(editor).RowGroups[0].Rows;

            // Through the drag corners, the way the mouse does it: WPF widens a
            // cross-row selection to whole rows, so the selection alone
            // cannot say that one column was chosen.
            TableCommands.BeginDrag(editor, rows[0].Cells[0].ContentStart);
            editor.Selection.Select(rows[0].Cells[0].ContentStart, rows[1].Cells[0].ContentEnd);
            TableCommands.EndDrag(editor, rows[1].Cells[0].ContentEnd);

            TableCommands.Merge(editor);
        });

        var table = Assert.IsType<IrTable>(Assert.Single(ir.Blocks, b => b is IrTable));

        output.WriteLine(IrNormalizer.Describe(IrNormalizer.Normalize(ir)));

        Assert.Equal(2, table.Rows[0][0].RowSpan);
        Assert.Equal(2, table.Rows[0].Count);
        Assert.Single(table.Rows[1]);
    }

    [Fact]
    public void ShadingACellSurvivesToTheIr()
    {
        var ir = InEditor(editor =>
        {
            TableCommands.Insert(editor, 1, 2, borders: true);
            TableCommands.SetShading(editor, System.Windows.Media.Colors.Yellow);
        });

        var table = Assert.IsType<IrTable>(Assert.Single(ir.Blocks, b => b is IrTable));

        Assert.Equal("#FFFF00", table.Rows[0][0].Shading, ignoreCase: true);
    }

    [Fact]
    public void TurningBordersOffSurvivesToTheIr()
    {
        var ir = InEditor(editor =>
        {
            TableCommands.Insert(editor, 2, 2, borders: true);
            TableCommands.ToggleBorders(editor);
        });

        Assert.False(Assert.IsType<IrTable>(Assert.Single(ir.Blocks, b => b is IrTable)).Borders);
    }

    [Fact]
    public void TabMovesToTheNextCell()
    {
        var moved = AppHost.InFocusedEditor(editor =>
        {
            TableCommands.Insert(editor, 1, 2, borders: true);

            var row = FirstTable(editor).RowGroups[0].Rows[0];
            Fill(row.Cells[1], "second");

            var handled = TableCommands.MoveToNextCell(editor, backwards: false);
            var cell = TableCommands.CellAt(editor.Selection.Start);

            return (handled, inSecond: ReferenceEquals(cell, row.Cells[1]));
        });

        Assert.True(moved.handled);
        Assert.True(moved.inSecond);
    }

    [Fact]
    public void TabInTheLastCellAddsARow()
    {
        var ir = InEditor(editor =>
        {
            TableCommands.Insert(editor, 1, 2, borders: true);

            // Into the last cell, then Tab.
            TableCommands.MoveToNextCell(editor, backwards: false);
            Assert.True(TableCommands.MoveToNextCell(editor, backwards: false));
        });

        Assert.Equal(2, Assert.IsType<IrTable>(Assert.Single(ir.Blocks, b => b is IrTable)).Rows.Count);
    }

    [Fact]
    public void TabOutsideATableIsNotTakenOver()
    {
        var handled = AppHost.InFocusedEditor(editor =>
        {
            Type(editor, "plain");

            return TableCommands.MoveToNextCell(editor, backwards: false);
        });

        Assert.False(handled);
    }

    [Fact]
    public void TheTableCommandsDoNothingOutsideATable()
    {
        // Every one of them is guarded by CanExecute in the menu, but a
        // command can also be raised from a shortcut or by a test, and none of
        // them may throw.
        AppHost.InFocusedEditor(editor =>
        {
            Type(editor, "not a table");

            TableCommands.InsertRow(editor, above: true);
            TableCommands.InsertColumn(editor, left: true);
            TableCommands.DeleteRow(editor);
            TableCommands.DeleteColumn(editor);
            TableCommands.DeleteTable(editor);
            TableCommands.Merge(editor);
            TableCommands.Split(editor);
            TableCommands.SetShading(editor, System.Windows.Media.Colors.Red);
            TableCommands.ToggleBorders(editor);

            Assert.False(TableCommands.CaretIsInTable(editor));

            return true;
        });
    }

    [Fact]
    public void AnOutsizeTableIsClampedRatherThanBuilt()
    {
        var ir = InEditor(editor => TableCommands.Insert(editor, 10_000, 10_000, borders: false));

        var table = Assert.IsType<IrTable>(Assert.Single(ir.Blocks, b => b is IrTable));

        Assert.Equal(TableCommands.MaxRows, table.Rows.Count);
        Assert.Equal(TableCommands.MaxColumns, table.Rows[0].Count);
    }

    // --------------------------------------------------------- helpers ------

    /// <summary>
    /// Runs an edit in a fresh editor and returns the document as IR, which is
    /// what a save would write.
    /// </summary>
    private static SpDocument InEditor(Action<RichTextBox> edit) =>
        AppHost.InFocusedEditor(editor =>
        {
            edit(editor);

            return Bridge.FlowWalker.Walk(editor.Document, Styles);
        });

        /// <summary>
    /// Runs an edit on a focused editor inside a shown window.
    /// </summary>
    /// <remarks>
    /// Needed for anything that goes through WPF's own EditingCommands: their
    /// CanExecute is false unless the control has keyboard focus, and Execute
    /// on a command that cannot execute does nothing at all - so an indent
    /// test on a detached RichTextBox passes the call and changes nothing,
    /// which looks exactly like a broken indent.
    /// </remarks>
    private static void Type(RichTextBox editor, string text) =>
        editor.CaretPosition.InsertTextInRun(text);

    /// <summary>
    /// Starts a new paragraph and puts the caret in it - which is what
    /// pressing Enter does.
    /// </summary>
    /// <remarks>
    /// InsertParagraphBreak returns the new position and does not move the
    /// caret, so without this the next Type lands at the end of the old
    /// paragraph. That made a two-item list whose caret sat in item one, and
    /// every indent test then measured the wrong item.
    /// </remarks>
    private static void NewParagraph(RichTextBox editor)
    {
        editor.CaretPosition = editor.Document.ContentEnd;
        editor.CaretPosition = editor.CaretPosition.InsertParagraphBreak();
    }

    private static void Fill(TableCell cell, string text)
    {
        if (cell.Blocks.FirstBlock is WpfParagraph paragraph)
        {
            paragraph.Inlines.Add(new Run(text));
        }
    }

    private static WpfTable FirstTable(RichTextBox editor) =>
        editor.Document.Blocks.OfType<WpfTable>().First();

    private static List<WpfList> AllLists(RichTextBox editor)
    {
        var found = new List<WpfList>();

        void Walk(IEnumerable<System.Windows.Documents.Block> blocks)
        {
            foreach (var block in blocks)
            {
                if (block is WpfList list)
                {
                    found.Add(list);

                    foreach (var item in list.ListItems)
                    {
                        Walk(item.Blocks.Cast<System.Windows.Documents.Block>());
                    }
                }
            }
        }

        Walk(editor.Document.Blocks.Cast<System.Windows.Documents.Block>());

        return found;
    }

    /// <summary>
    /// The paragraph of the nth list item across all the lists, in document
    /// order. Document.ContentEnd.Paragraph is null, because that position is
    /// outside the List element.
    /// </summary>
    private static WpfParagraph ItemParagraph(RichTextBox editor, int index) =>
        AllLists(editor)
            .SelectMany(list => list.ListItems)
            .Select(item => item.Blocks.OfType<WpfParagraph>().FirstOrDefault())
            .Where(p => p is not null)
            .ElementAt(index)!;

    private static int Deepest(RichTextBox editor) =>
        ListCommands.ListAt(editor.Selection.Start) is { } list ? ListCommands.LevelOf(list) : -1;

    private static string CellText(Cell cell) =>
        string.Concat(
                cell.Blocks.OfType<IrParagraph>()
                    .SelectMany(p => p.Inlines)
                    .OfType<TextRun>()
                    .Select(r => r.Text))
            .Trim();
}
