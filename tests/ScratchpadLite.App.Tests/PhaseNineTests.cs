using System.IO;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ScratchpadLite.App.Editor;
using ScratchpadLite.App.UI.Dialogs;
using ScratchpadLite.Core.Model;
using ScratchpadLite.Core.Platform;
using Xunit;

using Paragraph = System.Windows.Documents.Paragraph;

namespace ScratchpadLite.App.Tests;

/// <summary>
/// The three menu items that had been left as shells in earlier phases and
/// are built for the release: Format, Paragraph; Insert, Special Character;
/// and View, Show Formatting Marks.
/// </summary>
/// <remarks>
/// Each was reachable from the menus all along, greyed out, which is the
/// reason they are tested together: a feature that exists and cannot be
/// reached and a menu item that can be reached and does nothing are the same
/// bug seen from two sides.
/// </remarks>
[Collection(nameof(AppHost))]
public class ParagraphDialogTests
{
    private static RichTextBox Editor(params string[] paragraphs)
    {
        var document = new FlowDocument();

        foreach (var text in paragraphs)
        {
            document.Blocks.Add(new Paragraph(new Run(text)));
        }

        return new RichTextBox(document) { UndoLimit = 500 };
    }

    private static Paragraph Nth(RichTextBox rtb, int index) =>
        rtb.Document.Blocks.OfType<Paragraph>().ElementAt(index);

    [StaFact]
    public void ReadParagraph_ReportsWhatTheParagraphHas()
    {
        var rtb = Editor("One");
        var paragraph = Nth(rtb, 0);

        paragraph.TextAlignment = TextAlignment.Center;
        paragraph.Margin = new Thickness(
            Units.PointsToDips(36), Units.PointsToDips(6),
            Units.PointsToDips(18), Units.PointsToDips(12));
        paragraph.TextIndent = Units.PointsToDips(24);

        var look = FormatCommands.ReadParagraph(rtb);

        Assert.Equal(Align.Center, look.Align);
        Assert.Equal(36, look.LeftPt!.Value, 2);
        Assert.Equal(18, look.RightPt!.Value, 2);
        Assert.Equal(6, look.BeforePt!.Value, 2);
        Assert.Equal(12, look.AfterPt!.Value, 2);
        Assert.Equal(24, look.FirstLinePt!.Value, 2);
    }

    [StaFact]
    public void ReadParagraph_LeavesADisagreementBlank()
    {
        var rtb = Editor("One", "Two");

        Nth(rtb, 0).TextAlignment = TextAlignment.Left;
        Nth(rtb, 1).TextAlignment = TextAlignment.Right;

        Nth(rtb, 0).Margin = new Thickness(Units.PointsToDips(36), 0, 0, 0);
        Nth(rtb, 1).Margin = new Thickness(Units.PointsToDips(36), 0, 0, 0);

        rtb.Selection.Select(rtb.Document.ContentStart, rtb.Document.ContentEnd);

        var look = FormatCommands.ReadParagraph(rtb);

        // The one they disagree on comes back blank, the one they share does
        // not: the dialog has to be able to tell those two apart.
        Assert.Null(look.Align);
        Assert.Equal(36, look.LeftPt!.Value, 2);
    }

    [StaFact]
    public void ApplyParagraph_LeavesTheBlanksAlone()
    {
        var rtb = Editor("One");
        var paragraph = Nth(rtb, 0);

        paragraph.TextAlignment = TextAlignment.Justify;
        paragraph.Margin = new Thickness(Units.PointsToDips(36), 0, 0, 0);

        FormatCommands.ApplyParagraph(rtb, new FormatCommands.ParagraphLook { BeforePt = 18 });

        Assert.Equal(TextAlignment.Justify, paragraph.TextAlignment);
        Assert.Equal(36, Units.DipsToPoints(paragraph.Margin.Left), 2);
        Assert.Equal(18, Units.DipsToPoints(paragraph.Margin.Top), 2);
    }

