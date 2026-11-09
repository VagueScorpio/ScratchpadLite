using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using ScratchpadLite.App.Bridge;
using ScratchpadLite.App.Editor;
using ScratchpadLite.Core.Model;
using Xunit;

// The IR and WPF both have a Paragraph, a Block and an Inline. In WPF-side
// code the WPF ones are meant; the IR versions are reached through Core.Model.
using Block = System.Windows.Documents.Block;
using Inline = System.Windows.Documents.Inline;
using Paragraph = System.Windows.Documents.Paragraph;

namespace ScratchpadLite.App.Tests;

/// <summary>
/// Section 10.1: each formatting command on a selection, on an empty selection,
/// and across mixed formatting, and one undo reversing each.
/// </summary>
/// <remarks>
/// <c>[StaFact]</c> from Xunit.StaFact is what puts these on an STA thread;
/// WPF text objects cannot be created on the xUnit worker thread.
/// </remarks>
public class FormatCommandsTests
{
    private static RichTextBox NewEditor(string text = "The quick brown fox")
    {
        var rtb = new RichTextBox(new FlowDocument(new Paragraph(new Run(text))))
        {
            // Matches EditorView, so the tests exercise the real configuration.
            UndoLimit = 500,
        };

        return rtb;
    }

    private static void SelectAll(RichTextBox rtb) =>
        rtb.Selection.Select(rtb.Document.ContentStart, rtb.Document.ContentEnd);

    private static Run FirstRun(RichTextBox rtb) =>
        ((Paragraph)rtb.Document.Blocks.First()).Inlines.OfType<Run>().First();

    // ------------------------------------------------------------- bold ----

    [StaFact]
    public void ToggleBold_AppliesAndRemoves()
    {
        var rtb = NewEditor();
        SelectAll(rtb);

        FormatCommands.ToggleBold(rtb);
        Assert.Equal(FontWeights.Bold, rtb.Selection.GetPropertyValue(TextElement.FontWeightProperty));

        FormatCommands.ToggleBold(rtb);
        Assert.Equal(FontWeights.Normal, rtb.Selection.GetPropertyValue(TextElement.FontWeightProperty));
    }

    [StaFact]
    public void ToggleBold_OnMixedSelection_MakesTheWholeSelectionBold()
    {
        var rtb = NewEditor();

        // Bold only the first word, then select everything: WPF reports
        // UnsetValue for the mixed range, and the command should resolve that to
        // "make it all bold" rather than toggling half of it off.
        var start = rtb.Document.ContentStart.GetPositionAtOffset(1)!;
        rtb.Selection.Select(start, start.GetPositionAtOffset(3)!);
        FormatCommands.ToggleBold(rtb);

        SelectAll(rtb);
        Assert.Equal(DependencyProperty.UnsetValue, rtb.Selection.GetPropertyValue(TextElement.FontWeightProperty));

        FormatCommands.ToggleBold(rtb);
        Assert.Equal(FontWeights.Bold, rtb.Selection.GetPropertyValue(TextElement.FontWeightProperty));
    }

    [StaFact]
    public void ToggleBold_IsOneUndoStep()
    {
        // Undo needs a hosted control: on a RichTextBox that is not in a visual
        // tree, CanUndo stays false for every edit, a plain text change
        // included, so a detached control cannot test undo at all.
        using var host = new HostedEditor();
        SelectAll(host.Editor);

        FormatCommands.ToggleBold(host.Editor);
        Assert.True(host.Editor.CanUndo);

        host.Editor.Undo();

        SelectAll(host.Editor);
        Assert.NotEqual(
            FontWeights.Bold,
            host.Editor.Selection.GetPropertyValue(TextElement.FontWeightProperty));
    }

    [StaFact]
    public void SetUnderline_IsOneUndoStep_DespiteBeingSeveralChanges()
    {
        // This is the test that earns BeginChange/EndChange its place: setting a
        // double underline writes two decorations and an attached property, and
        // one Ctrl+Z has to take all of it back, not a third of it.
        using var host = new HostedEditor();
        SelectAll(host.Editor);

        FormatCommands.SetUnderline(host.Editor, UnderlineKind.Double);
        Assert.Equal(UnderlineKind.Double, SpProps.ReadUnderline(host.Editor.Selection));

        host.Editor.Undo();
        SelectAll(host.Editor);

        var decorations = host.Editor.Selection.GetPropertyValue(Inline.TextDecorationsProperty)
            as TextDecorationCollection;

        Assert.True(
            decorations is null || decorations.All(d => d.Location != TextDecorationLocation.Underline),
            "one undo should remove the whole underline");
    }

