using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using ScratchpadLite.App.Editor;
using Xunit;

namespace ScratchpadLite.App.Tests;

/// <summary>
/// Find and replace (Section 6.10). The test the whole class exists for is
/// <see cref="FindsAMatchThatCrossesRuns"/>: a RichTextBox holds a tree rather
/// than a string, so a word with its first half in bold is two Runs, and the
/// obvious implementation of find would never see it.
/// </summary>
[Collection(nameof(AppHost))]
public class FindEngineTests
{
    // --------------------------------------------------------------- finding --

    [Fact]
    public void FindsAPlainMatch()
    {
        var found = AppHost.Run(() => FindIn(Paragraphs("the cat sat on the mat"), "cat"));

        Assert.Single(found);
        Assert.Equal("cat", found[0]);
    }

    [Fact]
    public void FindsEveryMatchInDocumentOrder()
    {
        var found = AppHost.Run(() =>
        {
            var document = Paragraphs("one two one", "one");

            return FindEngine.All(document, new FindEngine.Options("one"))
                .Select(m => m.Start.GetOffsetToPosition(document.ContentStart))
                .ToList();
        });

        Assert.Equal(3, found.Count);

        // Each offset to the start is more negative than the last, which is
        // document order expressed in the only unit a TextPointer has.
        Assert.True(found[0] > found[1] && found[1] > found[2], "matches should be in document order");
    }

    [Fact]
    public void IgnoresCaseUnlessAsked()
    {
        var (insensitive, sensitive) = AppHost.Run(() =>
        {
            var document = Paragraphs("Cat cat CAT");

            return (
                FindEngine.All(document, new FindEngine.Options("cat")).Count,
                FindEngine.All(document, new FindEngine.Options("cat", MatchCase: true)).Count);
        });

        Assert.Equal(3, insensitive);
        Assert.Equal(1, sensitive);
    }

    [Fact]
    public void WholeWordSkipsAMatchInsideAWord()
    {
        var (loose, whole) = AppHost.Run(() =>
        {
            var document = Paragraphs("cat cathedral bobcat cat.");

            return (
                FindEngine.All(document, new FindEngine.Options("cat")).Count,
                FindEngine.All(document, new FindEngine.Options("cat", WholeWord: true)).Count);
        });

        Assert.Equal(4, loose);

        // "cat" and "cat." - the full stop is not a word character, so the
        // last one counts.
        Assert.Equal(2, whole);
    }

    /// <summary>
    /// The reason this engine flattens the document rather than walking runs.
    /// </summary>
    [Fact]
    public void FindsAMatchThatCrossesRuns()
    {
        var found = AppHost.Run(() =>
        {
            // "formatting" split across three runs, the middle one bold, which
            // is exactly what typing a word and emboldening its middle makes.
            var paragraph = new Paragraph();

            paragraph.Inlines.Add(new Run("for"));
            paragraph.Inlines.Add(new Bold(new Run("matt")));
            paragraph.Inlines.Add(new Run("ing is fine"));

            var document = new FlowDocument(paragraph);

            return FindEngine.All(document, new FindEngine.Options("formatting"))
                .Select(m => m.Text)
                .ToList();
        });

        Assert.Single(found);
        Assert.Equal("formatting", found[0]);
    }

    [Fact]
    public void FindsAMatchInsideALink()
    {
        var found = AppHost.Run(() =>
        {
            var paragraph = new Paragraph();

            paragraph.Inlines.Add(new Run("see "));
            paragraph.Inlines.Add(new Hyperlink(new Run("the manual")) { NavigateUri = new Uri("https://e.org") });

            return FindEngine.All(new FlowDocument(paragraph), new FindEngine.Options("see the manual")).Count;
        });

        // A hyperlink is an inline like any other, so the text runs straight
        // through it.
        Assert.Equal(1, found);
    }

