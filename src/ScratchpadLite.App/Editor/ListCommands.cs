using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using ScratchpadLite.Core.Model;
using WpfList = System.Windows.Documents.List;
using WpfParagraph = System.Windows.Documents.Paragraph;

namespace ScratchpadLite.App.Editor;

/// <summary>
/// Bulleted and numbered lists (Section 6.9): toggling, the marker style
/// per level, changing level with Tab and Shift+Tab, and restarting
/// numbering.
/// </summary>
/// <remarks>
/// WPF does the toggling and the indenting through
/// <see cref="EditingCommands.ToggleBullets"/>,
/// <see cref="EditingCommands.ToggleNumbering"/> and the two indentation
/// commands, so this class is the part WPF leaves out: giving a list the right
/// marker for its depth, and the numbering controls.
/// <para>
/// Why the markers need fixing up at all: WPF applies one marker to the whole
/// list and keeps it when a list is nested, so three levels of bullets all
/// come out as discs and are impossible to tell apart. Word cycles the shape
/// by depth, and <see cref="Bridge.FlowBuilder"/> already does the same when
/// building a document from a file - so the editor has to agree with it, or a
/// list typed by hand and the same list reopened from a file would look
/// different.
/// </para>
/// </remarks>
internal static class ListCommands
{
    /// <summary>The deepest level the IR can carry (Section 4.2).</summary>
    internal const int MaxLevel = 8;

    internal static void ToggleBullets(RichTextBox editor)
    {
        ArgumentNullException.ThrowIfNull(editor);

        editor.BeginChange();

        try
        {
            EditingCommands.ToggleBullets.Execute(null, editor);
            FixMarkers(editor);
        }
        finally
        {
            editor.EndChange();
        }
    }

    internal static void ToggleNumbering(RichTextBox editor)
    {
        ArgumentNullException.ThrowIfNull(editor);

        editor.BeginChange();

        try
        {
            EditingCommands.ToggleNumbering.Execute(null, editor);
            FixMarkers(editor);
        }
        finally
        {
            editor.EndChange();
        }
    }

    /// <summary>Whether the caret or selection sits in a list.</summary>
    internal static bool CaretIsInList(RichTextBox editor) =>
        editor is not null && ListAt(editor.Selection.Start) is not null;

    /// <summary>Whether the caret sits in a numbered list.</summary>
    internal static bool CaretIsInNumberedList(RichTextBox editor) =>
        editor is not null
        && ListAt(editor.Selection.Start) is { } list
        && !IsBullet(list.MarkerStyle);

    /// <summary>
    /// One level deeper, which is what Tab does inside a list (Section 6.9).
    /// </summary>
    /// <remarks>
    /// Written against the List and ListItem objects rather than through
    /// <see cref="EditingCommands.IncreaseIndentation"/>, which the blueprint
    /// suggested. That command reports it can execute and then does nothing to
    /// a list: WPF's own list nesting lives in its Tab key handling, not in a
    /// command, so there is nothing to call. What it would do instead is widen
    /// the paragraph's margin, which looks like an indent until the file is
    /// saved and the list turns out to be flat.
    /// <para>
    /// Nesting means moving the item into a list inside the item above it,
    /// which is the shape <see cref="Bridge.FlowBuilder"/> builds from a file
    /// and <see cref="Bridge.FlowWalker"/> reads back - so a list typed by hand
    /// and the same list reopened are the same document.
    /// </para>
    /// </remarks>
    /// <returns>False when there was no list to indent, so the key can do its usual job.</returns>
    internal static bool Indent(RichTextBox editor)
    {
        ArgumentNullException.ThrowIfNull(editor);

        var items = SelectedItems(editor);

        if (items.Count == 0)
        {
            return false;
        }

        editor.BeginChange();

        try
        {
            var caret = Caret.Of(editor);

            foreach (var item in items)
            {
                IndentOne(item);
            }

            FixMarkers(editor);
            caret.Restore(editor);
        }
        finally
        {
            editor.EndChange();
        }

        return true;
    }

    /// <summary>One level shallower, which is what Shift+Tab does.</summary>
    /// <returns>False when there was no list to outdent.</returns>
    internal static bool Outdent(RichTextBox editor)
    {
        ArgumentNullException.ThrowIfNull(editor);

        var items = SelectedItems(editor);

        if (items.Count == 0)
        {
            return false;
        }

        editor.BeginChange();

        try
        {
            var caret = Caret.Of(editor);

            // Innermost first, so moving one item out does not change where
            // the next one is.
            foreach (var item in items)
            {
                if (!OutdentOne(item))
                {
                    // Already at the top level: leaving the list is what
                    // Shift+Tab means there. WPF's own toggle handles the
                    // splitting when the item is in the middle of a list.
                    LeaveList(editor);

                    break;
                }
            }

            FixMarkers(editor);
            caret.Restore(editor);
        }
        finally
        {
            editor.EndChange();
        }

        return true;
    }