    [StaFact]
    public void ClearFormatting_IsOneUndoStep()
    {
        using var host = new HostedEditor();
        var styles = StyleSheet.Default;

        SelectAll(host.Editor);
        FormatCommands.ToggleBold(host.Editor);
        FormatCommands.SetUnderline(host.Editor, UnderlineKind.Wavy);
        FormatCommands.SetTextColor(host.Editor, Colors.Red);

        SelectAll(host.Editor);
        FormatCommands.ClearFormatting(host.Editor, styles);

        SelectAll(host.Editor);
        Assert.NotEqual(
            FontWeights.Bold,
            host.Editor.Selection.GetPropertyValue(TextElement.FontWeightProperty));

        host.Editor.Undo();

        SelectAll(host.Editor);
        Assert.Equal(
            FontWeights.Bold,
            host.Editor.Selection.GetPropertyValue(TextElement.FontWeightProperty));
    }

    /// <summary>
    /// A RichTextBox inside a shown window, which is what makes the undo stack
    /// live. The window is deliberately not activated, so a test run does not
    /// steal focus from whatever the user is doing.
    /// </summary>
    private sealed class HostedEditor : IDisposable
    {
        private readonly Window _window;

        internal HostedEditor(string text = "The quick brown fox")
        {
            Editor = NewEditor(text);
            _window = new Window
            {
                Content = Editor,
                Width = 400,
                Height = 300,
                ShowActivated = false,
                ShowInTaskbar = false,
            };

            _window.Show();
        }

        internal RichTextBox Editor { get; }

        public void Dispose() => _window.Close();
    }

    // ------------------------------------------------------- underlines ----

    [StaTheory]
    [InlineData(UnderlineKind.Single)]
    [InlineData(UnderlineKind.Double)]
    [InlineData(UnderlineKind.Dotted)]
    [InlineData(UnderlineKind.Dashed)]
    [InlineData(UnderlineKind.Wavy)]
    public void SetUnderline_RecordsTheKindItWasAskedFor(UnderlineKind kind)
    {
        var rtb = NewEditor();
        SelectAll(rtb);

        FormatCommands.SetUnderline(rtb, kind);

        // The kind is what gets saved. Reading it back from the decorations
        // would not round-trip, which is exactly why SpProps.Underline exists:
        // a wavy underline is drawn approximately but must still save as wavy.
        Assert.Equal(kind, SpProps.ReadUnderline(rtb.Selection));

        var decorations = (TextDecorationCollection)rtb.Selection.GetPropertyValue(
            Inline.TextDecorationsProperty);
        Assert.Contains(decorations, d => d.Location == TextDecorationLocation.Underline);
    }

    [StaFact]
    public void SetUnderline_Double_UsesTwoLinesAtDifferentOffsets()
    {
        var rtb = NewEditor();
        SelectAll(rtb);

        FormatCommands.SetUnderline(rtb, UnderlineKind.Double);

        var decorations = (TextDecorationCollection)rtb.Selection.GetPropertyValue(
            Inline.TextDecorationsProperty);

        var underlines = decorations.Where(d => d.Location == TextDecorationLocation.Underline).ToList();
        Assert.Equal(2, underlines.Count);
        Assert.NotEqual(underlines[0].PenOffset, underlines[1].PenOffset);
    }

    [StaFact]
    public void SetUnderline_None_RemovesTheUnderlineButKeepsStrikethrough()
    {
        var rtb = NewEditor();
        SelectAll(rtb);

        FormatCommands.ToggleStrikethrough(rtb);
        FormatCommands.SetUnderline(rtb, UnderlineKind.Single);
        FormatCommands.SetUnderline(rtb, UnderlineKind.None);

        var decorations = (TextDecorationCollection)rtb.Selection.GetPropertyValue(
            Inline.TextDecorationsProperty);

        Assert.DoesNotContain(decorations, d => d.Location == TextDecorationLocation.Underline);
        Assert.Contains(decorations, d => d.Location == TextDecorationLocation.Strikethrough);
    }

    // --------------------------------------------- super and subscript ----

    [StaFact]
    public void Superscript_ShrinksTheTextAndRemembersTheOriginalSize()
    {
        var rtb = NewEditor();
        rtb.Document.FontSize = Units.PointsToDips(12);
        SelectAll(rtb);
        FormatCommands.SetFontSize(rtb, 12);

        FormatCommands.SetVerticalAlignment(rtb, VAlign.Super);

        Assert.Equal(
            BaselineAlignment.Superscript,
            rtb.Selection.GetPropertyValue(Inline.BaselineAlignmentProperty));

        // 65% of 12 pt. The stored base size is what makes turning it off able
        // to restore the real size rather than guessing.
        var sizeDip = (double)rtb.Selection.GetPropertyValue(TextElement.FontSizeProperty);
        Assert.Equal(Units.PointsToDips(12) * 0.65, sizeDip, 3);
        Assert.Equal(12d, SpProps.ReadBaseSizePt(rtb.Selection));
    }