    [Fact]
    public void DoesNotMatchAcrossParagraphs()
    {
        var found = AppHost.Run(() =>
            FindEngine.All(Paragraphs("the end", "begins here"), new FindEngine.Options("end begins")).Count);

        // Not a limitation to work around: a TextRange spanning two paragraphs
        // cannot be given replacement text without destroying the break between
        // them, so a match there would be one that could not be replaced.
        Assert.Equal(0, found);
    }

    [Fact]
    public void DoesNotMatchAcrossALineBreak()
    {
        var found = AppHost.Run(() =>
        {
            var paragraph = new Paragraph();

            paragraph.Inlines.Add(new Run("hard"));
            paragraph.Inlines.Add(new LineBreak());
            paragraph.Inlines.Add(new Run("wired"));

            return FindEngine.All(new FlowDocument(paragraph), new FindEngine.Options("hardwired")).Count;
        });

        Assert.Equal(0, found);
    }

    [Fact]
    public void DoesNotMatchAcrossAPicture()
    {
        var found = AppHost.Run(() =>
        {
            var paragraph = new Paragraph();

            paragraph.Inlines.Add(new Run("be"));
            paragraph.Inlines.Add(new InlineUIContainer(new System.Windows.Controls.Image { Width = 8, Height = 8 }));
            paragraph.Inlines.Add(new Run("fore"));

            return FindEngine.All(new FlowDocument(paragraph), new FindEngine.Options("before")).Count;
        });

        Assert.Equal(0, found);
    }

    [Fact]
    public void FindsInsideATableCellAndAListItem()
    {
        var (count, texts) = AppHost.Run(() =>
        {
            var row = new TableRow();
            row.Cells.Add(new TableCell(new Paragraph(new Run("needle in a cell"))));

            var group = new TableRowGroup();
            group.Rows.Add(row);

            var table = new Table();
            table.RowGroups.Add(group);

            var list = new List();
            list.ListItems.Add(new ListItem(new Paragraph(new Run("needle in an item"))));

            var document = new FlowDocument();
            document.Blocks.Add(table);
            document.Blocks.Add(list);

            var matches = FindEngine.All(document, new FindEngine.Options("needle"));

            return (matches.Count, matches.Select(m => m.Text).ToList());
        });

        Assert.Equal(2, count);

        // EndsWith, not Equal, and the reason is worth knowing: a TextRange
        // that starts at the beginning of a list item's paragraph reports its
        // Text with the list marker in front of it, so the match in the list
        // comes back as "•\tneedle". The range itself is right - the
        // replacement test below proves the bullet is not part of what gets
        // replaced - it is only Text that draws the marker in.
        Assert.All(texts, text => Assert.EndsWith("needle", text, StringComparison.Ordinal));
    }

    [Fact]
    public void ReplacingTheFirstWordOfAListItemKeepsTheBullet()
    {
        var (text, markers) = AppHost.Run(() =>
        {
            var list = new List();
            list.ListItems.Add(new ListItem(new Paragraph(new Run("needle in an item"))));

            var editor = new RichTextBox(new FlowDocument());
            editor.Document.Blocks.Add(list);

            FindEngine.ReplaceAll(editor, new FindEngine.Options("needle"), "pin");

            var paragraph = (Paragraph)list.ListItems.FirstListItem!.Blocks.FirstBlock!;

            return (
                new TextRange(paragraph.ContentStart, paragraph.ContentEnd).Text,
                list.ListItems.Count);
        });

        Assert.Equal("•\tpin in an item", text);
        Assert.Equal(1, markers);
    }

    [Fact]
    public void FindsNothingForAnEmptyNeedle()
    {
        var found = AppHost.Run(() =>
            FindEngine.All(Paragraphs("anything at all"), new FindEngine.Options("")).Count);

        Assert.Equal(0, found);
    }

    // -------------------------------------------------------------- regular --

