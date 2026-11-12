using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using ScratchpadLite.Core.Model;
using ScratchpadLite.Core.Platform;
using WpfParagraph = System.Windows.Documents.Paragraph;
using WpfTable = System.Windows.Documents.Table;

namespace ScratchpadLite.App.Editor;

/// <summary>
/// The Table menu (Section 8.1), enabled only when the caret sits inside a
/// table: insert row above or below, insert column left or right, delete
/// row, column or table, merge and split cells, cell shading, and borders
/// on or off.
/// </summary>
/// <remarks>
/// WPF has no table editing commands whatsoever, so all of this is written
/// against the <see cref="WpfTable"/>, <see cref="TableRow"/> and
/// <see cref="TableCell"/> objects directly, each operation wrapped in
/// <c>BeginChange</c>/<c>EndChange</c> so one Ctrl+Z undoes one action.
/// <para>
/// The thing that makes table editing harder than it looks is that a WPF row
/// does not hold a cell per column. A cell with <c>RowSpan</c> 2 occupies its
/// own row and the one below, and the row below simply has one cell fewer -
/// there is no placeholder. So a cell's column number cannot be read off its
/// index in the row: it has to be worked out by laying the rows out in a grid
/// and accounting for what the rows above have taken. <see cref="Grid"/> does
/// that once, and every operation here works from it.
/// </para>
/// </remarks>
internal static class TableCommands
{
    /// <summary>A table may not be bigger than this (Section 7.5).</summary>
    internal const int MaxRows = 500;

    /// <summary>Columns, likewise. A pipe table of 64 is already unreadable.</summary>
    internal const int MaxColumns = 64;

    internal static bool CaretIsInTable(RichTextBox editor) =>
        editor is not null && CellAt(editor.Selection.Start) is not null;

    // ------------------------------------------------------------- insert ---

    internal static void Insert(RichTextBox editor, int rows, int columns, bool borders)
    {
        ArgumentNullException.ThrowIfNull(editor);

        rows = Math.Clamp(rows, 1, MaxRows);
        columns = Math.Clamp(columns, 1, MaxColumns);

        var table = new WpfTable
        {
            CellSpacing = 0,
            Margin = new Thickness(0, 0, 0, Units.PointsToDips(8)),
        };

        for (var c = 0; c < columns; c++)
        {
            table.Columns.Add(new TableColumn { Width = GridLength.Auto });
        }

        var group = new TableRowGroup();
        table.RowGroups.Add(group);

        for (var r = 0; r < rows; r++)
        {
            var row = new TableRow();

            for (var c = 0; c < columns; c++)
            {
                row.Cells.Add(NewCell(borders));
            }

            group.Rows.Add(row);
        }

        editor.BeginChange();

        try
        {
            var paragraph = editor.Selection.Start.Paragraph;

            if (paragraph?.SiblingBlocks is { } siblings)
            {
                siblings.InsertAfter(paragraph, table);

                // A table at the very end of a document cannot be typed past,
                // and in .docx it is not even valid, so there is always a
                // paragraph after one.
                if (table.NextBlock is null)
                {
                    siblings.InsertAfter(table, new WpfParagraph());
                }
            }
            else
            {
                editor.Document.Blocks.Add(table);
                editor.Document.Blocks.Add(new WpfParagraph());
            }

            // The caret goes into the first cell, which is where typing should
            // carry on.
            if (FirstParagraph(table) is { } first)
            {
                editor.CaretPosition = first.ContentStart;
            }
        }
        finally
        {
            editor.EndChange();
        }
    }

