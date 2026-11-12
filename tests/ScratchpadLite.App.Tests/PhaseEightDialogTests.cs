using ScratchpadLite.App.Services;
using ScratchpadLite.App.UI.Dialogs;
using ScratchpadLite.Core.Model;
using ScratchpadLite.Core.Platform;
using Xunit;

namespace ScratchpadLite.App.Tests;

/// <summary>
/// The three dialogs Phase 8 builds: Preferences (Section 6.2), Page Setup
/// (Section 6.17) and Recover Documents (Section 6.13.1).
/// </summary>
/// <remarks>
/// Each one is tested through the method the OK or Restore button calls rather
/// than by clicking. Setting <c>DialogResult</c> on a window that was not shown
/// with <c>ShowDialog</c> throws, so a test that pressed the button would be
/// testing WPF's rules about dialogs instead of what the dialog reads and
/// decides.
/// </remarks>
[Collection(nameof(AppHost))]
public class PhaseEightDialogTests
{
    private static readonly IReadOnlyList<string> SomeLanguages = ["en-US", "en-GB", "de-DE"];

    // ------------------------------------------------- Preferences: 6.2 ----

    [Fact]
    public void PreferencesGivesBackWhatItWasGiven() =>
        AppHost.Run(() =>
        {
            var settings = new Settings
            {
                Theme = ThemeChoice.Dark,
                DarkPaper = true,
                PageView = PageViewMode.FullWidth,
                ShowStatusBar = false,
                ShowToolbarRow1 = false,
                ShowToolbarRow2 = true,
                DefaultFontFamily = "Georgia",
                DefaultFontSizePt = 13,
                DefaultParagraphStyle = "Heading 1",
                PreviewFontNamesInOwnFont = false,
                SpellCheckEnabled = false,
                SpellCheckLanguage = "en-GB",
                SpellCheckMaxCharacters = 500_000,
                RecoveryIntervalSeconds = 90,
                AutoSaveEnabled = true,
                AutoSaveIntervalSeconds = 300,
                AutoSaveOnFocusLoss = false,
                KeepBakFile = false,
                RestoreLastSession = false,
                RecentFilesCount = 7,
                SingleInstance = false,
                TxtWriteBom = true,
                MaxOpenFileBytes = 64L * 1024 * 1024,
            };

            var dialog = new Preferences();

            dialog.Show(settings, StyleSheet.Default, SomeLanguages);

            Assert.True(dialog.Commit());

            // Opening Preferences and pressing OK must change nothing. Every
            // setting that failed to make the round trip would be silently
            // reset the first time somebody looked at the dialog.
            Assert.Equal(settings, dialog.Result);
        });

    [Fact]
    public void PreferencesChangesTheSettingTheUserChanged() =>
        AppHost.Run(() =>
        {
            var dialog = new Preferences();

            dialog.Show(new Settings(), StyleSheet.Default, SomeLanguages);

            Assert.False(dialog.Result.AutoSaveEnabled);

            dialog.AutoSaveBox.IsChecked = true;

            Assert.True(dialog.Commit());
            Assert.True(dialog.Result.AutoSaveEnabled);
        });

    [Fact]
    public void AnIntervalBelowTheFloorIsRaisedToIt() =>
        AppHost.Run(() =>
        {
            var dialog = new Preferences();

            dialog.Show(new Settings(), StyleSheet.Default, SomeLanguages);

            dialog.RecoveryIntervalBox.Text = "2";
            dialog.AutoSaveIntervalBox.Text = "0";

            Assert.True(dialog.Commit());

            Assert.Equal(Recovery.MinIntervalSeconds, dialog.Result.RecoveryIntervalSeconds);
            Assert.Equal(AutoSave.MinIntervalSeconds, dialog.Result.AutoSaveIntervalSeconds);
        });

    [Fact]
    public void AnIntervalAboveTheCeilingIsLoweredToIt() =>
        AppHost.Run(() =>
        {
            var dialog = new Preferences();

            dialog.Show(new Settings(), StyleSheet.Default, SomeLanguages);

            dialog.RecoveryIntervalBox.Text = "999999";

            Assert.True(dialog.Commit());
            Assert.Equal(Recovery.MaxIntervalSeconds, dialog.Result.RecoveryIntervalSeconds);
        });

