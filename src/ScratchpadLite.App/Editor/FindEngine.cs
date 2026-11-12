using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace ScratchpadLite.App.Editor;

/// <summary>
/// Find and replace over the document (Section 6.10), walking TextPointer
/// positions so matches that span runs are still found. Supports match
/// case, whole word, wrap-around, replace and replace all, and reports the
/// count. Replace all is one undo unit.
/// </summary>
/// <remarks>
/// WPF has no find API at all, so the whole of this is the project's own.
/// <para>
/// The shape of it is the one thing worth understanding. A RichTextBox holds a
/// tree, not a string: the word "bold" with its first two letters emboldened is
/// two Runs, and a search that looked at one Run at a time would never find it.
/// So the document is flattened first into <see cref="Unit"/>s - one per
/// paragraph or cell - each carrying the plain text plus a list of
/// <see cref="Segment"/>s recording which TextPointer each stretch of that text
/// came from. The search runs on the string, and every hit is mapped back
/// through the segments to a real <see cref="TextRange"/>.
/// </para>
/// <para>
/// A match never crosses a paragraph, a cell, a line break or a picture. That
/// is deliberate rather than a shortcut: a TextRange that spans two paragraphs
/// cannot be given replacement text without destroying the paragraph break
/// between them, so a match that could not be replaced would be a match worth
/// nothing.
/// </para>
/// </remarks>
internal static class FindEngine
{
    /// <summary>The 2-second cap Section 6.10 puts on a regular expression.</summary>
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(2);

    /// <summary>
    /// The formatting a replacement inherits: that of the first character of
    /// the match (Section 6.10).
    /// </summary>
    /// <remarks>
    /// Exactly the properties WPF calls text formatting.
    /// <c>TextRange.GetPropertyValue</c> and <c>ApplyPropertyValue</c> check
    /// what they are given against a fixed list and throw
    /// <see cref="ArgumentException"/> - "'Underline' property is not valid for
    /// text formatting" - for anything else, so an attached property of this
    /// app's own cannot go through them at all. Those are carried by
    /// <see cref="OwnProperties"/> instead.
    /// </remarks>
    private static readonly DependencyProperty[] Carried =
    [
        TextElement.FontFamilyProperty,
        TextElement.FontSizeProperty,
        TextElement.FontWeightProperty,
        TextElement.FontStyleProperty,
        TextElement.FontStretchProperty,
        TextElement.ForegroundProperty,
        TextElement.BackgroundProperty,
        Inline.TextDecorationsProperty,
        Inline.BaselineAlignmentProperty,
        Typography.CapitalsProperty,
    ];

    /// <summary>
    /// This app's own attached properties, which hold what WPF's model cannot:
    /// which kind of underline, and whether the run is super or subscript
    /// (Section 6.8). They are read and written on the elements directly,
    /// because the TextRange methods refuse them.
    /// </summary>
    private static readonly DependencyProperty[] OwnProperties =
    [
        Bridge.SpProps.UnderlineProperty,
        Bridge.SpProps.VAlignProperty,
        Bridge.SpProps.BaseSizePtProperty,
    ];

    /// <summary>What to look for.</summary>
    internal sealed record Options(
        string Needle,
        bool MatchCase = false,
        bool WholeWord = false,
        bool UseRegex = false);

    /// <summary>
    /// A regular expression the user typed that will not compile, or that ran
    /// for longer than its timeout. Section 6.10: the bar shows the message, the
    /// app does not crash.
    /// </summary>
    internal sealed class BadPatternException(string message) : Exception(message);

    /// <summary>
    /// Every match in the document, in document order.
    /// </summary>
    /// <exception cref="BadPatternException">The pattern will not compile.</exception>
    internal static IReadOnlyList<TextRange> All(FlowDocument document, Options options)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(options);

        if (options.Needle.Length == 0)
        {
            return [];
        }

        var regex = options.UseRegex ? Compile(options) : null;
        var found = new List<TextRange>();