    internal static void InsertRow(RichTextBox editor, bool above)
    {
        ArgumentNullException.ThrowIfNull(editor);

        if (CellAt(editor.Selection.Start) is not { } cell
            || RowOf(cell) is not { } row
            || GroupOf(row) is not { } group)
        {
            return;
        }

        var grid = Grid.Of(TableOf(cell));

        if (group.Rows.Count >= MaxRows)
        {
            return;
        }

        editor.BeginChange();

        try
        {
            var index = group.Rows.IndexOf(row);
            var at = above ? index : index + 1;

            // As many cells as the table is wide, so the new row lines up with
            // the others however they are merged.
            var fresh = new TableRow();

            for (var c = 0; c < grid.Columns; c++)
            {
                fresh.Cells.Add(NewCell(HasBorders(cell)));
            }

            if (at >= group.Rows.Count)
            {
                group.Rows.Add(fresh);
            }
            else
            {
                group.Rows.Insert(at, fresh);
            }

            // A cell above whose RowSpan reached across the new row has to grow
            // by one, or the table loses a column's worth of width at that row.
            foreach (var spanning in grid.CellsCrossing(at, above))
            {
                spanning.RowSpan++;

                // That cell now covers the new row, so the new row needs one
                // fewer cell of its own.
                if (fresh.Cells.Count > 0)
                {
                    fresh.Cells.RemoveAt(fresh.Cells.Count - 1);
                }
            }
        }
        finally
        {
            editor.EndChange();
        }
    }

    internal static void InsertColumn(RichTextBox editor, bool left)
    {
        ArgumentNullException.ThrowIfNull(editor);

        if (CellAt(editor.Selection.Start) is not { } cell)
        {
            return;
        }

        var table = TableOf(cell);
        var grid = Grid.Of(table);

        if (grid.Columns >= MaxColumns || grid.ColumnOf(cell) is not { } column)
        {
            return;
        }

        var at = left ? column : column + Math.Max(1, cell.ColumnSpan);

        editor.BeginChange();

        try
        {
            if (at >= table.Columns.Count)
            {
                table.Columns.Add(new TableColumn { Width = GridLength.Auto });
            }
            else
            {
                table.Columns.Insert(at, new TableColumn { Width = GridLength.Auto });
            }

            foreach (var row in AllRows(table))
            {
                // Where the new column falls inside a cell that spans across
                // it, that cell grows instead of a new one appearing - which is
                // what every other editor does, and the only way to keep the
                // row widths consistent.
                if (grid.CellCovering(row, at) is { } covering && grid.ColumnOf(covering) < at)
                {
                    covering.ColumnSpan++;
                    continue;
                }

                var index = grid.IndexInRowFor(row, at);
                var fresh = NewCell(HasBorders(cell));

                if (index >= row.Cells.Count)
                {
                    row.Cells.Add(fresh);
                }
                else
                {
                    row.Cells.Insert(index, fresh);
                }
            }
        }
        finally
        {
            editor.EndChange();
        }
    }

    // ------------------------------------------------------------- delete ---

    internal static void DeleteRow(RichTextBox editor)
    {
        ArgumentNullException.ThrowIfNull(editor);

        if (CellAt(editor.Selection.Start) is not { } cell
            || RowOf(cell) is not { } row
            || GroupOf(row) is not { } group)
        {
            return;
        }

        var table = TableOf(cell);

        if (group.Rows.Count <= 1)
        {
            DeleteTable(editor);

            return;
        }

        var grid = Grid.Of(table);
        var index = group.Rows.IndexOf(row);

        editor.BeginChange();

        try
        {
            // A cell in this row that reaches into the rows below has to leave
            // its content somewhere: it moves down one row and keeps the rest
            // of its span.
            foreach (var spanning in row.Cells.Where(c => c.RowSpan > 1).ToList())
            {
                if (index + 1 < group.Rows.Count)
                {
                    row.Cells.Remove(spanning);
                    spanning.RowSpan--;
                    group.Rows[index + 1].Cells.Insert(
                        Math.Min(
                            grid.IndexInRowFor(group.Rows[index + 1], grid.ColumnOf(spanning) ?? 0),
                            group.Rows[index + 1].Cells.Count),
                        spanning);
                }
            }

            // And one from above that reached into this row loses a row of its
            // span.
            foreach (var spanning in grid.CellsCovering(index).Where(c => !ReferenceEquals(RowOf(c), row)))
            {
                spanning.RowSpan = Math.Max(1, spanning.RowSpan - 1);
            }

            group.Rows.Remove(row);

            if (FirstParagraph(table) is { } first)
            {
                editor.CaretPosition = first.ContentStart;
            }
        }
        finally
        {
            editor.EndChange();
        }
    }