    [Fact]
    public void RunsARegularExpression()
    {
        var found = AppHost.Run(() =>
            FindEngine.All(
                    Paragraphs("call 555-1234 or 555-9876"),
                    new FindEngine.Options(@"\d{3}-\d{4}", UseRegex: true))
                .Select(m => m.Text)
                .ToList());

        Assert.Equal(["555-1234", "555-9876"], found);
    }

    [Fact]
    public void AnInvalidPatternThrowsSomethingTheBarCanShow()
    {
        var message = AppHost.Run(() =>
            Assert.Throws<FindEngine.BadPatternException>(
                () => FindEngine.All(Paragraphs("text"), new FindEngine.Options("(unclosed", UseRegex: true)))
                .Message);

        // Section 6.10: the bar shows the error. So there has to be one worth
        // showing, rather than a crash or a silent nothing.
        Assert.NotEmpty(message);
    }

    [Fact]
    public void APatternThatMatchesNothingAtAllFindsNothing()
    {
        var found = AppHost.Run(() =>
            FindEngine.All(Paragraphs("aaa"), new FindEngine.Options("b*", UseRegex: true)).Count);

        // "b*" matches the empty string at every position. Replacing those
        // would insert the replacement between every pair of characters, which
        // is never what Replace All was meant to do.
        Assert.Equal(0, found);
    }

    [Fact]
    public void RegexHonoursMatchCaseAndWholeWord()
    {
        var (anyCase, exactCase, whole) = AppHost.Run(() =>
        {
            var document = Paragraphs("Cat catalogue");

            return (
                FindEngine.All(document, new FindEngine.Options("cat", UseRegex: true)).Count,
                FindEngine.All(document, new FindEngine.Options("cat", MatchCase: true, UseRegex: true)).Count,
                FindEngine.All(document, new FindEngine.Options("cat", WholeWord: true, UseRegex: true)).Count);
        });

        Assert.Equal(2, anyCase);
        Assert.Equal(1, exactCase);
        Assert.Equal(1, whole);
    }

    // ------------------------------------------------------------ replacing --

    [Fact]
    public void ReplaceSwapsTheText()
    {
        var text = AppHost.Run(() =>
        {
            var editor = Editor("the cat sat");
            var match = FindEngine.All(editor.Document, new FindEngine.Options("cat"))[0];

            FindEngine.Replace(editor, match, "dog");

            return AllText(editor);
        });

        Assert.Equal("the dog sat", text);
    }

    [Fact]
    public void ReplaceKeepsTheFormattingOfTheFirstCharacter()
    {
        var (weight, colour) = AppHost.Run(() =>
        {
            var paragraph = new Paragraph();

            paragraph.Inlines.Add(new Run("plain "));
            paragraph.Inlines.Add(new Run("CAT") { FontWeight = FontWeights.Bold, Foreground = Brushes.Red });
            paragraph.Inlines.Add(new Run(" plain"));

            var editor = new RichTextBox(new FlowDocument(paragraph));
            var match = FindEngine.All(editor.Document, new FindEngine.Options("CAT"))[0];

            FindEngine.Replace(editor, match, "DOG");

            var replaced = FindEngine.All(editor.Document, new FindEngine.Options("DOG"))[0];

            return (
                replaced.GetPropertyValue(TextElement.FontWeightProperty),
                replaced.GetPropertyValue(TextElement.ForegroundProperty)?.ToString());
        });

        Assert.Equal(FontWeights.Bold, weight);
        Assert.Equal(Brushes.Red.ToString(), colour);
    }

