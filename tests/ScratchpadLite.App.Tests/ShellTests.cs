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
/// </list>
///
/// Phase 8
/// <list type="bullet">
///   <item>RecoveryTests: writes recovery files, detects an abandoned lock, restores.</item>
///   <item>AutoSaveTests: off by default; when on, saves a modified titled .docx after the idle pause; skips untitled, lossy-format, read-only and changed-on-disk files; never clears undo; writes the .bak only on the first save of the session.</item>
/// </list>
/// </remarks>
internal static class TestPlan;
