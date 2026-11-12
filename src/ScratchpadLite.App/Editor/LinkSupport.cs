using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using ScratchpadLite.App.Bridge;
using ScratchpadLite.Core.Platform;

namespace ScratchpadLite.App.Editor;

/// <summary>
/// Hyperlinks (Section 6.9): insert and edit through Ctrl+K, Ctrl+click to
/// follow, and the tooltip that shows the target. A link is only ever opened
/// on a deliberate Ctrl+click, never on a plain click while editing.
/// </summary>
/// <remarks>
/// The rule about what may be opened is the important part of this file. A
/// document is untrusted (Section 7.5), and a hyperlink is a line of text in
/// it that asks this program to run something. <c>http</c>, <c>https</c> and
/// <c>mailto</c> open straight away because they can only reach a browser or a
/// mail client. Everything else - <c>file:</c>, a UNC path, a custom scheme
/// registered by some other application - is shown to the user with its full
/// address first, and opens only if they say so.
/// </remarks>
internal static class LinkSupport
{
    /// <summary>The schemes that open without asking.</summary>
    private static readonly string[] SafeSchemes = ["http", "https", "mailto"];

    /// <summary>The link at the caret, or null.</summary>
    internal static Hyperlink? LinkAt(RichTextBox editor)
    {
        if (editor is null)
        {
            return null;
        }

        return Containing(editor.Selection.Start) ?? Containing(editor.Selection.End);
    }

    /// <summary>
    /// The link a position is in, or immediately beside.
    /// </summary>
    /// <remarks>
    /// "Beside" matters. After inserting a link the caret sits just after it,
    /// and clicking the end of one leaves it there too - at which point the
    /// caret's parent is the paragraph, not the Hyperlink. Without this, Edit
    /// Link and Remove Link were greyed out immediately after making a link,
    /// which is exactly when they are wanted.
    /// </remarks>
    internal static Hyperlink? Containing(TextPointer? pointer)
    {
        for (DependencyObject? node = pointer?.Parent; node is not null; node = Parent(node))
        {
            if (node is Hyperlink link)
            {
                return link;
            }
        }

        if (pointer?.GetAdjacentElement(LogicalDirection.Backward) is Hyperlink before)
        {
            return before;
        }

        return pointer?.GetAdjacentElement(LogicalDirection.Forward) as Hyperlink;
    }

    /// <summary>
    /// What the Ctrl+K dialog should start with: the existing link's target and
    /// text, or the selected text with an empty target.
    /// </summary>
    internal static (string Url, string Text) Current(RichTextBox editor)
    {
        ArgumentNullException.ThrowIfNull(editor);

        if (LinkAt(editor) is { } link)
        {
            return (
                SpProps.GetLink(link) ?? link.NavigateUri?.ToString() ?? "",
                new TextRange(link.ContentStart, link.ContentEnd).Text);
        }

        return ("", editor.Selection.Text);
    }

    /// <summary>
    /// Inserts a link, or changes the one at the caret (Section 6.9).
    /// </summary>
    /// <remarks>
    /// Three cases, and they behave differently on purpose. On an existing
    /// link, the target and the text are both updated in place. On a selection,
    /// the selected text becomes the link - keeping its own formatting, because
    /// linking a bold word should not unbolden it. With no selection, the
    /// display text is inserted as new text, falling back to the address when
    /// there is nothing to show.
    /// </remarks>
    internal static void InsertOrEdit(RichTextBox editor, string url, string? displayText)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentException.ThrowIfNullOrWhiteSpace(url);

        editor.BeginChange();