    internal static void DeleteColumn(RichTextBox editor)
    {
        ArgumentNullException.ThrowIfNull(editor);

        if (CellAt(editor.Selection.Start) is not { } cell)
        {
            return;
        }

        var table = TableOf(cell);
        var grid = Grid.Of(table);

        if (grid.Columns <= 1)
        {
            DeleteTable(editor);

            return;
        }

        if (grid.ColumnOf(cell) is not { } column)
        {
            return;
        }

        editor.BeginChange();

        try
        {
            foreach (var row in AllRows(table))
            {
                if (grid.CellCovering(row, column) is not { } target)
                {
                    continue;
                }

                if (target.ColumnSpan > 1)
                {
                    // It spans the column being removed and others, so it just
                    // narrows.
                    target.ColumnSpan--;
                }
                else
                {
                    row.Cells.Remove(target);
                }
            }

            if (column < table.Columns.Count)
            {
                table.Columns.RemoveAt(column);
            }

            if (FirstParagraph(table) is { } first)
            {
                editor.CaretPosition = first.ContentStart;
            }
        }
        finally
        {
            editor.EndChange();
        }
    }

    internal static void DeleteTable(RichTextBox editor)
    {
        ArgumentNullException.ThrowIfNull(editor);

        if (CellAt(editor.Selection.Start) is not { } cell)
        {
            return;
        }

        var table = TableOf(cell);

        if (table.SiblingBlocks is not { } siblings)
        {
            return;
        }

        editor.BeginChange();

        try
        {
            // Somewhere to put the caret afterwards, since a document must
            // never be left with no block at all.
            var after = table.NextBlock ?? table.PreviousBlock;

            if (after is null)
            {
                var paragraph = new WpfParagraph();
                siblings.InsertAfter(table, paragraph);
                after = paragraph;
            }

            siblings.Remove(table);
            editor.CaretPosition = after.ContentStart;
        }
        finally
        {
            editor.EndChange();
        }
    }

    // -------------------------------------------------------------- merge ---

    /// <summary>Whether the selection covers more than one cell of one table.</summary>
    internal static bool CanMerge(RichTextBox editor) =>
        editor is not null && SelectedCells(editor).Count > 1;

    /// <summary>
    /// Merges the selected cells into one, keeping their content
    /// (Section 6.9).
    /// </summary>
    /// <remarks>
    /// Only a rectangle can be merged, which is the same rule Word has. The
    /// content of the cells being absorbed is moved into the first one rather
    /// than thrown away - a merge should never lose writing, even when what it
    /// produces is a cell with four paragraphs in it.
    /// </remarks>
    internal static void Merge(RichTextBox editor)
    {
        ArgumentNullException.ThrowIfNull(editor);

        var cells = SelectedCells(editor);

        if (cells.Count < 2)
        {
            return;
        }

        var table = TableOf(cells[0]);
        var grid = Grid.Of(table);

        if (grid.Bounds(cells) is not { } bounds)
        {
            return;
        }

        var first = cells[0];

        editor.BeginChange();

        try
        {
            foreach (var absorbed in cells.Skip(1))
            {
                foreach (var block in absorbed.Blocks.ToList())
                {
                    absorbed.Blocks.Remove(block);

                    // An empty paragraph from an empty cell would only add
                    // blank lines to the merged cell.
                    if (block is WpfParagraph { Inlines.Count: 0 })
                    {
                        continue;
                    }

                    first.Blocks.Add(block);
                }

                RowOf(absorbed)?.Cells.Remove(absorbed);
            }

            first.ColumnSpan = bounds.Width;
            first.RowSpan = bounds.Height;

            if (first.Blocks.Count == 0)
            {
                first.Blocks.Add(new WpfParagraph());
            }

            editor.CaretPosition = first.ContentStart;
        }
        finally
        {
            editor.EndChange();
        }
    }

    /// <summary>Whether the cell at the caret is a merged one.</summary>
    internal static bool CanSplit(RichTextBox editor) =>
        editor is not null
        && CellAt(editor.Selection.Start) is { } cell
        && (cell.ColumnSpan > 1 || cell.RowSpan > 1);

