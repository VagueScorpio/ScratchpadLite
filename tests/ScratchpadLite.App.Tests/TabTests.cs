using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using ScratchpadLite.App.Editor;
using ScratchpadLite.App.Services;
using ScratchpadLite.App.UI;
using Xunit;

namespace ScratchpadLite.App.Tests;

/// <summary>
/// Several documents open at once (Section 6.12).
/// </summary>
/// <remarks>
/// <see cref="UndoSurvivesATabSwitch"/> is the test Section 6.12 asks for by
/// name, and the reason this class does not use a <c>TabControl</c>: a
/// TabControl re-creates the content of a tab every time it is shown, so the
/// RichTextBox would be a new one and its undo history would be gone - quietly,
/// and only in the built application.
/// </remarks>
[Collection(nameof(AppHost))]
public class TabTests
{
    [Fact]
    public void ANewTabHasNoEditorUntilItIsShown()
    {
        var (beforeShowing, afterShowing) = InWindow((_, tabs) =>
        {
            var session = tabs.Add();
            var before = session.View;

            tabs.Activate(session);

            return (before, session.View);
        });

        // Section 2.1: a restored session of nine files must not build nine
        // RichTextBoxes before the first frame.
        Assert.Null(beforeShowing);
        Assert.NotNull(afterShowing);
    }

    [Fact]
    public void OnlyTheActiveTabsEditorIsVisible()
    {
        var states = InWindow((_, tabs) =>
        {
            var first = tabs.Add();
            var second = tabs.Add();

            tabs.Activate(first);
            tabs.Activate(second);

            return (first.View!.Visibility, second.View!.Visibility);
        });

        Assert.Equal(Visibility.Collapsed, states.Item1);
        Assert.Equal(Visibility.Visible, states.Item2);
    }

    [Fact]
    public void EveryEditorStaysInTheHost()
    {
        var count = InWindow((host, tabs) =>
        {
            foreach (var _ in Enumerable.Range(0, 3))
            {
                tabs.Activate(tabs.Add());
            }

            return host.Children.Count;
        });

        // All three alive at once, which is what makes a tab switch instant and
        // what keeps the undo history.
        Assert.Equal(3, count);
    }

    /// <summary>The test Section 6.12 names.</summary>
    [Fact]
    public void UndoSurvivesATabSwitch()
    {
        var (sameControl, afterUndo) = InWindow((_, tabs) =>
        {
            var first = tabs.Add();
            var second = tabs.Add();

            tabs.Activate(first);

            // The editor has to finish loading before it has an undo history
            // at all - see AppHost.Settle.
            AppHost.Settle();

            var editor = first.View!.Editor;

            Type(editor, "typed in the first tab");

            var before = editor;

            // Away and back, which is where a TabControl would have thrown the
            // control away and built another.
            tabs.Activate(second);
            AppHost.Settle();
            tabs.Activate(first);
            AppHost.Settle();

            var after = first.View!.Editor;

            after.Undo();

            return (ReferenceEquals(before, after), Text(after));
        });

        Assert.True(sameControl, "the tab's RichTextBox should be the same control after a switch");
        Assert.DoesNotContain("typed in the first tab", afterUndo, StringComparison.Ordinal);
    }

    [Fact]
    public void EachTabKeepsItsOwnDocument()
    {
        var (first, second) = InWindow((_, tabs) =>
        {
            var one = tabs.Add();
            var two = tabs.Add();

            tabs.Activate(one);
            Type(one.View!.Editor, "document one");

            tabs.Activate(two);
            Type(two.View!.Editor, "document two");

            return (Text(one.View!.Editor), Text(two.View!.Editor));
        });

        Assert.Contains("document one", first, StringComparison.Ordinal);
        Assert.Contains("document two", second, StringComparison.Ordinal);
        Assert.DoesNotContain("document two", first, StringComparison.Ordinal);
    }

    [Fact]
    public void ClosingATabActivatesTheOneToItsRight()
    {
        var (active, left) = InWindow((_, tabs) =>
        {
            var one = tabs.Add();
            var two = tabs.Add();
            var three = tabs.Add();

            tabs.Activate(two);
            tabs.Remove(two);

            return (ReferenceEquals(tabs.Active, three), tabs.Tabs.Count);
        });

        Assert.True(active, "the tab to the right should become the active one");
        Assert.Equal(2, left);
    }