        foreach (var unit in Units(document))
        {
            foreach (var (start, length) in MatchesIn(unit.Text, options, regex))
            {
                var from = PointerAt(unit, start);
                var to = PointerAt(unit, start + length);

                if (from is not null && to is not null)
                {
                    found.Add(new TextRange(from, to));
                }
            }
        }

        return found;
    }

    /// <summary>
    /// The index of the first match at or after <paramref name="at"/>, or 0 when
    /// there is none - which is the wrap-around (Section 6.10).
    /// </summary>
    internal static int IndexFrom(IReadOnlyList<TextRange> matches, TextPointer? at, bool backwards)
    {
        ArgumentNullException.ThrowIfNull(matches);

        if (matches.Count == 0)
        {
            return -1;
        }

        if (at is null)
        {
            return backwards ? matches.Count - 1 : 0;
        }

        if (backwards)
        {
            for (var i = matches.Count - 1; i >= 0; i--)
            {
                // "Ends at or before here", not "ends before here". With
                // matches that touch - "a" in "aaaa" - the previous one ends
                // exactly where the selected one starts, and a strict
                // comparison would skip it and wrap round to the end of the
                // document instead.
                if (matches[i].End.CompareTo(at) <= 0)
                {
                    return i;
                }
            }

            return matches.Count - 1;      // wrapped round to the end
        }

        for (var i = 0; i < matches.Count; i++)
        {
            if (matches[i].Start.CompareTo(at) >= 0)
            {
                return i;
            }
        }

        return 0;                           // wrapped round to the start
    }

    /// <summary>
    /// Which match the caret is sitting in or just after, so the bar can say
    /// "3 of 17" without moving anything. Null when the caret is not in one.
    /// </summary>
    internal static int? IndexOfCurrent(IReadOnlyList<TextRange> matches, TextPointer? at)
    {
        ArgumentNullException.ThrowIfNull(matches);

        if (at is null)
        {
            return null;
        }

        for (var i = 0; i < matches.Count; i++)
        {
            if (matches[i].Start.CompareTo(at) <= 0 && matches[i].End.CompareTo(at) >= 0)
            {
                return i;
            }
        }

        return null;
    }

    /// <summary>
    /// Selects a match and scrolls it into view, which is how Section 6.10 says
    /// to show the current one.
    /// </summary>
    internal static void Show(RichTextBox editor, TextRange match)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(match);

        editor.Selection.Select(match.Start, match.End);

        // A selection the user cannot see is not a found match. The rectangle
        // comes from the start pointer because that is the end that has to be
        // on screen for the match to read.
        if (match.Start.GetCharacterRect(LogicalDirection.Forward) is { IsEmpty: false } rect)
        {
            editor.BringIntoView(rect);
        }
    }

    /// <summary>
    /// Replaces one match, keeping the formatting of its first character.
    /// </summary>
    /// <remarks>
    /// One <c>BeginChange</c>, so one Undo takes it back.
    /// <para>
    /// A match that lies inside a hyperlink loses the link: assigning to a
    /// TextRange over a Hyperlink's content deletes the Hyperlink with it, which
    /// is WPF's behaviour and was found the hard way in Phase 6. Replacing the
    /// text of a link is rare enough, and visible enough when it happens, to be
    /// worth recording in the user guide rather than working around with a
    /// rebuild that would get the link's other properties wrong.
    /// </para>
    /// </remarks>
    internal static void Replace(RichTextBox editor, TextRange match, string replacement)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(match);

        editor.BeginChange();

        try
        {
            ReplaceOne(match, replacement);
        }
        finally
        {
            editor.EndChange();
        }
    }

    /// <summary>
    /// Replaces every match and returns how many (Section 6.10). All of it is
    /// one <c>BeginChange</c>, so one Undo reverses the lot.
    /// </summary>
    internal static int ReplaceAll(RichTextBox editor, Options options, string replacement)
    {
        ArgumentNullException.ThrowIfNull(editor);

        var matches = All(editor.Document, options);

        if (matches.Count == 0)
        {
            return 0;
        }

        editor.BeginChange();

        try
        {
            // Backwards. A TextPointer moves with the text around it, but
            // replacing a short match with a long one still shifts everything
            // after it, and working from the end means no range is touched
            // before it is used.
            for (var i = matches.Count - 1; i >= 0; i--)
            {
                ReplaceOne(matches[i], replacement);
            }
        }
        finally
        {
            editor.EndChange();
        }

        return matches.Count;
    }

    private static void ReplaceOne(TextRange match, string replacement)
    {
        var formatting = Capture(match);
        var own = CaptureOwn(match);

        match.Text = replacement;

        if (replacement.Length == 0)
        {
            return;
        }

        foreach (var (property, value) in formatting)
        {
            match.ApplyPropertyValue(property, value);
        }

        if (own.Count == 0)
        {
            return;
        }

        foreach (var inline in InlinesIn(match))
        {
            foreach (var (property, value) in own)
            {
                inline.SetValue(property, value);
            }
        }
    }

    /// <summary>
    /// The formatting of the first character of the match, read before it is
    /// replaced.
    /// </summary>
    private static List<(DependencyProperty Property, object Value)> Capture(TextRange match)
    {
        var firstCharacter = FirstCharacter(match);
        var captured = new List<(DependencyProperty, object)>(Carried.Length);

        foreach (var property in Carried)
        {
            var value = firstCharacter.GetPropertyValue(property);

            if (value != DependencyProperty.UnsetValue && value is not null)
            {
                captured.Add((property, value));
            }
        }

        return captured;
    }

    /// <summary>
    /// The app's own attached values at the first character, read off the
    /// element holding it. They inherit, so the element the caret is in has the
    /// effective value.
    /// </summary>
    private static List<(DependencyProperty Property, object Value)> CaptureOwn(TextRange match)
    {
        var captured = new List<(DependencyProperty, object)>(OwnProperties.Length);

        if (match.Start.Parent is not TextElement element)
        {
            return captured;
        }

        foreach (var property in OwnProperties)
        {
            if (element.GetValue(property) is { } value
                && !Equals(value, property.DefaultMetadata.DefaultValue))
            {
                captured.Add((property, value));
            }
        }

        return captured;
    }

    private static TextRange FirstCharacter(TextRange match) =>
        new(match.Start, match.Start.GetPositionAtOffset(1) ?? match.End);

    /// <summary>The inline elements a range covers.</summary>
    private static IEnumerable<Inline> InlinesIn(TextRange range)
    {
        var at = range.Start;

        while (at is not null && at.CompareTo(range.End) < 0)
        {
            if (at.Parent is Inline inline)
            {
                yield return inline;
            }

            at = at.GetNextContextPosition(LogicalDirection.Forward);
        }
    }

    private static Regex Compile(Options options)
    {
        var flags = RegexOptions.None;

        if (!options.MatchCase)
        {
            flags |= RegexOptions.IgnoreCase;
        }

        try
        {
            return new Regex(options.Needle, flags, RegexTimeout);
        }
        catch (ArgumentException ex)
        {
            throw new BadPatternException(ex.Message);
        }
    }

    private static IEnumerable<(int Start, int Length)> MatchesIn(string text, Options options, Regex? regex)
    {
        if (regex is not null)
        {
            return RegexMatchesIn(text, options, regex);
        }

        return LiteralMatchesIn(text, options);
    }

    private static List<(int Start, int Length)> RegexMatchesIn(string text, Options options, Regex regex)
    {
        var found = new List<(int, int)>();

        try
        {
            foreach (var match in regex.Matches(text).Cast<Match>())
            {
                // A pattern that matches nothing at all, like "a*", matches at
                // every position. Replacing those would insert the replacement
                // between every pair of characters, which is never what anyone
                // meant by Replace All.
                if (match.Length > 0 && (!options.WholeWord || IsWholeWord(text, match.Index, match.Length)))
                {
                    found.Add((match.Index, match.Length));
                }
            }
        }
        catch (RegexMatchTimeoutException)
        {
            throw new BadPatternException(
                "That pattern took too long on this document. Try a simpler one.");
        }

        return found;
    }

    private static List<(int Start, int Length)> LiteralMatchesIn(string text, Options options)
    {
        var comparison = options.MatchCase
            ? StringComparison.CurrentCulture
            : StringComparison.CurrentCultureIgnoreCase;

        var found = new List<(int, int)>();
        var from = 0;

        while (from <= text.Length - options.Needle.Length)
        {
            var at = text.IndexOf(options.Needle, from, comparison);

            if (at < 0)
            {
                break;
            }

            if (!options.WholeWord || IsWholeWord(text, at, options.Needle.Length))
            {
                found.Add((at, options.Needle.Length));
            }

            from = at + 1;
        }

        return found;
    }

    /// <summary>
    /// Whole word means no letter, digit or underscore immediately either side.
    /// </summary>
    private static bool IsWholeWord(string text, int start, int length)
    {
        var before = start - 1;
        var after = start + length;

        return (before < 0 || !IsWordCharacter(text[before]))
               && (after >= text.Length || !IsWordCharacter(text[after]));
    }

    private static bool IsWordCharacter(char c) => char.IsLetterOrDigit(c) || c == '_';

    // ------------------------------------------------- the flattened document --

    /// <summary>
    /// One stretch of text and the TextPointer it starts at.
    /// </summary>
    private sealed record Segment(TextPointer Start, int Offset, int Length);

    /// <summary>
    /// One searchable run of text - a paragraph, a list item, a table cell -
    /// with the map back to the document.
    /// </summary>
    private sealed record Unit(string Text, IReadOnlyList<Segment> Segments);

    private static List<Unit> Units(FlowDocument document)
    {
        var units = new List<Unit>();
        var text = new StringBuilder();
        var segments = new List<Segment>();

        void Close()
        {
            if (text.Length > 0)
            {
                units.Add(new Unit(text.ToString(), [..segments]));
            }

            text.Clear();
            segments.Clear();
        }

        var at = document.ContentStart;

        while (at is not null)
        {
            switch (at.GetPointerContext(LogicalDirection.Forward))
            {
                case TextPointerContext.Text:
                    var run = at.GetTextInRun(LogicalDirection.Forward);

                    segments.Add(new Segment(at, text.Length, run.Length));
                    text.Append(run);
                    break;

                case TextPointerContext.ElementStart or TextPointerContext.ElementEnd:
                    // Crossing into or out of a Run, a Span, a Bold or a
                    // Hyperlink keeps the unit going - that is the whole point,
                    // because it is what lets a half-bold word be found.
                    // Anything else is a boundary.
                    if (!KeepsGoing(at.GetAdjacentElement(LogicalDirection.Forward)))
                    {
                        Close();
                    }

                    break;

                case TextPointerContext.EmbeddedElement:
                    Close();
                    break;
            }

            at = at.GetNextContextPosition(LogicalDirection.Forward);
        }

        Close();

        return units;
    }

    private static bool KeepsGoing(DependencyObject? element) =>
        element is Inline and not LineBreak and not InlineUIContainer;

    /// <summary>
    /// The position in the document that a character offset in a unit's text
    /// came from.
    /// </summary>
    private static TextPointer? PointerAt(Unit unit, int offset)
    {
        // Backwards, so an offset that falls exactly on a boundary resolves to
        // the start of the next segment rather than the end of the previous one.
        // They are the same place in the text, but the next segment's start is
        // the one inside the text that follows, which is what a selection
        // starting there has to be.
        for (var i = unit.Segments.Count - 1; i >= 0; i--)
        {
            var segment = unit.Segments[i];

            if (segment.Offset <= offset)
            {
                return segment.Start.GetPositionAtOffset(offset - segment.Offset) ?? segment.Start;
            }
        }

        return unit.Segments.Count > 0 ? unit.Segments[0].Start : null;
    }
}