    /// <summary>
    /// Undoes a merge: the cell shrinks to one and empty cells fill the space
    /// it gave up.
    /// </summary>
    internal static void Split(RichTextBox editor)
    {
        ArgumentNullException.ThrowIfNull(editor);

        if (CellAt(editor.Selection.Start) is not { } cell
            || RowOf(cell) is not { } row
            || (cell.ColumnSpan <= 1 && cell.RowSpan <= 1))
        {
            return;
        }

        var table = TableOf(cell);
        var grid = Grid.Of(table);
        var column = grid.ColumnOf(cell) ?? 0;
        var rows = AllRows(table);
        var index = rows.IndexOf(row);
        var borders = HasBorders(cell);
        var width = Math.Max(1, cell.ColumnSpan);
        var height = Math.Max(1, cell.RowSpan);

        editor.BeginChange();

        try
        {
            cell.ColumnSpan = 1;
            cell.RowSpan = 1;

            // The rest of this cell's own row.
            for (var c = 1; c < width; c++)
            {
                row.Cells.Insert(
                    Math.Min(row.Cells.IndexOf(cell) + c, row.Cells.Count),
                    NewCell(borders));
            }

            // And the rows it used to reach into.
            for (var r = 1; r < height && index + r < rows.Count; r++)
            {
                var below = rows[index + r];
                var at = Math.Min(grid.IndexInRowFor(below, column), below.Cells.Count);

                for (var c = 0; c < width; c++)
                {
                    below.Cells.Insert(Math.Min(at + c, below.Cells.Count), NewCell(borders));
                }
            }
        }
        finally
        {
            editor.EndChange();
        }
    }

    // ------------------------------------------------------- looks ----------

    internal static void SetShading(RichTextBox editor, Color? color)
    {
        ArgumentNullException.ThrowIfNull(editor);

        var cells = SelectedCells(editor);

        if (cells.Count == 0)
        {
            return;
        }

        editor.BeginChange();

        try
        {
            foreach (var cell in cells)
            {
                cell.Background = color is { } actual
                    ? new SolidColorBrush(actual) { Opacity = 1 }
                    : null;
            }
        }
        finally
        {
            editor.EndChange();
        }
    }

    /// <summary>The shading of the cell at the caret, for the colour picker.</summary>
    internal static Color? ShadingAt(RichTextBox editor) =>
        editor is not null
        && CellAt(editor.Selection.Start)?.Background is SolidColorBrush brush
            ? brush.Color
            : null;

    internal static void ToggleBorders(RichTextBox editor)
    {
        ArgumentNullException.ThrowIfNull(editor);

        if (CellAt(editor.Selection.Start) is not { } cell)
        {
            return;
        }

        var table = TableOf(cell);
        var on = !HasBorders(cell);

        editor.BeginChange();

        try
        {
            foreach (var row in AllRows(table))
            {
                foreach (var each in row.Cells)
                {
                    each.BorderThickness = on ? new Thickness(0.5) : new Thickness(0);

                    if (on)
                    {
                        each.BorderBrush = new SolidColorBrush(Colors.Gray);
                    }
                }
            }
        }
        finally
        {
            editor.EndChange();
        }
    }

    // ------------------------------------------------------------ the keys ---

    /// <summary>
    /// Tab inside a table moves to the next cell, and from the last cell adds
    /// a row (Section 6.9).
    /// </summary>
    /// <returns>False when the caret is not in a table, so Tab can do its usual job.</returns>
    internal static bool MoveToNextCell(RichTextBox editor, bool backwards)
    {
        ArgumentNullException.ThrowIfNull(editor);

        if (CellAt(editor.Selection.Start) is not { } cell)
        {
            return false;
        }

        var table = TableOf(cell);
        var cells = AllRows(table).SelectMany(r => r.Cells).ToList();
        var index = cells.IndexOf(cell);

        if (index < 0)
        {
            return false;
        }

        var next = backwards ? index - 1 : index + 1;

        if (next < 0)
        {
            return true;        // before the first cell: stay put
        }

        if (next >= cells.Count)
        {
            // Tab in the last cell adds a row, which is how a table gets
            // filled in without reaching for the menu.
            InsertRow(editor, above: false);

            cells = AllRows(table).SelectMany(r => r.Cells).ToList();

            if (next >= cells.Count)
            {
                return true;
            }
        }

        if (FirstParagraphOf(cells[next]) is { } paragraph)
        {
            // Selecting the cell's text, rather than only moving the caret, is
            // what Word does and makes tabbing through a filled table useful.
            editor.Selection.Select(paragraph.ContentStart, paragraph.ContentEnd);
        }

        return true;
    }