    [StaFact]
    public void ApplyParagraph_IsOneUndoStep()
    {
        // The reason ApplyParagraph exists at all. SetAlignment, SetIndents and
        // SetSpacing each open a change block of their own, so a dialog that
        // called all three would need three Ctrl+Z to put back one OK.
        var done = AppHost.InFocusedEditor(editor =>
        {
            editor.Document.Blocks.Clear();
            editor.Document.Blocks.Add(new Paragraph(new Run("One")));
            AppHost.Settle();

            var paragraph = editor.Document.Blocks.OfType<Paragraph>().First();

            FormatCommands.ApplyParagraph(
                editor,
                new FormatCommands.ParagraphLook
                {
                    Align = Align.Right,
                    LeftPt = 36,
                    BeforePt = 18,
                    AfterPt = 24,
                    LineSpacing = 2.0,
                });

            var changed = paragraph.TextAlignment == TextAlignment.Right
                && paragraph.Margin.Left > 0
                && paragraph.Margin.Top > 0;

            editor.Undo();

            // The margin comes back as NaN rather than zero, because NaN is
            // what a paragraph's margin starts as - auto - and undo puts back
            // what was there rather than a number.
            return (changed,
                    undone: paragraph.TextAlignment != TextAlignment.Right
                        && (double.IsNaN(paragraph.Margin.Left) || paragraph.Margin.Left < 1));
        });

        Assert.True(done.changed, "the dialog's values should have been applied");
        Assert.True(done.undone, "one undo should have put all of them back");
    }

    [StaFact]
    public void ApplyParagraph_AutomaticLineSpacingClearsTheHeight()
    {
        var rtb = Editor("One");
        var paragraph = Nth(rtb, 0);

        FormatCommands.ApplyParagraph(rtb, new FormatCommands.ParagraphLook { LineSpacing = 1.5 });

        Assert.Equal(paragraph.FontSize * 1.5, paragraph.LineHeight, 2);
        Assert.Equal(LineStackingStrategy.BlockLineHeight, paragraph.LineStackingStrategy);

        FormatCommands.ApplyParagraph(
            rtb, new FormatCommands.ParagraphLook { LineSpacing = double.NaN });

        // Automatic is a cleared height, not a multiple of 1: a multiple of 1
        // is tighter than any real font's own line height.
        Assert.True(double.IsNaN(paragraph.LineHeight), "automatic should clear the line height");
    }

    [StaFact]
    public void ReadParagraph_CallsAnUntouchedParagraphAutomatic()
    {
        var look = FormatCommands.ReadParagraph(Editor("One"));

        Assert.NotNull(look.LineSpacing);
        Assert.True(double.IsNaN(look.LineSpacing!.Value), "a fresh paragraph has automatic spacing");
    }

    // ------------------------------------------------------- the dialog ----