    [StaFact]
    public void Superscript_Twice_TurnsItOffAndRestoresTheSize()
    {
        var rtb = NewEditor();
        SelectAll(rtb);
        FormatCommands.SetFontSize(rtb, 14);

        FormatCommands.SetVerticalAlignment(rtb, VAlign.Super);
        FormatCommands.SetVerticalAlignment(rtb, VAlign.Super);

        Assert.Equal(VAlign.Normal, SpProps.ReadVAlign(rtb.Selection));
        Assert.Equal(
            BaselineAlignment.Baseline,
            rtb.Selection.GetPropertyValue(Inline.BaselineAlignmentProperty));

        var sizePt = Units.DipsToPoints((double)rtb.Selection.GetPropertyValue(TextElement.FontSizeProperty));
        Assert.Equal(14, sizePt, 3);
    }

    [StaFact]
    public void SubscriptThenSuperscript_SwapsWithoutShrinkingTwice()
    {
        var rtb = NewEditor();
        SelectAll(rtb);
        FormatCommands.SetFontSize(rtb, 10);

        FormatCommands.SetVerticalAlignment(rtb, VAlign.Sub);
        FormatCommands.SetVerticalAlignment(rtb, VAlign.Super);

        // Shrinking twice would give 10 * 0.65 * 0.65 = 4.2 pt, which is the bug
        // this test exists to catch.
        var sizePt = Units.DipsToPoints((double)rtb.Selection.GetPropertyValue(TextElement.FontSizeProperty));
        Assert.Equal(6.5, sizePt, 3);
        Assert.Equal(VAlign.Super, SpProps.ReadVAlign(rtb.Selection));
    }

    // ------------------------------------------------------------ sizes ----

    [StaFact]
    public void GrowAndShrink_StepThroughThePresetList()
    {
        var rtb = NewEditor();
        SelectAll(rtb);
        FormatCommands.SetFontSize(rtb, 11);

        FormatCommands.GrowFont(rtb);
        Assert.Equal(12, CurrentSizePt(rtb), 3);

        FormatCommands.GrowFont(rtb);
        Assert.Equal(14, CurrentSizePt(rtb), 3);

        FormatCommands.ShrinkFont(rtb);
        Assert.Equal(12, CurrentSizePt(rtb), 3);

        // 10.5 is in the list, so shrinking from 11 must land on it rather than
        // skipping to 10.
        FormatCommands.SetFontSize(rtb, 11);
        FormatCommands.ShrinkFont(rtb);
        Assert.Equal(10.5, CurrentSizePt(rtb), 3);
    }

    [StaFact]
    public void GrowFont_StopsAtTheTopOfTheList()
    {
        var rtb = NewEditor();
        SelectAll(rtb);
        FormatCommands.SetFontSize(rtb, 72);

        FormatCommands.GrowFont(rtb);

        Assert.Equal(72, CurrentSizePt(rtb), 3);
    }

    [StaFact]
    public void SetFontSize_AcceptsHalfSizesAndRejectsOutOfRange()
    {
        var rtb = NewEditor();
        SelectAll(rtb);

        FormatCommands.SetFontSize(rtb, 10.5);
        Assert.Equal(10.5, CurrentSizePt(rtb), 3);

        FormatCommands.SetFontSize(rtb, 0);
        Assert.Equal(10.5, CurrentSizePt(rtb), 3);

        FormatCommands.SetFontSize(rtb, 5000);
        Assert.Equal(10.5, CurrentSizePt(rtb), 3);
    }

    private static double CurrentSizePt(RichTextBox rtb) =>
        Units.DipsToPoints((double)rtb.Selection.GetPropertyValue(TextElement.FontSizeProperty));

    // ------------------------------------------------------ change case ----

    [StaTheory]
    [InlineData(CaseMode.Upper, "THE QUICK BROWN FOX")]
    [InlineData(CaseMode.Lower, "the quick brown fox")]
    [InlineData(CaseMode.Title, "The Quick Brown Fox")]
    public void ChangeCase_RewritesTheLetters(CaseMode mode, string expected)
    {
        var rtb = NewEditor("the quick BROWN fox");
        SelectAll(rtb);

        FormatCommands.ChangeCase(rtb, mode);

        Assert.Equal(expected, FirstRun(rtb).Text);
    }