    // ------------------------------------------------------------ walking ---

    /// <summary>The cell a pointer is inside, or null.</summary>
    internal static TableCell? CellAt(TextPointer? pointer)
    {
        for (DependencyObject? node = pointer?.Parent; node is not null; node = Parent(node))
        {
            if (node is TableCell cell)
            {
                return cell;
            }
        }

        return null;
    }

    /// <summary>
    /// Every cell the selection touches, in document order.
    /// </summary>
    /// <remarks>
    /// A selection inside one cell gives that cell, so the shading commands
    /// work from the caret alone.
    /// </remarks>
    /// <summary>
    /// The two cells a mouse drag went between, remembered so a merge knows
    /// what was really selected.
    /// </summary>
    /// <remarks>
    /// This exists because WPF's <see cref="RichTextBox.Selection"/> cannot
    /// say. A selection is a stretch of a document, and WPF widens one that
    /// crosses cells to whole rows: dragging down the left column of a
    /// two-by-two table leaves a selection that starts in the top-left cell
    /// and ends in the <em>bottom-right</em> one, which is indistinguishable
    /// from selecting the whole table. Merging what that describes would
    /// quietly merge four cells when two were chosen.
    /// <para>
    /// So the editor records the cell under the pointer when a drag begins and
    /// when it ends, and a merge uses those two corners. A selection made with
    /// the keyboard has no drag, and falls back to what WPF reports.
    /// </para>
    /// </remarks>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<
        RichTextBox,
        DragCorners> Drags = new();

    private sealed class DragCorners
    {
        internal TableCell? From { get; set; }

        internal TableCell? To { get; set; }
    }

    /// <summary>Records where a drag started. Called on mouse down.</summary>
    internal static void BeginDrag(RichTextBox editor, TextPointer? at)
    {
        ArgumentNullException.ThrowIfNull(editor);

        var corners = Drags.GetOrCreateValue(editor);

        corners.From = CellAt(at);
        corners.To = null;
    }

    /// <summary>Records where a drag ended. Called on mouse up.</summary>
    internal static void EndDrag(RichTextBox editor, TextPointer? at)
    {
        ArgumentNullException.ThrowIfNull(editor);

        if (Drags.TryGetValue(editor, out var corners) && corners.From is not null)
        {
            corners.To = CellAt(at);
        }
    }

    internal static List<TableCell> SelectedCells(RichTextBox editor)
    {
        var start = editor.Selection.Start;
        var end = editor.Selection.End;

        if (CellAt(start) is not { } anchor)
        {
            return [];
        }

        // What the mouse actually swept, where there was a mouse.
        if (Drags.TryGetValue(editor, out var drag)
            && drag is { From: { } from, To: { } to }
            && !ReferenceEquals(from, to)
            && RowOf(from) is not null
            && RowOf(to) is not null
            && ReferenceEquals(TableOf(from), TableOf(to)))
        {
            return Grid.Of(TableOf(from)).Rectangle(from, to);
        }

        // Asking the table which of its cells overlap the range, rather than
        // asking the range which cells its ends are in. WPF normalises a
        // selection that crosses cells, and its End can come back positioned
        // on the row rather than inside the last cell - so reading the two
        // ends found one cell where two were selected, and Merge did nothing.
        var table = TableOf(anchor);

        // The far end of the selection, which WPF sometimes normalises to a
        // position on the row rather than inside the last cell - so when the
        // cast comes back empty the last cell before it is used instead.
        var tail = CellAt(end)
                   ?? AllRows(table)
                       .SelectMany(r => r.Cells)
                       .LastOrDefault(cell => cell.ContentStart.CompareTo(end) <= 0)
                   ?? anchor;

        if (ReferenceEquals(anchor, tail))
        {
            return [anchor];
        }

        // The rectangle those two corners span, rather than every cell between
        // them in document order. A document reads a table row by row, so
        // dragging down one column of a two-by-two table passes through the
        // cell to the right of the first on the way: taking the run of cells
        // as given merged three when two were selected. Word treats a table
        // selection as a rectangle, and so does every format's idea of a
        // merge.
        return Grid.Of(table).Rectangle(anchor, tail);
    }