    [Fact]
    public void AnIntervalThatIsNotANumberIsRefused() =>
        AppHost.Run(() =>
        {
            var dialog = new Preferences();

            dialog.Show(new Settings(), StyleSheet.Default, SomeLanguages);

            dialog.RecoveryIntervalBox.Text = "soon";

            // Refused rather than clamped: there is no obvious number this
            // means, so guessing one would be worse than asking again.
            Assert.False(dialog.Commit());
        });

    [Fact]
    public void AnEmptyFontNameIsRefused() =>
        AppHost.Run(() =>
        {
            var dialog = new Preferences();

            dialog.Show(new Settings(), StyleSheet.Default, SomeLanguages);

            dialog.FontBox.Text = "   ";

            Assert.False(dialog.Commit());
        });

    [Fact]
    public void ALanguageThisPcDoesNotHaveIsKeptRatherThanDropped() =>
        AppHost.Run(() =>
        {
            // settings.json may have come from another PC, or from a USB stick.
            // Opening Preferences must not quietly change the language.
            var settings = new Settings { SpellCheckLanguage = "is-IS" };

            var dialog = new Preferences();

            dialog.Show(settings, StyleSheet.Default, SomeLanguages);

            Assert.True(dialog.Commit());
            Assert.Equal("is-IS", dialog.Result.SpellCheckLanguage);
        });

    [Fact]
    public void AParagraphStyleThatNoLongerExistsFallsBackToNormal() =>
        AppHost.Run(() =>
        {
            // styles.json is editable, so a style named in settings may be gone.
            var settings = new Settings { DefaultParagraphStyle = "Shouting" };

            var dialog = new Preferences();

            dialog.Show(settings, StyleSheet.Default, SomeLanguages);

            Assert.True(dialog.Commit());
            Assert.Equal(StyleSheet.NormalStyleName, dialog.Result.DefaultParagraphStyle);
        });

    [Fact]
    public void TheSpellCheckLimitStaysOffByDefault() =>
        AppHost.Run(() =>
        {
            var dialog = new Preferences();

            dialog.Show(new Settings(), StyleSheet.Default, SomeLanguages);

            Assert.True(dialog.Commit());

            // Phase 7 measured it: spell check costs nothing per keystroke, so
            // the switch exists and is off.
            Assert.Equal(0, dialog.Result.SpellCheckMaxCharacters);
        });

    // -------------------------------------------------- Page Setup: 6.17 ---

    [Fact]
    public void PageSetupGivesBackWhatItWasGiven() =>
        AppHost.Run(() =>
        {
            var page = new PageSetup("A4", Landscape: true, 36, 54, 72, 18);

            var dialog = new PageSetupDialog();

            dialog.Show(page);

            Assert.True(dialog.Commit());
            Assert.Equal(page, dialog.Result);
        });

    [Fact]
    public void PageSetupShowsInchesAndStoresPoints() =>
        AppHost.Run(() =>
        {
            var dialog = new PageSetupDialog();

            dialog.Show(new PageSetup(MarginTopPt: 72));

            Assert.Equal("1", dialog.TopBox.Text);

            dialog.TopBox.Text = "0.5";

            Assert.True(dialog.Commit());
            Assert.Equal(36, dialog.Result.MarginTopPt, 1);
        });

    [Fact]
    public void PageSetupRefusesAMarginThatIsNotANumber() =>
        AppHost.Run(() =>
        {
            var dialog = new PageSetupDialog();

            dialog.Show(new PageSetup());
            dialog.LeftBox.Text = "wide";

            Assert.False(dialog.Commit());
        });

    [Fact]
    public void PageSetupRefusesAMarginWiderThanThePage() =>
        AppHost.Run(() =>
        {
            var dialog = new PageSetupDialog();

            dialog.Show(new PageSetup());
            dialog.LeftBox.Text = "40";

            Assert.False(dialog.Commit());
        });

    [Fact]
    public void PageSetupKeepsOrientation() =>
        AppHost.Run(() =>
        {
            var dialog = new PageSetupDialog();

            dialog.Show(new PageSetup());

            Assert.True(dialog.PortraitButton.IsChecked);

            dialog.LandscapeButton.IsChecked = true;

            Assert.True(dialog.Commit());
            Assert.True(dialog.Result.Landscape);
        });

    [Fact]
    public void APaperNameFromSomeoneElsesFileDoesNotBreakTheDialog() =>
        AppHost.Run(() =>
        {
            var dialog = new PageSetupDialog();

            dialog.Show(new PageSetup("Executive"));

            Assert.True(dialog.Commit());

            // Shown as Letter, because that is the only honest thing a list of
            // four papers can do with a fifth.
            Assert.Equal("Letter", dialog.Result.Paper);
        });