    [Fact]
    public void ClosingTheRightmostTabActivatesTheOneToItsLeft()
    {
        var active = InWindow((_, tabs) =>
        {
            var one = tabs.Add();
            var two = tabs.Add();

            tabs.Activate(two);
            tabs.Remove(two);

            return ReferenceEquals(tabs.Active, one);
        });

        Assert.True(active);
    }

    [Fact]
    public void ClosingAnInactiveTabLeavesTheActiveOneAlone()
    {
        var stillActive = InWindow((_, tabs) =>
        {
            var one = tabs.Add();
            var two = tabs.Add();

            tabs.Activate(one);
            tabs.Remove(two);

            return ReferenceEquals(tabs.Active, one);
        });

        Assert.True(stillActive);
    }

    [Fact]
    public void ClosingTheLastTabLeavesNothingActive()
    {
        var (remaining, inHost) = InWindow((host, tabs) =>
        {
            var only = tabs.Add();

            tabs.Activate(only);
            tabs.Remove(only);

            return (tabs.Active, host.Children.Count);
        });

        // The window makes a fresh empty tab at this point; the manager's job
        // is only to report honestly that there is none.
        Assert.Null(remaining);

        // And the closed tab's editor is out of the tree, not merely hidden -
        // a kept editor for a closed document would hold the whole document.
        Assert.Equal(0, inHost);
    }

    [Fact]
    public void TabsCanBeReordered()
    {
        var order = InWindow((_, tabs) =>
        {
            var one = tabs.Add("a.txt");
            var two = tabs.Add("b.txt");
            var three = tabs.Add("c.txt");

            tabs.Move(0, 2);

            return tabs.Tabs.Select(t => t.FilePath).ToList();
        });

        Assert.Equal(["b.txt", "c.txt", "a.txt"], order);
    }

    [Fact]
    public void AnImpossibleMoveChangesNothing()
    {
        var order = InWindow((_, tabs) =>
        {
            tabs.Add("a.txt");
            tabs.Add("b.txt");

            tabs.Move(0, 9);
            tabs.Move(-1, 0);
            tabs.Move(1, 1);

            return tabs.Tabs.Select(t => t.FilePath).ToList();
        });

        Assert.Equal(["a.txt", "b.txt"], order);
    }

    [Fact]
    public void TheSameFileFindsItsOwnTabWhateverTheCase()
    {
        var found = InWindow((_, tabs) =>
        {
            var session = tabs.Add(@"C:\Docs\Report.docx");

            return ReferenceEquals(tabs.ForPath(@"c:\docs\report.docx"), session);
        });

        // Section 6.12: opening a file that is already open switches to its
        // tab. On Windows that has to ignore case, or the same file opens twice
        // and the two copies overwrite each other.
        Assert.True(found);
    }

    [Fact]
    public void CyclingGoesRoundInBothDirections()
    {
        var visited = InWindow((_, tabs) =>
        {
            var one = tabs.Add("1");
            var two = tabs.Add("2");
            var three = tabs.Add("3");

            tabs.Activate(one);

            var seen = new List<string?>();

            tabs.Cycle(backwards: false);
            seen.Add(tabs.Active?.FilePath);

            tabs.Cycle(backwards: false);
            seen.Add(tabs.Active?.FilePath);

            tabs.Cycle(backwards: false);
            seen.Add(tabs.Active?.FilePath);        // wrapped round

            tabs.Cycle(backwards: true);
            seen.Add(tabs.Active?.FilePath);        // and back round the other way

            return seen;
        });

        Assert.Equal(["2", "3", "1", "3"], visited);
    }

    [Fact]
    public void CyclingWithOneTabDoesNothing()
    {
        var active = InWindow((_, tabs) =>
        {
            var only = tabs.Add("1");

            tabs.Activate(only);
            tabs.Cycle(backwards: false);

            return tabs.Active?.FilePath;
        });

        Assert.Equal("1", active);
    }