    [StaFact]
    public void TheDialogShowsInchesAndGivesBackPoints()
    {
        AppHost.Run(() =>
        {
            var dialog = new ParagraphDialog();

            try
            {
                dialog.Show(new FormatCommands.ParagraphLook { LeftPt = 72, BeforePt = 12 });

                Assert.True(dialog.Commit());

                // 72 points in, one inch shown, 72 points back out.
                Assert.Equal(72, dialog.Result.LeftPt!.Value, 2);
                Assert.Equal(12, dialog.Result.BeforePt!.Value, 2);
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [StaFact]
    public void TheDialogTreatsABlankBoxAsLeaveItAlone()
    {
        AppHost.Run(() =>
        {
            var dialog = new ParagraphDialog();

            try
            {
                dialog.Show(new FormatCommands.ParagraphLook());

                Assert.True(dialog.Commit());

                Assert.Null(dialog.Result.LeftPt);
                Assert.Null(dialog.Result.RightPt);
                Assert.Null(dialog.Result.BeforePt);
                Assert.Null(dialog.Result.AfterPt);
                Assert.Null(dialog.Result.LineSpacing);
                Assert.Null(dialog.Result.Align);
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [StaFact]
    public void TheDialogRefusesAWordAndAnImpossibleNumber()
    {
        AppHost.Run(() =>
        {
            var dialog = new ParagraphDialog();

            try
            {
                var leftBox = (TextBox)dialog.FindName("LeftBox")!;

                leftBox.Text = "soon";
                Assert.False(dialog.Commit());

                leftBox.Text = "400";
                Assert.False(dialog.Commit());

                leftBox.Text = "1.5";
                Assert.True(dialog.Commit());
                Assert.Equal(108, dialog.Result.LeftPt!.Value, 2);
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [StaFact]
    public void TheDialogTakesAutomaticAndATypedMultiple()
    {
        AppHost.Run(() =>
        {
            var dialog = new ParagraphDialog();

            try
            {
                var box = (ComboBox)dialog.FindName("LineSpacingBox")!;

                box.Text = "Automatic";
                Assert.True(dialog.Commit());
                Assert.True(double.IsNaN(dialog.Result.LineSpacing!.Value));

                box.Text = "1.3";
                Assert.True(dialog.Commit());
                Assert.Equal(1.3, dialog.Result.LineSpacing!.Value, 2);

                box.Text = "never";
                Assert.False(dialog.Commit());
            }
            finally
            {
                dialog.Close();
            }
        });
    }
}

/// <summary>
/// Insert, Special Character (Section 8.1): the character grid, what it
/// offers, and the recently used list.
/// </summary>
[Collection(nameof(AppHost))]
public class SpecialCharacterTests
{
    [Fact]
    public void EveryGroupIsSmallEnoughToDrawWithoutVirtualizing()
    {
        Assert.NotEmpty(CharacterPalette.Groups);

        foreach (var group in CharacterPalette.Groups)
        {
            Assert.True(group.First < group.Last, $"{group.Name} is not a range");
            Assert.True(
                group.Last - group.First < 256,
                $"{group.Name} has {group.Last - group.First + 1} code points; "
                + "a plain WrapPanel lays out a few hundred, not a few thousand");
        }
    }

    [StaFact]
    public void AGroupOffersOnlyWhatTheFontCanDraw()
    {
        // The contract: every character offered is one the font has a glyph
        // for. Checked against the font's own map rather than against a list
        // written here, because the list would only say what was expected.
        var face = CharacterPalette.FaceFor("Arial");

        if (face is null)
        {
            // No glyph typeface for a font installed on the machine, which is
            // what a bare CI runner gives: resolving a system font by name
            // needs the Windows font cache service, while loading one from a
            // file URI - which the rest of the suite does - does not. With no
            // face there is nothing to check the offered characters against,
            // and CharactersIn offers the range unfiltered by design, so this
            // test steps aside rather than failing for the machine it is on.
            return;
        }

        var punctuation = CharacterPalette.Groups.First(g => g.Name == "Punctuation");
        var offered = CharacterPalette.CharactersIn(punctuation, "Arial");

        Assert.NotEmpty(offered);

        foreach (var character in offered)
        {
            var code = char.ConvertToUtf32(character, 0);

            Assert.InRange(code, punctuation.First, punctuation.Last);
            Assert.True(
                face.CharacterToGlyphMap.ContainsKey(code),
                $"U+{code:X4} was offered and Arial has no glyph for it");
        }
    }

    [StaFact]
    public void AGroupLeavesOutTheCharactersNothingCanShow()
    {
        var all = CharacterPalette.Groups
            .SelectMany(g => CharacterPalette.CharactersIn(g, "Segoe UI"))
            .Select(c => char.ConvertToUtf32(c, 0));

        foreach (var code in all)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(code);

            Assert.True(
                category is not (UnicodeCategory.Control or UnicodeCategory.Format
                    or UnicodeCategory.OtherNotAssigned or UnicodeCategory.PrivateUse),
                $"U+{code:X4} is a {category} and should not be on offer");
        }
    }

    [Fact]
    public void RememberPutsTheNewestFirstWithNoDuplicates()
    {
        var recent = CharacterPalette.Remember(["a", "b", "c"], "c");

        Assert.Equal(["c", "a", "b"], recent);
    }

    [Fact]
    public void RememberStopsAtTheLimit()
    {
        var many = Enumerable.Range(0, 100).Select(i => i.ToString(CultureInfo.InvariantCulture));

        var recent = CharacterPalette.Remember(many, "x");

        Assert.Equal(CharacterPalette.RecentLimit, recent.Count);
        Assert.Equal("x", recent[0]);
    }

    [Fact]
    public void DescribeNamesTheCodePoint()
    {
        Assert.StartsWith("U+00E9", CharacterPalette.Describe("é"), StringComparison.Ordinal);
        Assert.Contains("lowercase letter", CharacterPalette.Describe("é"), StringComparison.Ordinal);
        Assert.Equal("", CharacterPalette.Describe(""));
    }

    [StaFact]
    public void TheDialogHandsTheCharacterOverAndRemembersIt()
    {
        AppHost.Run(() =>
        {
            var dialog = new SpecialChars();
            var inserted = new List<string>();

            try
            {
                dialog.CharacterChosen += (_, character) => inserted.Add(character);
                dialog.Show("Segoe UI", ["©"]);

                dialog.Pick("½");
                dialog.InsertSelected();

                Assert.Equal(["½"], inserted);

                // The dialog does not insert anything itself, and the one it
                // just handed over is now at the top of the list the window
                // will save.
                Assert.Equal("½", dialog.Recent[0]);
                Assert.Equal("©", dialog.Recent[1]);
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [StaFact]
    public void TheDialogInsertsNothingWithNothingPicked()
    {
        AppHost.Run(() =>
        {
            var dialog = new SpecialChars();
            var inserted = 0;

            try
            {
                dialog.CharacterChosen += (_, _) => inserted++;
                dialog.Show("Segoe UI", []);

                dialog.InsertSelected();

                Assert.Equal(0, inserted);
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void SettingsCarryTheRecentlyUsedCharactersThroughJson()
    {
        // Phase 8's lesson, applied before it can bite again: the IR's font
        // list was written for four phases and could never be read back,
        // because System.Text.Json will not deserialize a read-only set. A
        // read-only list is fine - and this is the test that says so rather
        // than the documentation.
        var path = Path.Combine(Path.GetTempPath(), $"sp-settings-{Guid.NewGuid():N}.json");

        try
        {
            new Settings { RecentCharacters = ["½", "—"] }.Save(path);

            var read = Settings.Load(path);

            Assert.Equal(["½", "—"], read.RecentCharacters);
        }
        finally
        {
            File.Delete(path);
        }
    }
}

/// <summary>
/// View, Show Formatting Marks (Section 8.1), which Phase 1 left greyed out
/// because WPF has nothing for it.
/// </summary>
/// <remarks>
/// These tests render. Asserting that <c>Enabled</c> is true would prove
/// nothing: the marks are drawn from where the text actually landed, so the
/// only honest question is how many marks a real paint put on a real laid-out
/// page - the same reason Phase 8's one-column test counts glyph origins
/// rather than reading back the column width it set.
/// </remarks>
[Collection(nameof(AppHost))]
public class FormattingMarksTests
{
    private static T InEditorView<T>(string[] paragraphs, Func<EditorView, T> work)
    {
        return AppHost.Run(() =>
        {
            var view = new EditorView();

            var window = new Window
            {
                Content = view,
                Width = 700,
                Height = 500,
                ShowInTaskbar = false,
                Left = -4000,
                Top = -4000,
            };

            try
            {
                window.Show();
                window.UpdateLayout();
                AppHost.Settle();

                view.Editor.Document.Blocks.Clear();

                foreach (var text in paragraphs)
                {
                    view.Editor.Document.Blocks.Add(new Paragraph(new Run(text)));
                }

                window.UpdateLayout();
                AppHost.Settle();

                return work(view);
            }
            finally
            {
                window.Close();
            }
        });
    }

    /// <summary>
    /// Paints the view for real, which is what makes the overlay's
    /// <c>OnRender</c> run.
    /// </summary>
    private static int Paint(EditorView view)
    {
        view.UpdateLayout();

        var bitmap = new RenderTargetBitmap(
            Math.Max(1, (int)view.ActualWidth),
            Math.Max(1, (int)view.ActualHeight),
            96, 96, PixelFormats.Pbgra32);

        bitmap.Render(view);

        return view.MarksOverlay.LastMarkCount;
    }

    [StaFact]
    public void OffByDefaultAndNothingIsDrawn()
    {
        var marks = InEditorView(["one two", "three four"], view =>
        {
            Assert.False(view.ShowFormattingMarks, "formatting marks are off until asked for");

            return Paint(view);
        });

        Assert.Equal(0, marks);
    }

    [StaFact]
    public void OnceOnEveryParagraphGetsAPilcrowAndEverySpaceADot()
    {
        var marks = InEditorView(["one two", "three four five"], view =>
        {
            view.ShowFormattingMarks = true;

            return Paint(view);
        });

        // Two paragraphs and three spaces between the words.
        Assert.Equal(5, marks);
    }

    [StaFact]
    public void ATabAndAHardSpaceAreMarkedToo()
    {
        // The non-breaking space is built from its code point rather than
        // typed: in the source it looks exactly like an ordinary space, and a
        // tidy-up that replaced it would leave this test passing while no
        // longer testing anything.
        var line = "a\tb" + (char)0x00A0 + "c";

        var marks = InEditorView([line], view =>
        {
            view.ShowFormattingMarks = true;

            return Paint(view);
        });

        // One pilcrow, one tab arrow, one hard space.
        Assert.Equal(3, marks);
    }

    [StaFact]
    public void TurningThemOffStopsTheDrawing()
    {
        var after = InEditorView(["one two three"], view =>
        {
            view.ShowFormattingMarks = true;
            Assert.True(Paint(view) > 0, "the marks should have been drawn while on");

            view.ShowFormattingMarks = false;

            return Paint(view);
        });

        Assert.Equal(0, after);
    }

    [StaFact]
    public void TheMarksDoNotTakeTheMouse()
    {
        // An overlay over the text that swallowed clicks would make the
        // editor unusable while the marks were on.
        var hittable = InEditorView(["one"], view => view.MarksOverlay.IsHitTestVisible);

        Assert.False(hittable);
    }

    [StaFact]
    public void OnlyWhatIsOnScreenCostsAnything()
    {
        // The property that makes this affordable at all: a paint is
        // proportional to the window, not to the document. Four hundred
        // paragraphs, a window that holds perhaps twenty.
        var many = Enumerable.Repeat("a line of text here", 400).ToArray();

        var marks = InEditorView(many, view =>
        {
            view.ShowFormattingMarks = true;

            return Paint(view);
        });

        Assert.True(marks > 0, "the visible paragraphs should have been marked");
        Assert.True(
            marks < 400,
            $"{marks} marks for 400 paragraphs means the whole document was walked");
    }

    [StaFact]
    public void ALongParagraphFillingTheScreenIsStillMarked()
    {
        // This is what the first version of the visible-range check got
        // wrong: it asked where a paragraph ended, so one paragraph taller
        // than the window - its start above the fold and its end below it -
        // was skipped altogether, and the marks vanished exactly when there
        // was most to mark.
        var marks = InEditorView(
            [string.Join(" ", Enumerable.Repeat("word", 400))],
            view =>
            {
                view.ShowFormattingMarks = true;

                return Paint(view);
            });

        Assert.True(marks > 10, $"only {marks} marks inside a screenful of one paragraph");
    }

    [StaFact]
    public void WithNoViewportTheBudgetIsWhatStopsIt()
    {
        // Detached from a scroll viewer there is no "on screen", so the whole
        // document is visible and the budget is the only brake. It is a
        // backstop rather than the usual path, which is why it is tested
        // here rather than through the editor.
        var (marks, full) = AppHost.Run(() =>
        {
            var editor = AppHost.NewEditor();
            var overlay = new FormattingMarks { Target = editor };
            var host = new Grid();

            host.Children.Add(editor);
            host.Children.Add(overlay);

            var window = new Window
            {
                Content = host,
                Width = 700,
                Height = 500,
                ShowInTaskbar = false,
                Left = -4000,
                Top = -4000,
            };

            try
            {
                window.Show();
                AppHost.Settle();

                editor.Document.Blocks.Clear();
                editor.Document.Blocks.Add(
                    new Paragraph(new Run(string.Join(" ", Enumerable.Repeat("word", 12000)))));

                overlay.Enabled = true;
                window.UpdateLayout();

                var bitmap = new RenderTargetBitmap(700, 500, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(host);

                return (overlay.LastMarkCount, overlay.LastPaintWasFull);
            }
            finally
            {
                window.Close();
            }
        });

        Assert.False(full, "60,000 characters should have run out of budget");
        Assert.True(marks > 0, "the marks inside the budget should still be drawn");
    }
}
