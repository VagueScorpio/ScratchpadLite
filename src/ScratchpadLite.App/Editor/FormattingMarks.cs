using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace ScratchpadLite.App.Editor;

/// <summary>
/// Draws the formatting marks - a pilcrow at the end of each paragraph, a dot
/// for a space, an arrow for a tab, and a return mark for a line break (the
/// View menu's Show Formatting Marks, Section 8.1).
/// </summary>
/// <remarks>
/// <para>WPF offers nothing for this: a <c>RichTextBox</c> has no formatting
/// marks and no way to ask where a particular space ended up except to ask
/// the text pointer. Phase 1 said as much and left the menu item greyed out
/// rather than half-building it; this is that item, built for the release.
/// </para>
///
/// <para>It is an ordinary element sitting over the editor inside the same
/// paper border rather than an adorner, which matters for two reasons. An
/// adorner would go in the window's adorner layer, which is not clipped by
/// the editor's scroll viewer, so marks would be painted over the toolbar as
/// soon as the document scrolled. And an element in the same cell as the
/// text box shares its bounds exactly, so the rectangles that
/// <see cref="TextPointer.GetCharacterRect"/> hands back can be drawn where
/// they are without a transform.</para>
///
/// <para>Only the paragraphs on screen are drawn, and the per-character
/// marks - spaces and tabs - are only drawn while the visible text is under
/// <see cref="CharacterBudget"/> characters, because each one costs a
/// <c>GetCharacterRect</c> call and that is a layout query rather than a
/// lookup. Over the budget the pilcrows stay and the dots stop, which is the
/// part people turn the marks on for. The paragraph mark count is reported
/// in <see cref="LastMarkCount"/> so a test can see the budget working.</para>
/// </remarks>
internal sealed class FormattingMarks : FrameworkElement
{
    /// <summary>
    /// How much visible text still gets space and tab marks.
    /// </summary>
    /// <remarks>
    /// A backstop rather than a working limit. Only the text on screen is
    /// marked, and a screenful is a few thousand characters, so this is never
    /// reached in ordinary use - it is here for the case where there is no
    /// scroll viewer to say what "on screen" means, and for a window someone
    /// has made very large.
    /// </remarks>
    internal const int CharacterBudget = 20000;

    /// <summary>
    /// A non-breaking space, which gets a mark of its own: a line that will
    /// not break where it should is the usual reason to turn the marks on.
    /// Written as a number because the character itself is invisible in
    /// source and would read as an ordinary space.
    /// </summary>
    private const char HardSpace = (char)0x00A0;

    private readonly Dictionary<double, FormattedText> _pilcrows = [];

    private readonly Dictionary<double, FormattedText> _returns = [];

    private Brush _ink = Brushes.Gray;

    private Brush? _inkSource;

    private bool _enabled;

    public FormattingMarks()
    {
        IsHitTestVisible = false;
        Focusable = false;
    }

    /// <summary>The editor whose text is being marked.</summary>
    internal RichTextBox? Target { get; set; }

    /// <summary>
    /// The scroll viewer the editor sits in, used to work out which
    /// paragraphs are on screen. Without one, every paragraph is drawn.
    /// </summary>
    internal ScrollViewer? Viewport { get; set; }

    /// <summary>How many marks the last paint drew.</summary>
    internal int LastMarkCount { get; private set; }

    /// <summary>Whether the per-character marks fitted in the budget last paint.</summary>
    internal bool LastPaintWasFull { get; private set; }