    /// <summary>
    /// Where the caret was, so it can be put back after the list is rebuilt.
    /// </summary>
    /// <remarks>
    /// Taking a ListItem out of one collection and adding it to another
    /// invalidates every TextPointer inside it, and WPF then drops the caret
    /// at the start of the document. On screen that means pressing Tab in a
    /// list indents the item and throws the caret back to the top of the file.
    /// <para>
    /// The paragraph object itself survives the move - it is reparented, not
    /// recreated - so remembering it and the offset within it is enough to put
    /// the caret back where the user left it.
    /// </para>
    /// </remarks>
    private readonly record struct Caret(WpfParagraph? Paragraph, int Offset)
    {
        internal static Caret Of(RichTextBox editor)
        {
            var paragraph = editor.Selection.Start.Paragraph;

            return new Caret(
                paragraph,
                paragraph is null ? 0 : paragraph.ContentStart.GetOffsetToPosition(editor.Selection.Start));
        }

        internal void Restore(RichTextBox editor)
        {
            if (Paragraph is null)
            {
                return;
            }

            editor.CaretPosition =
                Paragraph.ContentStart.GetPositionAtOffset(Offset) ?? Paragraph.ContentStart;
        }
    }

    private static void IndentOne(ListItem item)
    {
        if (item.List is not { } list)
        {
            return;
        }

        var siblings = list.ListItems.ToList();
        var index = siblings.IndexOf(item);

        if (index <= 0)
        {
            // The first item of a list has nothing to nest under, which is the
            // same rule Word follows.
            return;
        }

        if (LevelOf(list) >= MaxLevel)
        {
            // As deep as a saved document can describe (Section 4.2). Doing
            // nothing beats nesting a level the next save would flatten.
            return;
        }

        var host = siblings[index - 1];
        var nested = host.Blocks.LastBlock as WpfList;

        if (nested is null)
        {
            nested = new WpfList
            {
                MarkerStyle = list.MarkerStyle,
                StartIndex = 1,
                Margin = new Thickness(0),
                Padding = new Thickness(0),
            };

            host.Blocks.Add(nested);
        }

        list.ListItems.Remove(item);
        nested.ListItems.Add(item);
    }

    /// <summary>
    /// Moves an item out one level.
    /// </summary>
    /// <returns>False when it is already at the top level.</returns>
    private static bool OutdentOne(ListItem item)
    {
        if (item.List is not { } list || list.Parent is not ListItem host || host.List is not { } outer)
        {
            return false;
        }

        list.ListItems.Remove(item);
        outer.ListItems.InsertAfter(host, item);

        if (list.ListItems.Count == 0)
        {
            host.Blocks.Remove(list);
        }

        return true;
    }

    /// <summary>
    /// Takes the selection out of its list altogether, which is what
    /// Shift+Tab does at the top level.
    /// </summary>
    /// <remarks>
    /// Through WPF's own toggle rather than by hand, because an item in the
    /// middle of a list has to split it in two and WPF already does that
    /// correctly.
    /// </remarks>
    private static void LeaveList(RichTextBox editor)
    {
        if (ListAt(editor.Selection.Start) is not { } list)
        {
            return;
        }

        var command = IsBullet(list.MarkerStyle)
            ? EditingCommands.ToggleBullets
            : EditingCommands.ToggleNumbering;

        command.Execute(null, editor);
    }

    /// <summary>
    /// The list items the selection touches, innermost ones first.
    /// </summary>
    /// <remarks>
    /// Ordered deepest-first so that indenting or outdenting several at once
    /// cannot move an item the loop has not reached yet.
    /// </remarks>
    /// <summary>Exposed for the tests that check which items a key affects.</summary>
    internal static List<ListItem> SelectedItemsForTests(RichTextBox editor) => SelectedItems(editor);

    private static List<ListItem> SelectedItems(RichTextBox editor)
    {
        var start = editor.Selection.Start;
        var end = editor.Selection.End;

        if (ItemAt(start) is not { } anchor)
        {
            return [];
        }

        if (Outermost(start) is not { } outermost)
        {
            return [anchor];
        }

        var found = WithDescendants(outermost)
            .SelectMany(list => list.ListItems)
            .Where(item => item.ContentEnd.CompareTo(start) >= 0
                           && item.ContentStart.CompareTo(end) <= 0)
            .ToList();

        if (found.Count == 0)
        {
            return [anchor];
        }

        // An item that contains another selected item is a parent, and moving
        // a parent moves its children with it - so only the deepest are
        // returned.
        return found
            .Where(item => !found.Any(other => !ReferenceEquals(other, item) && Contains(item, other)))
            .ToList();
    }