    /// <summary>
    /// The underline kind lives in an attached property of this app's own
    /// (Section 6.8), and <c>TextRange.ApplyPropertyValue</c> refuses anything
    /// WPF does not call text formatting - so a replacement that carried
    /// formatting through the TextRange methods alone threw on every single
    /// replace.
    /// </summary>
    [Fact]
    public void ReplaceKeepsTheUnderlineKind()
    {
        var kind = AppHost.Run(() =>
        {
            var run = new Run("CAT");
            ScratchpadLite.App.Bridge.SpProps.SetUnderline(run, ScratchpadLite.Core.Model.UnderlineKind.Wavy);

            var paragraph = new Paragraph();
            paragraph.Inlines.Add(new Run("a "));
            paragraph.Inlines.Add(run);

            var editor = new RichTextBox(new FlowDocument(paragraph));

            FindEngine.ReplaceAll(editor, new FindEngine.Options("CAT"), "DOG");

            var replaced = FindEngine.All(editor.Document, new FindEngine.Options("DOG"))[0];

            return replaced.Start.Parent is Inline inline
                ? (ScratchpadLite.Core.Model.UnderlineKind?)ScratchpadLite.App.Bridge.SpProps.GetUnderline(inline)
                : null;
        });

        Assert.Equal(ScratchpadLite.Core.Model.UnderlineKind.Wavy, kind);
    }

    [Fact]
    public void ReplaceWithNothingDeletesTheMatch()
    {
        var text = AppHost.Run(() =>
        {
            var editor = Editor("delete the word here");
            var match = FindEngine.All(editor.Document, new FindEngine.Options("the word "))[0];

            FindEngine.Replace(editor, match, "");

            return AllText(editor);
        });

        Assert.Equal("delete here", text);
    }

    [Fact]
    public void ReplaceAllReplacesEveryOneAndSaysHowMany()
    {
        var (count, text) = AppHost.Run(() =>
        {
            var editor = Editor("a cat, a cat and one more cat");
            var replaced = FindEngine.ReplaceAll(editor, new FindEngine.Options("cat"), "dog");

            return (replaced, AllText(editor));
        });

        Assert.Equal(3, count);
        Assert.Equal("a dog, a dog and one more dog", text);
    }

    [Fact]
    public void ReplaceAllWorksWhenTheReplacementIsLongerThanTheMatch()
    {
        // The case that catches an implementation replacing forwards with stale
        // positions: every replacement after the first lands in the wrong
        // place.
        var text = AppHost.Run(() =>
        {
            var editor = Editor("x x x x");

            FindEngine.ReplaceAll(editor, new FindEngine.Options("x"), "yyyyy");

            return AllText(editor);
        });

        Assert.Equal("yyyyy yyyyy yyyyy yyyyy", text);
    }

    /// <summary>
    /// Runs in a focused editor rather than a detached one, and has to: a
    /// RichTextBox that is not in a shown window keeps no undo history at all,
    /// so this test passed its Replace All and then found Undo had done
    /// nothing - which is indistinguishable from a Replace All that was five
    /// undo steps.
    /// </summary>
    [Fact]
    public void ReplaceAllIsOneUndoStep()
    {
        var (afterReplace, afterUndo) = AppHost.InFocusedEditor(editor =>
        {
            editor.Document = new FlowDocument(new Paragraph(new Run("cat cat cat cat cat")));
            editor.IsUndoEnabled = true;

            FindEngine.ReplaceAll(editor, new FindEngine.Options("cat"), "dog");

            var replaced = AllText(editor);

            editor.Undo();

            return (replaced, AllText(editor));
        });

        Assert.Equal("dog dog dog dog dog", afterReplace);

        // Section 6.10: one Undo reverses the lot. Five undos to get back from
        // one Replace All is the bug this is here to catch.
        Assert.Equal("cat cat cat cat cat", afterUndo);
    }

    [Fact]
    public void ReplaceAllOnNoMatchesChangesNothing()
    {
        var (count, text) = AppHost.Run(() =>
        {
            var editor = Editor("nothing to see");
            var replaced = FindEngine.ReplaceAll(editor, new FindEngine.Options("zebra"), "horse");

            return (replaced, AllText(editor));
        });

        Assert.Equal(0, count);
        Assert.Equal("nothing to see", text);
    }

    // ------------------------------------------------------- moving about --