    internal bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value)
            {
                return;
            }

            _enabled = value;
            InvalidateVisual();
        }
    }

    /// <summary>
    /// The colour of the marks: the text's own colour at a third strength, so
    /// they read as marks rather than as characters, in either theme and on
    /// dark paper.
    /// </summary>
    /// <remarks>
    /// Taken from the editor at paint time rather than pushed in when the
    /// theme changes. The theme sets the editor's foreground through a
    /// resource reference, which lands whenever WPF gets round to it, so
    /// anything that read it on the way past would be a frame behind.
    /// </remarks>
    internal void UseInk(Brush foreground)
    {
        ArgumentNullException.ThrowIfNull(foreground);

        if (!AdoptInk(foreground))
        {
            return;
        }

        Refresh();
    }

    /// <summary>
    /// Takes the ink from a foreground brush, and says whether anything
    /// changed. No repaint: this is called from the paint itself, where
    /// invalidating would start the paint again.
    /// </summary>
    private bool AdoptInk(Brush foreground)
    {
        if (ReferenceEquals(foreground, _inkSource))
        {
            return false;
        }

        _inkSource = foreground;

        var ink = foreground.Clone();
        ink.Opacity = 0.35;
        ink.Freeze();

        _ink = ink;
        _pilcrows.Clear();
        _returns.Clear();

        return true;
    }

    /// <summary>Repaints, for a caller that knows the text or the scroll moved.</summary>
    internal void Refresh()
    {
        if (Enabled)
        {
            InvalidateVisual();
        }
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);

        base.OnRender(drawingContext);

        LastMarkCount = 0;
        LastPaintWasFull = true;

        if (!Enabled || Target is not { IsLoaded: true } editor)
        {
            return;
        }

        try
        {
            Paint(drawingContext, editor);
        }
        catch (InvalidOperationException)
        {
            // The document changed under the walk - a background load
            // finishing, say. The next paint will be the right one, and
            // marks are not worth an exception dialog.
            LastMarkCount = 0;
        }
    }

    private void Paint(DrawingContext dc, RichTextBox editor)
    {
        if (editor.Foreground is { } foreground)
        {
            AdoptInk(foreground);
        }

        var (from, to) = VisibleText(editor);
        var budget = CharacterBudget;

        foreach (var paragraph in Paragraphs(editor.Document.Blocks))
        {
            // The paragraph is skipped only when it lies wholly outside the
            // visible text. Testing where it ends is not enough: one
            // paragraph can be taller than the window, and an earlier
            // version of this skipped exactly that case - the one paragraph
            // filling the screen - because its end was below the fold.
            if (paragraph.ContentEnd.CompareTo(from) < 0)
            {
                continue;
            }

            if (paragraph.ContentStart.CompareTo(to) > 0)
            {
                break;
            }

            var end = BoxOf(paragraph.ContentEnd);

            // Any paragraph that reaches the visible range gets its pilcrow,
            // including the one whose end is just past it. The position that
            // bounds the range is an insertion point inside the text, and a
            // paragraph's ContentEnd is always past the last of those - so
            // asking for the end to be inside the range left the last
            // paragraph on screen without its mark.
            if (!end.IsEmpty)
            {
                DrawPilcrow(dc, end, paragraph.FontSize);
            }

            if (budget <= 0)
            {
                LastPaintWasFull = false;

                continue;
            }

            budget -= MarkInside(dc, paragraph, budget, from, to);
        }
    }

    /// <summary>
    /// Marks the spaces, tabs and line breaks of one paragraph, and returns
    /// how much of the budget it used.
    /// </summary>
    private int MarkInside(
        DrawingContext dc, Paragraph paragraph, int budget, TextPointer from, TextPointer to)
    {
        var used = 0;
        var pointer = Later(paragraph.ContentStart, from);
        var end = Earlier(paragraph.ContentEnd, to);

        while (pointer is not null && pointer.CompareTo(end) < 0)
        {
            if (used >= budget)
            {
                LastPaintWasFull = false;

                break;
            }

            switch (pointer.GetPointerContext(LogicalDirection.Forward))
            {
                case TextPointerContext.Text:
                    // Clamped to the visible end, not to the end of the run:
                    // one run can be a whole chapter, and marking what is off
                    // screen is the cost this class is built to avoid.
                    var run = pointer.GetTextInRun(LogicalDirection.Forward);
                    var text = run[..Math.Clamp(pointer.GetOffsetToPosition(end), 0, run.Length)];

                    if (text.Length == 0)
                    {
                        // Nothing of this run is visible. Step over the
                        // context rather than by zero, which would not move.
                        pointer = pointer.GetNextContextPosition(LogicalDirection.Forward);

                        break;
                    }

                    for (var i = 0; i < text.Length && used + i < budget; i++)
                    {
                        if (text[i] is not (' ' or '\t' or HardSpace))
                        {
                            continue;
                        }

                        if (pointer.GetPositionAtOffset(i) is not { } at)
                        {
                            continue;
                        }

                        var box = BoxOf(at);

                        if (box.IsEmpty)
                        {
                            continue;
                        }

                        if (text[i] == '\t')
                        {
                            // A tab's own rectangle has no width to speak of -
                            // WPF reports the caret there, not the gap - so the
                            // arrow is drawn from here to wherever the next
                            // character starts.
                            var after = pointer.GetPositionAtOffset(i + 1) is { } next
                                ? BoxOf(next)
                                : Rect.Empty;

                            DrawTab(dc, box, after);
                        }
                        else
                        {
                            DrawSpace(dc, box, hard: text[i] == HardSpace);
                        }
                    }

                    used += text.Length;
                    pointer = pointer.GetPositionAtOffset(text.Length);

                    break;

                case TextPointerContext.ElementStart when pointer.GetAdjacentElement(
                    LogicalDirection.Forward) is LineBreak:
                    var rect = BoxOf(pointer);

                    if (!rect.IsEmpty)
                    {
                        DrawReturn(dc, rect, paragraph.FontSize);
                    }

                    used++;
                    pointer = pointer.GetNextContextPosition(LogicalDirection.Forward);

                    break;

                default:
                    pointer = pointer.GetNextContextPosition(LogicalDirection.Forward);

                    break;
            }
        }

        return used;
    }

    /// <summary>
    /// The first and last text on screen. Everything outside it is skipped,
    /// which is what keeps a paint proportional to the window rather than to
    /// the document: on a three-hundred-page file that is the difference
    /// between marking a page and marking a book.
    /// </summary>
    /// <remarks>
    /// The editor scrolls by being taller than its viewport rather than by
    /// scrolling itself, so its own coordinates are the document's. The
    /// visible rectangle is therefore the viewport's bounds mapped into the
    /// editor - which also takes the zoom into account, because the zoom is a
    /// layout transform between the two - and the two corners of it are
    /// turned into text positions by asking the editor what is drawn there.
    /// With no viewport, the whole document is visible, which is the right
    /// answer for a detached control and the one the budget then has to cope
    /// with.
    /// </remarks>
    private (TextPointer From, TextPointer To) VisibleText(RichTextBox editor)
    {
        var document = editor.Document;
        var whole = (document.ContentStart, document.ContentEnd);

        if (Viewport is not { ActualWidth: > 0, ActualHeight: > 0 } viewport)
        {
            return whole;
        }

        try
        {
            var band = viewport.TransformToVisual(editor)
                .TransformBounds(new Rect(0, 0, viewport.ActualWidth, viewport.ActualHeight));

            // A line's worth of slack at each end, so a mark on a line that is
            // only half on screen is not missing.
            band = Rect.Inflate(band, 0, 40);

            var from = editor.GetPositionFromPoint(new Point(2, band.Top), snapToText: true);
            var to = editor.GetPositionFromPoint(
                new Point(Math.Max(2, editor.ActualWidth - 2), band.Bottom), snapToText: true);

            return (from ?? document.ContentStart, to ?? document.ContentEnd);
        }
        catch (InvalidOperationException)
        {
            // Not in the same visual tree yet, or laid out mid-change.
            return whole;
        }
    }

    private static TextPointer Later(TextPointer a, TextPointer b) =>
        a.CompareTo(b) >= 0 ? a : b;

    private static TextPointer Earlier(TextPointer a, TextPointer b) =>
        a.CompareTo(b) <= 0 ? a : b;

    private static Rect BoxOf(TextPointer pointer)
    {
        try
        {
            var rect = pointer.GetCharacterRect(LogicalDirection.Forward);

            return rect.IsEmpty || double.IsNaN(rect.Top) ? Rect.Empty : rect;
        }
        catch (InvalidOperationException)
        {
            return Rect.Empty;
        }
    }

    private static IEnumerable<Paragraph> Paragraphs(IEnumerable<Block> blocks)
    {
        foreach (var block in blocks)
        {
            switch (block)
            {
                case Paragraph paragraph:
                    yield return paragraph;

                    break;

                case List list:
                    foreach (var item in list.ListItems)
                    {
                        foreach (var inner in Paragraphs(item.Blocks))
                        {
                            yield return inner;
                        }
                    }

                    break;

                case Table table:
                    foreach (var group in table.RowGroups)
                    {
                        foreach (var row in group.Rows)
                        {
                            foreach (var cell in row.Cells)
                            {
                                foreach (var inner in Paragraphs(cell.Blocks))
                                {
                                    yield return inner;
                                }
                            }
                        }
                    }

                    break;

                case Section section:
                    foreach (var inner in Paragraphs(section.Blocks))
                    {
                        yield return inner;
                    }

                    break;
            }
        }
    }

    private void DrawPilcrow(DrawingContext dc, Rect at, double fontSize)
    {
        var mark = Glyph(_pilcrows, "¶", fontSize);

        dc.DrawText(mark, new Point(at.Left, at.Top));
        LastMarkCount++;
    }

    private void DrawReturn(DrawingContext dc, Rect at, double fontSize)
    {
        var mark = Glyph(_returns, "↵", fontSize);

        dc.DrawText(mark, new Point(at.Left, at.Top));
        LastMarkCount++;
    }

    /// <summary>
    /// A dot in the middle of a space. A hard space gets a slightly larger
    /// hollow dot, so the two can be told apart - which is most of the reason
    /// to turn the marks on when a line will not break where it should.
    /// </summary>
    private void DrawSpace(DrawingContext dc, Rect at, bool hard)
    {
        var centre = new Point(
            at.Left + Math.Max(at.Width, 3) / 2,
            at.Top + (at.Height / 2));

        if (hard)
        {
            dc.DrawEllipse(null, new Pen(_ink, 1), centre, 2.4, 2.4);
        }
        else
        {
            dc.DrawEllipse(_ink, null, centre, 1.1, 1.1);
        }

        LastMarkCount++;
    }

    private void DrawTab(DrawingContext dc, Rect at, Rect next)
    {
        var y = at.Top + (at.Height / 2);

        // The character after the tab, when it is on the same line, is where
        // the tab stop landed. Without it, fall back to a fixed stub.
        var end = !next.IsEmpty && Math.Abs(next.Top - at.Top) < 1 && next.Left > at.Left
            ? next.Left
            : at.Left + 12;

        var right = end - 2;
        var left = Math.Min(at.Left + 2, right - 4);
        var pen = new Pen(_ink, 1);

        dc.DrawLine(pen, new Point(left, y), new Point(right, y));
        dc.DrawLine(pen, new Point(right - 3, y - 3), new Point(right, y));
        dc.DrawLine(pen, new Point(right - 3, y + 3), new Point(right, y));

        LastMarkCount++;
    }

    /// <summary>
    /// One <see cref="FormattedText"/> per size, kept: building one is not
    /// free and a document has a handful of sizes in it, not hundreds.
    /// </summary>
    private FormattedText Glyph(Dictionary<double, FormattedText> cache, string glyph, double fontSize)
    {
        var size = fontSize is > 0 and < 400 ? fontSize : 11;

        if (cache.TryGetValue(size, out var kept))
        {
            return kept;
        }

        var made = new FormattedText(
            glyph,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI"),
            size,
            _ink,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);

        cache[size] = made;

        return made;
    }
}