    /// <summary>
    /// The row a cell belongs to.
    /// </summary>
    /// <remarks>
    /// WPF gives a TableCell no Row property, although TableRow has Cells and
    /// TableRow has a RowGroup: the link upwards is only the generic content
    /// Parent, so it has to be cast on the way up.
    /// </remarks>
    internal static TableRow? RowOf(TableCell cell) => cell.Parent as TableRow;

    /// <summary>
    /// The row group a row belongs to, and the table above that. The whole
    /// chain upwards is generic Parent properties rather than typed ones.
    /// </summary>
    internal static TableRowGroup? GroupOf(TableRow? row) => row?.Parent as TableRowGroup;

    internal static WpfTable TableOf(TableCell cell) =>
        GroupOf(RowOf(cell))?.Parent as WpfTable
        ?? throw new InvalidOperationException("a table cell with no table above it");

    private static List<TableRow> AllRows(WpfTable table) =>
        table.RowGroups.SelectMany(g => g.Rows).ToList();

    private static WpfParagraph? FirstParagraph(WpfTable table) =>
        AllRows(table).SelectMany(r => r.Cells).Select(FirstParagraphOf).FirstOrDefault(p => p is not null);

    private static WpfParagraph? FirstParagraphOf(TableCell cell) =>
        cell.Blocks.OfType<WpfParagraph>().FirstOrDefault();

    private static bool HasBorders(TableCell cell) => cell.BorderThickness.Left > 0;

    private static TableCell NewCell(bool borders)
    {
        var cell = new TableCell(new WpfParagraph())
        {
            Padding = new Thickness(Units.PointsToDips(4)),
            BorderThickness = borders ? new Thickness(0.5) : new Thickness(0),
        };

        if (borders)
        {
            cell.BorderBrush = new SolidColorBrush(Colors.Gray);
        }

        return cell;
    }

    private static DependencyObject? Parent(DependencyObject node) => node switch
    {
        FrameworkContentElement element => element.Parent,
        FrameworkElement element => element.Parent,
        _ => null,
    };

    /// <summary>
    /// Where every cell of a table actually sits, once row spans are taken
    /// into account.
    /// </summary>
    /// <remarks>
    /// This is the piece that makes the rest possible. A WPF row holds only the
    /// cells that start in it, so the third cell of a row can be the fifth
    /// column when a cell above it spans two columns and reaches down. Working
    /// that out per operation would mean the same loop written six times,
    /// slightly differently each time.
    /// </remarks>
    private sealed class Grid
    {
        private readonly Dictionary<TableCell, (int Row, int Column)> _at = [];
        private readonly List<List<TableCell?>> _occupied = [];

        internal int Columns { get; private set; }

        internal static Grid Of(WpfTable table)
        {
            var grid = new Grid();
            var rows = AllRows(table);

            // Laid out row by row, with a cell claiming the squares its spans
            // cover so the rows below know what is already taken.
            for (var r = 0; r < rows.Count; r++)
            {
                grid.EnsureRow(r);

                var column = 0;

                foreach (var cell in rows[r].Cells)
                {
                    while (grid.Taken(r, column))
                    {
                        column++;
                    }

                    grid._at[cell] = (r, column);

                    for (var dr = 0; dr < Math.Max(1, cell.RowSpan); dr++)
                    {
                        for (var dc = 0; dc < Math.Max(1, cell.ColumnSpan); dc++)
                        {
                            grid.Claim(r + dr, column + dc, cell);
                        }
                    }

                    column += Math.Max(1, cell.ColumnSpan);
                }
            }

            grid.Columns = Math.Max(
                table.Columns.Count,
                grid._occupied.Count == 0 ? 0 : grid._occupied.Max(row => row.Count));

            return grid;
        }