    // ----------------------------------------- Recover Documents: 6.13.1 ---

    private static Recovery.Leftover Leftover(string title, string? path, int minutesAgo) =>
        new(
            $@"C:\data\recovery\abc\{title}.json",
            @"C:\data\recovery\abc",
            title,
            path,
            "DOCX",
            DateTime.UtcNow.AddMinutes(-minutesAgo));

    [Fact]
    public void TheRecoveryDialogListsEverythingItWasGiven() =>
        AppHost.Run(() =>
        {
            var dialog = new RecoveryDialog();

            dialog.Describe([
                Leftover("notes.docx", @"C:\work\notes.docx", 3),
                Leftover("Untitled 2", null, 9),
            ]);

            Assert.Equal(2, dialog.All.Count);
            Assert.Equal(2, dialog.Items.Items.Count);
        });

    [Fact]
    public void EverythingStartsTickedSoRestoreMeansRestoreEverything() =>
        AppHost.Run(() =>
        {
            var dialog = new RecoveryDialog();

            dialog.Describe([
                Leftover("one.docx", @"C:\work\one.docx", 1),
                Leftover("two.docx", @"C:\work\two.docx", 2),
            ]);

            Assert.True(dialog.ChooseRestore());
            Assert.Equal(RecoveryDialog.Answer.Restore, dialog.Result);
            Assert.Equal(2, dialog.Chosen.Count);
        });

    [Fact]
    public void UntickingOneLeavesItBehind() =>
        AppHost.Run(() =>
        {
            var dialog = new RecoveryDialog();

            dialog.Describe([
                Leftover("wanted.docx", @"C:\work\wanted.docx", 1),
                Leftover("not-wanted.docx", @"C:\work\not-wanted.docx", 2),
            ]);

            ((System.Windows.Controls.CheckBox)dialog.Items.Items[1]!).IsChecked = false;

            Assert.True(dialog.ChooseRestore());

            var chosen = Assert.Single(dialog.Chosen);

            Assert.Equal("wanted.docx", chosen.Title);
        });

    [Fact]
    public void RestoreAllIgnoresTheTicks() =>
        AppHost.Run(() =>
        {
            var dialog = new RecoveryDialog();

            dialog.Describe([
                Leftover("one.docx", @"C:\work\one.docx", 1),
                Leftover("two.docx", @"C:\work\two.docx", 2),
            ]);

            ((System.Windows.Controls.CheckBox)dialog.Items.Items[0]!).IsChecked = false;
            ((System.Windows.Controls.CheckBox)dialog.Items.Items[1]!).IsChecked = false;

            Assert.True(dialog.ChooseRestore(everything: true));
            Assert.Equal(2, dialog.Chosen.Count);
        });

    [Fact]
    public void RestoreWithNothingTickedIsTreatedAsLater() =>
        AppHost.Run(() =>
        {
            var dialog = new RecoveryDialog();

            dialog.Describe([Leftover("one.docx", @"C:\work\one.docx", 1)]);

            ((System.Windows.Controls.CheckBox)dialog.Items.Items[0]!).IsChecked = false;

            // Pressing Restore having ticked nothing is a mistake, not an
            // instruction to throw the work away.
            Assert.False(dialog.ChooseRestore());
            Assert.Equal(RecoveryDialog.Answer.Later, dialog.Result);
            Assert.Empty(dialog.Chosen);
        });

    [Fact]
    public void ClosingTheDialogKeepsEverything() =>
        AppHost.Run(() =>
        {
            var dialog = new RecoveryDialog();

            dialog.Describe([Leftover("one.docx", @"C:\work\one.docx", 1)]);

            // Later is the default answer, which is what Escape gives. A dialog
            // whose Escape key threw away unsaved work would eventually throw
            // away somebody's.
            Assert.Equal(RecoveryDialog.Answer.Later, dialog.Result);
        });

    [Fact]
    public void AnUntitledDocumentSaysSoRatherThanShowingABlankPath() =>
        AppHost.Run(() =>
        {
            var dialog = new RecoveryDialog();

            dialog.Describe([Leftover("Untitled 3", null, 4)]);

            var row = (System.Windows.Controls.CheckBox)dialog.Items.Items[0]!;

            Assert.Equal("This document had never been saved.", row.ToolTip);
        });
}