    private static bool Contains(ListItem outer, ListItem inner)
    {
        for (DependencyObject? node = Parent(inner); node is not null; node = Parent(node))
        {
            if (ReferenceEquals(node, outer))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Ends the list when Enter is pressed on an item with nothing in it
    /// (Section 6.9).
    /// </summary>
    /// <remarks>
    /// The behaviour everyone expects from every editor: an empty item plus
    /// Enter means "I have finished the list". A nested empty item steps out
    /// one level instead, so Enter walks back up a nested list rather than
    /// jumping to the body in one go.
    /// </remarks>
    /// <returns>True when the key was dealt with here.</returns>
    internal static bool EndListOnEmptyItem(RichTextBox editor)
    {
        ArgumentNullException.ThrowIfNull(editor);

        if (!editor.Selection.IsEmpty
            || editor.Selection.Start.Paragraph is not { } paragraph
            || ListAt(editor.Selection.Start) is not { } list)
        {
            return false;
        }

        // "Empty" means no text and nothing else either: an item holding only
        // a picture is not an empty item.
        //
        // Read off the runs rather than from a TextRange or an inline count,
        // both of which get this wrong. WPF leaves an empty Run behind when it
        // splits a paragraph, so a fresh list item has one inline; and
        // TextRange.Text over a paragraph includes its break, so an empty
        // paragraph measures two characters. Either mistake stops this ever
        // firing, which is the whole feature.
        var hasText = paragraph.Inlines.OfType<Run>().Any(run => run.Text.Length > 0);
        var hasAnythingElse = paragraph.Inlines.Any(inline => inline is not Run);

        if (hasText || hasAnythingElse)
        {
            return false;
        }

        editor.BeginChange();

        try
        {
            if (LevelOf(list) > 0)
            {
                EditingCommands.DecreaseIndentation.Execute(null, editor);
                FixMarkers(editor);
            }
            else
            {
                EditingCommands.ToggleBullets.Execute(null, editor);

                // ToggleBullets on a numbered list turns it into a bulleted
                // one rather than into body text, so it may take two.
                if (ListAt(editor.Selection.Start) is not null)
                {
                    EditingCommands.ToggleBullets.Execute(null, editor);
                }
            }
        }
        finally
        {
            editor.EndChange();
        }

        return true;
    }

    /// <summary>
    /// Makes this item the first of a new list, so the numbering starts again
    /// (Section 6.9).
    /// </summary>
    /// <remarks>
    /// WPF numbers a <see cref="WpfList"/> from its
    /// <see cref="WpfList.StartIndex"/>, with no way to restart part way
    /// through - so restarting means splitting the list in two at this item and
    /// giving the second half StartIndex 1. The IR says the same thing the same
    /// way (<c>ListInfo.Start</c>), and so do .docx and .odt, so this survives
    /// a save.
    /// </remarks>
    internal static void RestartNumbering(RichTextBox editor) => SetStart(editor, start: 1);

    /// <summary>
    /// Joins this item back onto the list above it, so the numbering carries
    /// on.
    /// </summary>
    internal static void ContinueNumbering(RichTextBox editor)
    {
        ArgumentNullException.ThrowIfNull(editor);

        if (ItemAt(editor.Selection.Start) is not { } item
            || item.List is not { } list
            || list.SiblingBlocks is null)
        {
            return;
        }

        // The list immediately above, if there is one and it has the same
        // marker.
        if (list.PreviousBlock is not WpfList above || above.MarkerStyle != list.MarkerStyle)
        {
            return;
        }

        editor.BeginChange();

        try
        {
            var moving = list.ListItems.ToList();

            foreach (var each in moving)
            {
                list.ListItems.Remove(each);
                above.ListItems.Add(each);
            }

            list.SiblingBlocks.Remove(list);
        }
        finally
        {
            editor.EndChange();
        }
    }

    // ------------------------------------------------------------ markers --

    /// <summary>
    /// Gives every list the caret touches the marker its depth calls for.
    /// </summary>
    /// <remarks>
    /// Runs after any WPF list command, because all of them can create or
    /// re-nest a list. The whole document is not walked: only the lists around
    /// the selection, since that is all that can have changed and walking a
    /// 300-page document on every Tab would be felt.
    /// </remarks>
    private static void FixMarkers(RichTextBox editor)
    {
        foreach (var list in ListsAround(editor))
        {
            var kind = IsBullet(list.MarkerStyle) ? ListKind.Bullet : ListKind.Number;
            var level = LevelOf(list);

            list.MarkerStyle = Bridge.FlowBuilder.MarkerFor(
                new ListInfo(kind, level, FormatOf(list.MarkerStyle)));
        }
    }

    /// <summary>
    /// Every list in and around the selection: the ones the caret is inside,
    /// and the ones nested within those.
    /// </summary>
    private static IEnumerable<WpfList> ListsAround(RichTextBox editor)
    {
        var outermost = Outermost(editor.Selection.Start) ?? Outermost(editor.Selection.End);

        if (outermost is null)
        {
            yield break;
        }

        foreach (var list in WithDescendants(outermost))
        {
            yield return list;
        }
    }

    private static IEnumerable<WpfList> WithDescendants(WpfList list)
    {
        yield return list;

        foreach (var item in list.ListItems)
        {
            foreach (var block in item.Blocks)
            {
                if (block is WpfList nested)
                {
                    foreach (var deeper in WithDescendants(nested))
                    {
                        yield return deeper;
                    }
                }
            }
        }
    }

    private static WpfList? Outermost(TextPointer pointer)
    {
        WpfList? found = null;

        for (DependencyObject? node = pointer.Parent; node is not null; node = Parent(node))
        {
            if (node is WpfList list)
            {
                found = list;
            }
        }

        return found;
    }

    /// <summary>The innermost list the pointer is inside, or null.</summary>
    internal static WpfList? ListAt(TextPointer? pointer)
    {
        for (DependencyObject? node = pointer?.Parent; node is not null; node = Parent(node))
        {
            if (node is WpfList list)
            {
                return list;
            }
        }

        return null;
    }

    private static ListItem? ItemAt(TextPointer? pointer)
    {
        for (DependencyObject? node = pointer?.Parent; node is not null; node = Parent(node))
        {
            if (node is ListItem item)
            {
                return item;
            }
        }

        return null;
    }

    /// <summary>How deep this list is nested, counting from zero.</summary>
    internal static int LevelOf(WpfList list)
    {
        var level = 0;

        for (DependencyObject? node = Parent(list); node is not null; node = Parent(node))
        {
            if (node is WpfList)
            {
                level++;
            }
        }

        return Math.Min(level, MaxLevel);
    }

    private static DependencyObject? Parent(DependencyObject node) => node switch
    {
        FrameworkContentElement element => element.Parent,
        FrameworkElement element => element.Parent,
        _ => null,
    };

    private static bool IsBullet(TextMarkerStyle marker) => marker
        is TextMarkerStyle.Disc or TextMarkerStyle.Circle or TextMarkerStyle.Square
        or TextMarkerStyle.Box or TextMarkerStyle.None;

    /// <summary>The IR's name for a marker, so a fix-up keeps the format.</summary>
    private static string FormatOf(TextMarkerStyle marker) => marker switch
    {
        TextMarkerStyle.LowerLatin => "lowerLetter",
        TextMarkerStyle.UpperLatin => "upperLetter",
        TextMarkerStyle.LowerRoman => "lowerRoman",
        TextMarkerStyle.UpperRoman => "upperRoman",
        _ => "decimal",
    };

    private static void SetStart(RichTextBox editor, int start)
    {
        ArgumentNullException.ThrowIfNull(editor);

        if (ItemAt(editor.Selection.Start) is not { } item
            || item.List is not { } list
            || list.SiblingBlocks is null)
        {
            return;
        }

        var index = list.ListItems.ToList().IndexOf(item);

        if (index <= 0)
        {
            // Already the first item, so there is nothing to split off.
            list.StartIndex = Math.Max(1, start);

            return;
        }

        editor.BeginChange();

        try
        {
            var second = new WpfList
            {
                MarkerStyle = list.MarkerStyle,
                StartIndex = Math.Max(1, start),
                Margin = list.Margin,
                Padding = list.Padding,
            };

            foreach (var moving in list.ListItems.ToList().Skip(index))
            {
                list.ListItems.Remove(moving);
                second.ListItems.Add(moving);
            }

            list.SiblingBlocks.InsertAfter(list, second);
        }
        finally
        {
            editor.EndChange();
        }
    }

    /// <summary>
    /// A fresh list item holding one empty paragraph, for the places that need
    /// a list to have at least one.
    /// </summary>
    internal static ListItem EmptyItem() => new(new WpfParagraph());
}