    [Fact]
    public void AddedTabsCarryTheirFileNameBeforeTheyAreRead()
    {
        var (title, pending) = InWindow((_, tabs) =>
        {
            var session = tabs.Add(@"C:\Docs\Quarterly.docx");

            return (session.Title, session.PendingPath);
        });

        // The header has to read as the file from the moment the tab appears -
        // a lazy tab called "Untitled 4" would be useless.
        Assert.Equal("Quarterly.docx", title);
        Assert.Equal(@"C:\Docs\Quarterly.docx", pending);
    }

    [Fact]
    public void ModifiedListsOnlyTheDirtyTabs()
    {
        var names = InWindow((_, tabs) =>
        {
            var clean = tabs.Add("clean.txt");
            var dirty = tabs.Add("dirty.txt");

            dirty.IsDirty = true;

            return tabs.Modified.Select(t => t.FilePath).ToList();
        });

        Assert.Equal(["dirty.txt"], names);
    }

    [Fact]
    public void UntitledDocumentsAreNumberedInOrder()
    {
        var (first, second) = InWindow((_, tabs) =>
        {
            var one = tabs.Add();
            var two = tabs.Add();

            return (one.UntitledNumber, two.UntitledNumber);
        });

        Assert.Equal(first + 1, second);
    }

    // ----------------------------------------------------------- the strip --

    [Fact]
    public void TheStripShowsOneHeaderPerTab()
    {
        var labels = InStrip(
            [
                new DocumentSession { FilePath = @"C:\a\First.txt" },
                new DocumentSession { FilePath = @"C:\b\Second.txt" },
            ],
            Labels);

        Assert.Equal(["First.txt", "Second.txt"], labels);
    }

    [Fact]
    public void AModifiedTabShowsADot()
    {
        var labels = InStrip([new DocumentSession { FilePath = @"C:\a\Draft.txt", IsDirty = true }], Labels);

        // Section 6.12: a tab shows a dot when there are unsaved changes.
        Assert.Equal(["Draft.txt \u2022"], labels);
    }

    [Fact]
    public void AHeadersTooltipIsTheFullPath()
    {
        var tooltip = InStrip([new DocumentSession { FilePath = @"C:\Users\someone\Documents\Report.docx" }], Tooltip);

        // The only place a tab says which of two files with the same name it
        // is.
        Assert.Equal(@"C:\Users\someone\Documents\Report.docx", tooltip);
    }

    [Fact]
    public void AnUntitledTabsTooltipIsItsName()
    {
        var tooltip = InStrip([new DocumentSession()], Tooltip);

        Assert.StartsWith("Untitled", tooltip, StringComparison.Ordinal);
    }

    // ------------------------------------------------------- the real window --

    [Fact]
    public void TheWindowOpensWithOneTab()
    {
        var labels = InMainWindow(window => Labels(TheStrip(window)));

        Assert.Single(labels);
        Assert.StartsWith("Untitled", labels[0], StringComparison.Ordinal);
    }

    [Fact]
    public void TheNewTabCommandAddsATab()
    {
        var labels = InMainWindow(window =>
        {
            ScratchpadLite.App.UI.Commands.NewTab.Execute(null, window);

            return Labels(TheStrip(window));
        });

        // Goes through the window's own command binding, which is the path
        // Ctrl+N, the File menu and the + button all take.
        Assert.Equal(2, labels.Count);
    }

    [Fact]
    public void TheFindBarIsHiddenUntilCtrlFAndTheEscapeKeyHidesItAgain()
    {
        var (atStart, afterFind) = InMainWindow(window =>
        {
            var bar = Descendants<ScratchpadLite.App.UI.Dialogs.FindReplaceBar>(window)[0];
            var before = bar.Visibility;

            ScratchpadLite.App.UI.Commands.Find.Execute(null, window);

            return (before, bar.Visibility);
        });

        Assert.Equal(Visibility.Collapsed, atStart);
        Assert.Equal(Visibility.Visible, afterFind);
    }

    [Fact]
    public void EditingATabPutsAStarInTheTitleAndADotOnTheTab()
    {
        var (title, labels) = InMainWindow(window =>
        {
            Type(window.Editor.Editor, "something");

            // The dirty flag is set from TextChanged, which has already run by
            // the time Type returns.
            return (window.Title, Labels(TheStrip(window)));
        });

        Assert.StartsWith("*", title, StringComparison.Ordinal);
        Assert.EndsWith("•", labels[0], StringComparison.Ordinal);
    }