    [StaFact]
    public void ChangeCase_Sentence_CapitalisesAfterEachFullStop()
    {
        var rtb = NewEditor("one thing. another THING! a third? yes");
        SelectAll(rtb);

        FormatCommands.ChangeCase(rtb, CaseMode.Sentence);

        Assert.Equal("One thing. Another thing! A third? Yes", FirstRun(rtb).Text);
    }

    [StaFact]
    public void ChangeCase_KeepsTheFormattingOfEachRun()
    {
        var rtb = NewEditor();

        // Bold the first word only, then change the case of everything. Doing
        // this by replacing Selection.Text would flatten the whole selection to
        // one format, which is what this guards against.
        var start = rtb.Document.ContentStart.GetPositionAtOffset(1)!;
        rtb.Selection.Select(start, start.GetPositionAtOffset(3)!);
        FormatCommands.ToggleBold(rtb);

        SelectAll(rtb);
        FormatCommands.ChangeCase(rtb, CaseMode.Upper);

        var runs = ((Paragraph)rtb.Document.Blocks.First()).Inlines.OfType<Run>().ToList();
        Assert.True(runs.Count > 1, "the bold run should still be separate");
        Assert.Contains(runs, r => r.FontWeight == FontWeights.Bold);
        Assert.Equal("THE QUICK BROWN FOX", string.Concat(runs.Select(r => r.Text)));
    }

    [StaFact]
    public void ChangeCase_WithNoSelection_DoesNothing()
    {
        var rtb = NewEditor();
        rtb.CaretPosition = rtb.Document.ContentStart;

        FormatCommands.ChangeCase(rtb, CaseMode.Upper);

        Assert.Equal("The quick brown fox", FirstRun(rtb).Text);
    }

    // -------------------------------------------------------- paragraphs ----

    [StaTheory]
    [InlineData(Align.Left, TextAlignment.Left)]
    [InlineData(Align.Center, TextAlignment.Center)]
    [InlineData(Align.Right, TextAlignment.Right)]
    [InlineData(Align.Justify, TextAlignment.Justify)]
    public void SetAlignment_Applies(Align align, TextAlignment expected)
    {
        var rtb = NewEditor();
        SelectAll(rtb);

        FormatCommands.SetAlignment(rtb, align);

        Assert.Equal(expected, rtb.Selection.GetPropertyValue(Block.TextAlignmentProperty));
    }

    [StaFact]
    public void SetLineSpacing_SetsBlockLineHeightSoWpfHonoursIt()
    {
        var rtb = NewEditor();
        SelectAll(rtb);
        FormatCommands.SetFontSize(rtb, 10);

        FormatCommands.SetLineSpacing(rtb, 1.5);

        var paragraph = (Paragraph)rtb.Document.Blocks.First();

        // Without BlockLineHeight, WPF ignores LineHeight and uses the font's
        // own line spacing, so the setting would appear to do nothing.
        Assert.Equal(LineStackingStrategy.BlockLineHeight, paragraph.LineStackingStrategy);
        Assert.Equal(paragraph.FontSize * 1.5, paragraph.LineHeight, 3);
    }

    [StaFact]
    public void SetIndents_ConvertsPointsToDips()
    {
        var rtb = NewEditor();
        SelectAll(rtb);

        FormatCommands.SetIndents(rtb, leftPt: 36, rightPt: null, firstLinePt: 18);

        var paragraph = (Paragraph)rtb.Document.Blocks.First();
        Assert.Equal(Units.PointsToDips(36), paragraph.Margin.Left, 3);
        Assert.Equal(Units.PointsToDips(18), paragraph.TextIndent, 3);
    }

    // ------------------------------------------------------------ styles ----

    [StaFact]
    public void SetStyle_AppliesTheStyleAndRecordsItsName()
    {
        var rtb = NewEditor();
        var styles = StyleSheet.Default;
        SelectAll(rtb);

        FormatCommands.SetStyle(rtb, "Heading 1", styles);

        var paragraph = (Paragraph)rtb.Document.Blocks.First();
        var expected = styles["Heading 1"];

        Assert.Equal("Heading 1", SpProps.GetStyleName(paragraph));
        Assert.Equal(Units.PointsToDips(expected.SizePt), paragraph.FontSize, 3);
        Assert.Equal(FontWeights.Bold, paragraph.FontWeight);
    }

