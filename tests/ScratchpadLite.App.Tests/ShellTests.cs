using ScratchpadLite.App.Services;
using Xunit;

namespace ScratchpadLite.App.Tests;

/// <summary>
/// Phase 0 WPF-side tests. WPF types need an STA thread, which is what
/// <c>[StaFact]</c> from Xunit.StaFact provides (Section 10.1).
/// </summary>
public class StartupTimerTests
{
    [Fact]
    public void FirstRender_RecordsATimeOnce()
    {
        StartupTimer.MarkFirstRender();
        var first = StartupTimer.ElapsedMs;

        StartupTimer.MarkFirstRender();

        Assert.NotNull(first);
        Assert.True(first > 0, "the elapsed time should be positive");
        Assert.Equal(first, StartupTimer.ElapsedMs);
    }
}

/// <summary>
/// The test map for the WPF side (Section 10.1), filled in per phase.
/// </summary>
/// <remarks>
/// Phase 1
/// <list type="bullet">
///   <item>CommandsTests: no two commands share a key gesture (Section 8.3).</item>
///   <item>FormatCommandsTests: each command on a selection, on an empty selection, and across mixed formatting; one undo reverses each one.</item>
/// </list>
///
/// Phase 2
/// <list type="bullet">
///   <item>FontRegistryTests: loads fixture fonts from a temp font folder through --fonts; reload adds new ones; a substitution returns the substitute and keeps the original name on the run.</item>
/// </list>
///
/// Phase 4 - the most important test in the project (Section 6.4)
/// <list type="bullet">
///   <item>BridgeRoundTripTests: Walk(Build(ir)) equals ir, after normalization, for a fixture set covering every IR feature.</item>
///   <item>FormatRegistryTests: building the Open dialog filter loads neither the Open XML SDK nor Markdig, checked through AppDomain.CurrentDomain.GetAssemblies().</item>
/// </list>
///
/// Phase 5
/// <list type="bullet">
///   <item>RtfTests: load the WordPad, Word and LibreOffice fixtures, walk them, and check images, tables, lists and colors survive.</item>
/// </list>
///
/// Phase 7
/// <list type="bullet">
///   <item>FindEngineTests: match case, whole word, a match that crosses runs, and Replace All as one undo step.</item>
///   <item>TabTests: undo survives a tab switch (the one Section 6.12 names), lazy editor creation, closing, reordering, cycling, and the tab strip.</item>
///   <item>SpellCheckTests: the custom dictionary file, the installed languages, Code paragraphs excluded, and the size limit.</item>
/// </list>
///
/// Phase 8
/// <list type="bullet">
///   <item>RecoveryTests: writes recovery files, detects an abandoned lock - against a real process that is killed - and reads them back; tidies away a folder with nothing in it.</item>
///   <item>AutoSaveTests: off by default; when on, saves a modified titled .docx after the idle pause; skips untitled, lossy-format, read-only and changed-on-disk files; writes the .bak only on the first save of the session.</item>
///   <item>PrintingTests: the gate. Both print and PDF come out in one column, checked by looking at where the text actually landed on a laid-out page.</item>
///   <item>SingleInstanceTests: the per-installation and per-user names, the claim, and a real hand-off over the pipe.</item>
///   <item>PhaseEightDialogTests: Preferences, Page Setup and Recover Documents read and write what they say they do.</item>
///   <item>MainWindowTests: the six menu items Phase 8 switched on are no longer greyed out, and the AutoSave switch is on the status bar saying whether it is on.</item>
/// </list>
///
/// Phase 9 - the release
/// <list type="bullet">
///   <item>ParagraphDialogTests: what the selected paragraphs have in common and what they disagree on; a blank box leaving a setting alone; inches in and points out; a refused word and a refused number; and the whole dialog as one undo step, which is what ApplyParagraph exists for.</item>
///   <item>SpecialCharacterTests: every range small enough to lay out without virtualizing; only characters the font has a glyph for, checked against the font's own map rather than against a list written here; the recently used list; and the dialog handing the character to the window rather than inserting it.</item>
///   <item>FormattingMarksTests: these render. Off by default and nothing drawn; a pilcrow per paragraph and a dot per space counted from a real paint; the tab and the hard space; the cost proportional to the window rather than to the document; a paragraph taller than the window still marked; and the budget, which is only reached with no viewport to limit the work.</item>
///   <item>VariableFontTests: what WPF really does with a variable font, which Section 13's fifth limitation asked to have measured. Rendered and compared pixel by pixel, because the advance widths give the wrong answer.</item>
///   <item>ReleaseAuditTests: the manifest and project settings the release depends on - Per-Monitor V2 in both spellings, longPathAware, UTF-8, asInvoker, the Windows 10 and 11 GUID, the seven publish settings from Section 11.1 - plus one version in one place, every shipped document present, and USER_GUIDE.md still stating each of Section 13's limitations.</item>
/// </list>
/// </remarks>
internal static class TestPlan;