    [Fact]
    public void IndexFromFindsTheNextMatchAfterAPosition()
    {
        var index = AppHost.Run(() =>
        {
            var document = Paragraphs("one two one two one");
            var matches = FindEngine.All(document, new FindEngine.Options("one"));

            // Just past the first match.
            var after = matches[0].End;

            return FindEngine.IndexFrom(matches, after, backwards: false);
        });

        Assert.Equal(1, index);
    }

    [Fact]
    public void IndexFromWrapsRoundAtTheEnd()
    {
        var index = AppHost.Run(() =>
        {
            var document = Paragraphs("one one");
            var matches = FindEngine.All(document, new FindEngine.Options("one"));

            return FindEngine.IndexFrom(matches, document.ContentEnd, backwards: false);
        });

        // Section 6.10 asks for wrap-around, which is this: past the last match,
        // Next is the first one again.
        Assert.Equal(0, index);
    }

    [Fact]
    public void IndexFromWrapsRoundAtTheStartGoingBackwards()
    {
        var index = AppHost.Run(() =>
        {
            var document = Paragraphs("one one one");
            var matches = FindEngine.All(document, new FindEngine.Options("one"));

            return FindEngine.IndexFrom(matches, document.ContentStart, backwards: true);
        });

        Assert.Equal(2, index);
    }

    [Fact]
    public void PreviousFindsAMatchThatTouchesTheCurrentOne()
    {
        var index = AppHost.Run(() =>
        {
            // Four matches with no gap between them, which is where a strict
            // "ends before here" skips the one next door and wraps round to the
            // end of the document.
            var document = Paragraphs("aaaa");
            var matches = FindEngine.All(document, new FindEngine.Options("a"));

            return FindEngine.IndexFrom(matches, matches[2].Start, backwards: true);
        });

        Assert.Equal(1, index);
    }

    [Fact]
    public void IndexOfCurrentKnowsWhichMatchTheCaretIsIn()
    {
        var (inside, outside) = AppHost.Run(() =>
        {
            var document = Paragraphs("one two one");
            var matches = FindEngine.All(document, new FindEngine.Options("one"));

            var middleOfTheSecond = matches[1].Start.GetPositionAtOffset(1);

            return (
                FindEngine.IndexOfCurrent(matches, middleOfTheSecond),
                FindEngine.IndexOfCurrent(matches, matches[0].End.GetPositionAtOffset(2)));
        });

        Assert.Equal(1, inside);
        Assert.Null(outside);
    }

    [Fact]
    public void ShowSelectsTheMatch()
    {
        var selected = AppHost.InFocusedEditor(editor =>
        {
            editor.Document = new FlowDocument(new Paragraph(new Run("find the needle here")));

            var match = FindEngine.All(editor.Document, new FindEngine.Options("needle"))[0];

            FindEngine.Show(editor, match);

            return editor.Selection.Text;
        });

        // A found match the user cannot see is not found, and selecting it is
        // how Section 6.10 says to show it.
        Assert.Equal("needle", selected);
    }

    // --------------------------------------------------------------- helpers --

    private static FlowDocument Paragraphs(params string[] paragraphs)
    {
        var document = new FlowDocument();

        foreach (var text in paragraphs)
        {
            document.Blocks.Add(new Paragraph(new Run(text)));
        }

        return document;
    }

    private static RichTextBox Editor(string text) =>
        new(new FlowDocument(new Paragraph(new Run(text))));

    private static IReadOnlyList<string> FindIn(FlowDocument document, string needle) =>
        [..FindEngine.All(document, new FindEngine.Options(needle)).Select(m => m.Text)];

    /// <summary>
    /// The document's text without the paragraph breaks, which
    /// <c>TextRange.Text</c> includes and which say nothing about whether a
    /// replacement worked.
    /// </summary>
    private static string AllText(RichTextBox editor) =>
        new TextRange(editor.Document.ContentStart, editor.Document.ContentEnd)
            .Text.Replace("\r", "").Replace("\n", "");
}