    [StaFact]
    public void SetStyle_WithAnUnknownName_FallsBackToNormal()
    {
        var rtb = NewEditor();
        var styles = StyleSheet.Default;
        SelectAll(rtb);

        // A .docx can carry any style name at all, so an unknown name has to be
        // handled rather than thrown (Section 7.2).
        FormatCommands.SetStyle(rtb, "Nonexistent Style From Some Docx", styles);

        var paragraph = (Paragraph)rtb.Document.Blocks.First();
        Assert.Equal("Normal", SpProps.GetStyleName(paragraph));
    }

    [StaFact]
    public void ClearFormatting_RemovesDirectFormattingButKeepsTheStyle()
    {
        var rtb = NewEditor();
        var styles = StyleSheet.Default;
        SelectAll(rtb);

        FormatCommands.SetStyle(rtb, "Heading 2", styles);
        FormatCommands.SetFontSize(rtb, 48);
        FormatCommands.ToggleItalic(rtb);
        FormatCommands.SetUnderline(rtb, UnderlineKind.Wavy);

        SelectAll(rtb);
        FormatCommands.ClearFormatting(rtb, styles);

        var paragraph = (Paragraph)rtb.Document.Blocks.First();

        // This is the distinction that makes styles worth having: clearing
        // returns the text to its style, not to nothing.
        Assert.Equal("Heading 2", SpProps.GetStyleName(paragraph));
        Assert.Equal(Units.PointsToDips(styles["Heading 2"].SizePt), paragraph.FontSize, 3);
        Assert.Equal(FontStyles.Normal, paragraph.FontStyle);
    }

    // ----------------------------------------------------------- colours ----

    [StaFact]
    public void SetTextColorAndHighlight_Apply()
    {
        var rtb = NewEditor();
        SelectAll(rtb);

        FormatCommands.SetTextColor(rtb, Colors.Red);
        FormatCommands.SetHighlight(rtb, Colors.Yellow);

        Assert.Equal(
            Colors.Red,
            ((SolidColorBrush)rtb.Selection.GetPropertyValue(TextElement.ForegroundProperty)).Color);
        Assert.Equal(
            Colors.Yellow,
            ((SolidColorBrush)rtb.Selection.GetPropertyValue(TextElement.BackgroundProperty)).Color);
    }

    [StaFact]
    public void SetHighlight_WithNull_ClearsIt()
    {
        var rtb = NewEditor();
        SelectAll(rtb);

        FormatCommands.SetHighlight(rtb, Colors.Yellow);
        FormatCommands.SetHighlight(rtb, null);

        Assert.Null(rtb.Selection.GetPropertyValue(TextElement.BackgroundProperty));
    }

    // ---------------------------------------------------- format painter ----

    [StaFact]
    public void FormatPainter_CarriesFormattingToTheNextSelection()
    {
        var rtb = NewEditor("source target");
        var painter = new FormatPainter();

        var start = rtb.Document.ContentStart.GetPositionAtOffset(1)!;
        var sourceEnd = start.GetPositionAtOffset(6)!;

        rtb.Selection.Select(start, sourceEnd);
        FormatCommands.ToggleBold(rtb);
        FormatCommands.SetTextColor(rtb, Colors.Blue);

        rtb.Selection.Select(start, sourceEnd);
        painter.Pick(rtb, sticky: false);
        Assert.True(painter.IsActive);

        var targetStart = sourceEnd.GetPositionAtOffset(1)!;
        rtb.Selection.Select(targetStart, rtb.Document.ContentEnd);
        painter.Paint(rtb);

        Assert.Equal(FontWeights.Bold, rtb.Selection.GetPropertyValue(TextElement.FontWeightProperty));

        // A single-click paint is spent after one use.
        Assert.False(painter.IsActive);
    }

    [StaFact]
    public void FormatPainter_StickyStaysOnUntilCancelled()
    {
        var rtb = NewEditor("a b c");
        var painter = new FormatPainter();

        SelectAll(rtb);
        FormatCommands.ToggleBold(rtb);
        painter.Pick(rtb, sticky: true);

        painter.Paint(rtb);
        Assert.True(painter.IsActive);

        painter.Cancel();
        Assert.False(painter.IsActive);
    }

    // ------------------------------------------------------- word count ----

    [StaTheory]
    [InlineData("", 0)]
    [InlineData("one", 1)]
    [InlineData("one two three", 3)]
    [InlineData("  spaced   out  ", 2)]
    [InlineData("line\r\nbreak", 2)]
    public void CountWords_HandlesTheAwkwardCases(string text, int expected) =>
        Assert.Equal(expected, EditorView.CountWords(text));
}