        try
        {
            if (LinkAt(editor) is { } existing)
            {
                Retarget(existing, url);
                Retext(existing, displayText);

                return;
            }

            if (!editor.Selection.IsEmpty)
            {
                // A Hyperlink built from the selection's own pointers keeps the
                // formatting of what was selected.
                var (from, to) = Linkable(editor);
                var link = new Hyperlink(from, to);

                Retarget(link, url);

                // Only when the dialog was given something else to show. With
                // nothing, the selected text is what to show - overwriting it
                // with the address was how "select a word, press Ctrl+K" used
                // to replace the word with a URL.
                Retext(link, displayText);

                PutCaretAfter(editor, link);

                return;
            }

            // Nothing selected and nothing to show, so the address is the only
            // text there can be.
            var fresh = new Hyperlink(
                new Run(string.IsNullOrWhiteSpace(displayText) ? url : displayText));

            Retarget(fresh, url);

            if (editor.CaretPosition.Paragraph is { } paragraph)
            {
                paragraph.Inlines.Add(fresh);
            }
            else
            {
                editor.Document.Blocks.Add(new System.Windows.Documents.Paragraph(fresh));
            }

            PutCaretAfter(editor, fresh);
        }
        finally
        {
            editor.EndChange();
        }
    }

    /// <summary>
    /// Changes what a link shows, leaving it alone when there is nothing new
    /// to show.
    /// </summary>
    /// <remarks>
    /// By replacing the inlines rather than assigning to a
    /// <c>TextRange.Text</c> over the link's content. That assignment deletes
    /// the content and inserts plain text in its place, and the Hyperlink goes
    /// with it: the result was a run of blue-less text where a link had been,
    /// with no error anywhere to say so.
    /// </remarks>
    private static void Retext(Hyperlink link, string? displayText)
    {
        if (string.IsNullOrWhiteSpace(displayText))
        {
            return;
        }

        if (string.Equals(
                new TextRange(link.ContentStart, link.ContentEnd).Text,
                displayText,
                StringComparison.Ordinal))
        {
            return;
        }

        link.Inlines.Clear();
        link.Inlines.Add(new Run(displayText));
    }

    /// <summary>
    /// The part of the selection a single hyperlink can cover.
    /// </summary>
    /// <remarks>
    /// A <see cref="Hyperlink"/> is an inline, so it cannot span paragraphs,
    /// and handing its constructor two pointers that do silently produces
    /// nothing at all - no exception, no link. That is what "Select All then
    /// Ctrl+K" does, which is not an unusual thing to try.
    /// <para>
    /// So the range is clamped to insertion positions inside the paragraph
    /// where the selection begins. On a one-paragraph document that is the
    /// whole selection; on a longer one it links the first paragraph's worth,
    /// which is at least what was asked for rather than nothing.
    /// </para>
    /// </remarks>
    private static (TextPointer From, TextPointer To) Linkable(RichTextBox editor)
    {
        var from = editor.Selection.Start.GetInsertionPosition(LogicalDirection.Forward)
                   ?? editor.Selection.Start;

        var to = editor.Selection.End.GetInsertionPosition(LogicalDirection.Backward)
                 ?? editor.Selection.End;

        if (from.Paragraph is { } paragraph && !ReferenceEquals(paragraph, to.Paragraph))
        {
            to = paragraph.ContentEnd;
        }

        return to.CompareTo(from) <= 0 ? (from, from) : (from, to);
    }

    /// <summary>
    /// Leaves the caret after a link, so typing carries on outside it.
    /// </summary>
    /// <remarks>
    /// Not simply <c>link.ElementEnd</c>: that is an element boundary rather
    /// than a place text can go, and assigning it to CaretPosition throws
    /// "Cannot set CaretPosition to be outside of RichTextBox" - which is a
    /// confusing way to say "not an insertion position".
    /// </remarks>
    private static void PutCaretAfter(RichTextBox editor, Hyperlink link)
    {
        try
        {
            // By offset from the document, so the pointer certainly belongs to
            // the editor's own text container. A pointer taken straight off
            // the Hyperlink can be rejected, and the exception WPF throws for
            // that says "outside of RichTextBox", which is no help at all.
            var offset = editor.Document.ContentStart.GetOffsetToPosition(link.ElementEnd);
            var after = editor.Document.ContentStart
                .GetPositionAtOffset(offset)
                ?.GetInsertionPosition(LogicalDirection.Forward);

            if (after is not null && after.IsAtInsertionPosition)
            {
                editor.CaretPosition = after;
            }
        }
        catch (ArgumentException ex)
        {
            // Where the caret ends up is a convenience; the link itself is the
            // work. Failing the insert over it would be the wrong trade.
            Log.Debug($"the caret could not be moved past a new link: {ex.Message}");
        }
    }

    /// <summary>
    /// Removes the link at the caret but keeps its text (Section 6.9).
    /// </summary>
    internal static void Remove(RichTextBox editor)
    {
        ArgumentNullException.ThrowIfNull(editor);

        if (LinkAt(editor) is not { } link)
        {
            return;
        }

        editor.BeginChange();

        try
        {
            var inlines = link.SiblingInlines;
            var moving = link.Inlines.ToList();

            foreach (var inline in moving)
            {
                link.Inlines.Remove(inline);

                // The link's blue and underline came from the Hyperlink
                // itself, so the text comes out looking like body text, which
                // is what removing a link should do.
                inlines?.InsertBefore(link, inline);
            }

            inlines?.Remove(link);
        }
        finally
        {
            editor.EndChange();
        }
    }

    /// <summary>
    /// Opens a link, asking first unless its scheme is one of the three safe
    /// ones (Section 6.9).
    /// </summary>
    /// <param name="owner">The window to centre the question on.</param>
    /// <returns>False when the link was not opened.</returns>
    internal static bool Open(Hyperlink link, Window? owner)
    {
        ArgumentNullException.ThrowIfNull(link);

        var target = SpProps.GetLink(link) ?? link.NavigateUri?.ToString();

        if (string.IsNullOrWhiteSpace(target))
        {
            return false;
        }

        return Open(target, owner);
    }

    /// <summary>
    /// What this app will do with an address, decided without any UI so it can
    /// be tested on its own.
    /// </summary>
    internal enum Verdict
    {
        /// <summary>Not an address Windows can open.</summary>
        Unusable,

        /// <summary>http, https or mailto: opens straight away.</summary>
        OpenDirectly,

        /// <summary>Anything else: show the address and ask first.</summary>
        AskFirst,
    }

    /// <summary>
    /// Decides what to do with a link target (Section 6.9).
    /// </summary>
    /// <remarks>
    /// Separated from <see cref="Open(string, Window?)"/> because this is the
    /// security rule and a rule needs a test. A message box cannot be tested:
    /// it blocks until somebody clicks it.
    /// </remarks>
    internal static Verdict Judge(string? target)
    {
        if (string.IsNullOrWhiteSpace(target)
            || !Uri.TryCreate(target, UriKind.Absolute, out var uri))
        {
            return Verdict.Unusable;
        }

        return SafeSchemes.Contains(uri.Scheme, StringComparer.OrdinalIgnoreCase)
            ? Verdict.OpenDirectly
            : Verdict.AskFirst;
    }

    internal static bool Open(string target, Window? owner)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(target);

        if (!Uri.TryCreate(target, UriKind.Absolute, out var uri))
        {
            Ask(
                owner,
                $"Scratchpad Lite does not understand this link:\n\n{Shorten(target)}\n\n"
                + "It is not an address Windows can open.",
                MessageBoxButton.OK);

            return false;
        }

        if (Judge(target) == Verdict.AskFirst)
        {
            // The full address is shown, not the link's text, because the two
            // can say completely different things and that is exactly how a
            // document talks someone into opening something.
            var answer = Ask(
                owner,
                "This link does not go to a web page or an email address. "
                + "Opening it will hand it to Windows, which may start another program.\n\n"
                + $"Address:\n{Shorten(uri.ToString())}\n\nOpen it?",
                MessageBoxButton.YesNo);

            if (answer != MessageBoxResult.Yes)
            {
                return false;
            }
        }

        try
        {
            Process.Start(new ProcessStartInfo(uri.ToString()) { UseShellExecute = true });
            Log.Info($"opened a link with scheme {uri.Scheme}");

            return true;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception
                                       or System.IO.FileNotFoundException
                                       or InvalidOperationException)
        {
            Log.Warn($"a link could not be opened: {ex.Message}");

            Ask(
                owner,
                $"Windows could not open this link:\n\n{Shorten(uri.ToString())}",
                MessageBoxButton.OK);

            return false;
        }
    }

    /// <summary>
    /// Sets a link's target on both the Uri and the kept-as-written copy.
    /// </summary>
    /// <remarks>
    /// <see cref="Hyperlink.NavigateUri"/> is what WPF shows and follows;
    /// <see cref="SpProps.LinkProperty"/> is what a save writes out. Both are
    /// set here so they cannot drift apart - and the Uri is only set when the
    /// address parses, since a file can carry a target that
    /// <see cref="Uri"/> rejects and dropping it would be worse than having no
    /// NavigateUri.
    /// </remarks>
    private static void Retarget(Hyperlink link, string url)
    {
        SpProps.SetLink(link, url);

        link.NavigateUri = Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri : null;
        link.ToolTip = url + "\nCtrl+click to open";
    }

    private static MessageBoxResult Ask(Window? owner, string message, MessageBoxButton buttons) =>
        owner is null
            ? MessageBox.Show(message, "Link", buttons, MessageBoxImage.Warning)
            : MessageBox.Show(owner, message, "Link", buttons, MessageBoxImage.Warning);

    /// <summary>
    /// Keeps a very long address readable in a dialog.
    /// </summary>
    /// <remarks>
    /// A link can be thousands of characters long, and a message box given one
    /// grows until its buttons are off the screen - at which point the user
    /// cannot say no.
    /// </remarks>
    private static string Shorten(string target) =>
        target.Length <= 200 ? target : target[..200] + "...";

    private static DependencyObject? Parent(DependencyObject node) => node switch
    {
        FrameworkContentElement element => element.Parent,
        FrameworkElement element => element.Parent,
        _ => null,
    };
}