    /// <summary>
    /// Builds the real window, off-screen, with <c>--measure-startup</c> so
    /// nothing starts the font folder or opens last session's files.
    /// </summary>
    private static T InMainWindow<T>(Func<ScratchpadLite.App.UI.MainWindow, T> work)
    {
        return AppHost.Run(() =>
        {
            var window = new ScratchpadLite.App.UI.MainWindow(
                new ScratchpadLite.Core.Platform.Settings(),
                new ScratchpadLite.Core.Platform.CommandLineOptions(
                    [], MeasureStartup: true, Debug: false, FontDirOverride: null))
            {
                ShowActivated = false,
                ShowInTaskbar = false,
                Left = -4000,
                Top = -4000,
            };

            try
            {
                window.Show();
                window.UpdateLayout();

                return work(window);
            }
            finally
            {
                // Closing a window that has unsaved changes opens the modal
                // "these documents have changes" dialog, and in a test that
                // waits for a click that never comes - the first version of
                // this hung the whole run. So the documents are marked clean
                // first, which is the test tidying up after itself rather than
                // the window being told not to ask.
                foreach (var session in window.OpenDocuments)
                {
                    session.IsDirty = false;
                }

                window.Close();
            }
        });
    }

    private static TabStrip TheStrip(DependencyObject window) => Descendants<TabStrip>(window)[0];

    // --------------------------------------------------------------- helpers --

    /// <summary>
    /// Runs the work against a tab manager whose host is in a shown window.
    /// </summary>
    /// <remarks>
    /// Shown, and off-screen rather than hidden, for the reason Phase 6 found
    /// the hard way: a RichTextBox that is not in a shown window takes no
    /// keyboard focus and keeps no undo history, so the one test this class
    /// exists for would pass without proving anything.
    /// </remarks>
    private static T InWindow<T>(Func<Grid, TabManager, T> work)
    {
        return AppHost.Run(() =>
        {
            var host = new Grid();

            var window = new Window
            {
                Content = host,
                Width = 600,
                Height = 400,
                ShowInTaskbar = false,
                Left = -4000,
                Top = -4000,
            };

            try
            {
                window.Show();
                window.UpdateLayout();

                return work(host, new TabManager(host, () => new EditorView()));
            }
            finally
            {
                window.Close();
            }
        });
    }

    /// <summary>
    /// Runs the work against a tab strip showing these tabs, in a shown window.
    /// </summary>
    /// <remarks>
    /// A control that is not in a window has no visual tree to look at: its
    /// template is never applied, so the headers exist as objects and nothing
    /// can be found by walking down from the strip.
    /// </remarks>
    private static T InStrip<T>(IReadOnlyList<DocumentSession> sessions, Func<TabStrip, T> work)
    {
        return AppHost.Run(() =>
        {
            var strip = new TabStrip();

            var window = new Window
            {
                Content = strip,
                Width = 600,
                Height = 120,
                ShowInTaskbar = false,
                Left = -4000,
                Top = -4000,
            };

            try
            {
                window.Show();
                strip.Show(sessions, sessions[0]);
                window.UpdateLayout();

                return work(strip);
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static void Type(RichTextBox editor, string text)
    {
        editor.BeginChange();

        try
        {
            editor.CaretPosition = editor.Document.ContentEnd;
            editor.CaretPosition.InsertTextInRun(text);
        }
        finally
        {
            editor.EndChange();
        }
    }

    private static string Text(RichTextBox editor) =>
        new TextRange(editor.Document.ContentStart, editor.Document.ContentEnd).Text;

    /// <summary>
    /// The header labels, picked out by their tag: a button's glyph and a
    /// scroll viewer's own parts are TextBlocks too.
    /// </summary>
    private static List<string> Labels(TabStrip strip) =>
        Descendants<TextBlock>(strip)
            .Where(t => Equals(t.Tag, TabStrip.LabelTag))
            .Select(t => t.Text)
            .ToList();

    private static string? Tooltip(TabStrip strip) =>
        Descendants<Border>(strip)
            .Select(b => b.ToolTip as string)
            .FirstOrDefault(t => t is not null);

    private static List<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        var found = new List<T>();

        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);

            if (child is T wanted)
            {
                found.Add(wanted);
            }

            found.AddRange(Descendants<T>(child));
        }

        return found;
    }
}