        internal int? ColumnOf(TableCell cell) =>
            _at.TryGetValue(cell, out var at) ? at.Column : null;

        /// <summary>The cell occupying this square of this row, if any.</summary>
        internal TableCell? CellCovering(TableRow row, int column)
        {
            var index = row.Cells.Count == 0 ? -1 : _at.TryGetValue(row.Cells[0], out var at) ? at.Row : -1;

            if (index < 0 || index >= _occupied.Count || column >= _occupied[index].Count)
            {
                return null;
            }

            return _occupied[index][column];
        }

        /// <summary>
        /// Where in this row's own cell list a cell at this column would go.
        /// </summary>
        internal int IndexInRowFor(TableRow row, int column)
        {
            var index = 0;

            foreach (var cell in row.Cells)
            {
                if ((ColumnOf(cell) ?? int.MaxValue) >= column)
                {
                    return index;
                }

                index++;
            }

            return row.Cells.Count;
        }

        /// <summary>Cells that occupy this row but did not start in it.</summary>
        internal IEnumerable<TableCell> CellsCovering(int row)
        {
            if (row < 0 || row >= _occupied.Count)
            {
                yield break;
            }

            foreach (var cell in _occupied[row].Where(c => c is not null).Distinct())
            {
                yield return cell!;
            }
        }

        /// <summary>
        /// Cells whose row span would be cut in two by a row inserted here.
        /// </summary>
        internal IEnumerable<TableCell> CellsCrossing(int insertedAt, bool above)
        {
            // A row inserted above the caret's row splits any span that starts
            // earlier and reaches at least this far; one inserted below splits
            // a span that reaches past it.
            var boundary = above ? insertedAt : insertedAt - 1;

            foreach (var (cell, at) in _at)
            {
                if (at.Row >= boundary)
                {
                    continue;
                }

                if (at.Row + Math.Max(1, cell.RowSpan) - 1 >= boundary)
                {
                    yield return cell;
                }
            }
        }

        /// <summary>
        /// Every cell inside the rectangle these two corners span.
        /// </summary>
        internal List<TableCell> Rectangle(TableCell one, TableCell other)
        {
            if (!_at.ContainsKey(one) || !_at.ContainsKey(other))
            {
                return [one];
            }

            var known = new[] { one, other };

            var top = known.Min(c => _at[c].Row);
            var left = known.Min(c => _at[c].Column);
            var bottom = known.Max(c => _at[c].Row + Math.Max(1, c.RowSpan) - 1);
            var right = known.Max(c => _at[c].Column + Math.Max(1, c.ColumnSpan) - 1);

            // In document order, which is the order a merge needs: the first
            // one keeps the content.
            return _at
                .Where(pair => pair.Value.Row >= top && pair.Value.Row <= bottom
                               && pair.Value.Column >= left && pair.Value.Column <= right)
                .OrderBy(pair => pair.Value.Row)
                .ThenBy(pair => pair.Value.Column)
                .Select(pair => pair.Key)
                .ToList();
        }

        /// <summary>
        /// The rectangle a set of cells covers, or null when they do not make
        /// one.
        /// </summary>
        internal (int Width, int Height)? Bounds(IReadOnlyList<TableCell> cells)
        {
            var known = cells.Where(c => _at.ContainsKey(c)).ToList();

            if (known.Count == 0)
            {
                return null;
            }

            var top = known.Min(c => _at[c].Row);
            var left = known.Min(c => _at[c].Column);
            var bottom = known.Max(c => _at[c].Row + Math.Max(1, c.RowSpan) - 1);
            var right = known.Max(c => _at[c].Column + Math.Max(1, c.ColumnSpan) - 1);

            return (right - left + 1, bottom - top + 1);
        }

        private bool Taken(int row, int column) =>
            row < _occupied.Count && column < _occupied[row].Count && _occupied[row][column] is not null;

        private void Claim(int row, int column, TableCell cell)
        {
            EnsureRow(row);

            while (_occupied[row].Count <= column)
            {
                _occupied[row].Add(null);
            }

            _occupied[row][column] = cell;
        }

        private void EnsureRow(int row)
        {
            while (_occupied.Count <= row)
            {
                _occupied.Add([]);
            }
        }
    }
}
