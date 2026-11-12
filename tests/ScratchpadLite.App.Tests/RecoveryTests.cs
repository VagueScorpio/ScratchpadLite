using System.Diagnostics;
using System.IO;
using ScratchpadLite.App.Services;
using ScratchpadLite.Core.Model;
using Xunit;

namespace ScratchpadLite.App.Tests;

/// <summary>
/// Crash recovery (Section 6.13.1): the lock that tells a live run from a dead
/// one, the files it writes, and what a later launch finds.
/// </summary>
/// <remarks>
/// Every test here uses a temporary recovery root rather than the real
/// <c>data/recovery</c>, so running the suite cannot offer to recover
/// somebody's actual work - or quietly delete it.
/// </remarks>
public class RecoveryTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("sp-recovery-");
    private readonly List<Recovery> _started = [];

    public void Dispose()
    {
        foreach (var recovery in _started)
        {
            recovery.ClearOnCleanExit();
        }

        try
        {
            _root.Delete(recursive: true);
        }
        catch (IOException)
        {
        }

        GC.SuppressFinalize(this);
    }

    private Recovery New(string sessionId)
    {
        var recovery = new Recovery(_root.FullName, sessionId);

        _started.Add(recovery);

        return recovery;
    }

    /// <summary>
    /// A document with a path, or an untitled one when the path is null. The
    /// title follows from the path, which is what <c>DocumentSession</c> does.
    /// </summary>
    private static DocumentSession Doc(string? path) =>
        new() { FilePath = path, FormatName = "DOCX" };

    /// <summary>
    /// A folder set up the way a crash leaves one: the files are there and the
    /// lock is there, but no living process holds it.
    /// </summary>
    private Recovery Crashed(string name)
    {
        var folder = Path.Combine(_root.FullName, name);

        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, Recovery.LockFileName), "999");

        return new Recovery(_root.FullName, name);
    }

    private static SpDocument Document(string text) =>
        new(
            [new Paragraph { Inlines = [new TextRun(text, new CharStyle())] }],
            new Dictionary<string, string> { ["title"] = "A test" },
            new PageSetup("A4", Landscape: true),
            new HashSet<string> { "Calibri" });

    // ------------------------------------------------------------- the lock --

    [Fact]
    public void ClaimMakesTheFolderAndTheLock()
    {
        var recovery = New("one");

        Assert.True(recovery.Claim());
        Assert.True(Directory.Exists(recovery.SessionDir));
        Assert.True(File.Exists(Path.Combine(recovery.SessionDir, Recovery.LockFileName)));
    }

    /// <summary>
    /// The lock file cannot be opened at all while a run holds it.
    /// </summary>
    /// <remarks>
    /// Not even for reading, and not even by a process asking politely with
    /// <see cref="FileShare.ReadWrite"/>: <see cref="FileShare.None"/> on the
    /// holder's side refuses everyone. That is what makes the check in
    /// <c>IsAbandoned</c> a real answer rather than a guess, and it is also why
    /// the process id written inside is only readable once the run that wrote
    /// it is gone - which is the only time anybody wants to know.
    /// </remarks>
    [Fact]
    public void TheLockFileCannotBeOpenedAtAllWhileARunHoldsIt()
    {
        var recovery = New("pid");
        recovery.Claim();

        var lockFile = Path.Combine(recovery.SessionDir, Recovery.LockFileName);

        Assert.Throws<IOException>(() => new FileStream(
            lockFile,
            new FileStreamOptions
            {
                Mode = FileMode.Open,
                Access = FileAccess.Read,
                Share = FileShare.ReadWrite,
            }));
    }

    [Fact]
    public void AFolderWhoseLockIsHeldIsNotAbandoned()
    {
        var recovery = New("live");
        recovery.Claim();

        Assert.False(Recovery.IsAbandoned(recovery.SessionDir));
    }

    [Fact]
    public void AFolderWhoseLockWasReleasedIsAbandoned()
    {
        var recovery = New("gone");
        recovery.Claim();

        var folder = recovery.SessionDir;

        // Releasing the handle is what the operating system does when a process
        // ends, however it ends.
        recovery.ClearOnCleanExit();

        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, Recovery.LockFileName), "1");

        Assert.True(Recovery.IsAbandoned(folder));
    }

    [Fact]
    public void AFolderWithNoLockFileAtAllIsAbandoned()
    {
        // What a process killed in its first second leaves behind.
        var folder = Path.Combine(_root.FullName, "no-lock");
        Directory.CreateDirectory(folder);

        Assert.True(Recovery.IsAbandoned(folder));
    }

    /// <summary>
    /// The Section 6.13.1 mechanism against a real process that really dies.
    /// </summary>
    /// <remarks>
    /// Everything else here releases the handle by disposing it, which is the
    /// same call Windows makes but is not the same event. This one has another
    /// process take the lock, kills it without warning, and checks that the
    /// folder then reads as abandoned. It is the automated half of the
    /// blueprint's end-task-and-recover test; the other half - that the dialog
    /// appears on the next launch - is a hand test, because it needs a launch.
    /// </remarks>
    [Fact]
    public void AFolderLockedByAProcessThatIsKilledBecomesAbandoned()
    {
        var folder = Path.Combine(_root.FullName, "killed");
        Directory.CreateDirectory(folder);

        var lockFile = Path.Combine(folder, Recovery.LockFileName);

        // PowerShell, because the rule for this project is C# and PowerShell
        // only - and because it can hold a FileShare.None handle open for as
        // long as it is alive, which is exactly what is being simulated.
        var script =
            $"$s=[System.IO.File]::Open('{lockFile}',[System.IO.FileMode]::Create,"
            + "[System.IO.FileAccess]::Write,[System.IO.FileShare]::None); "
            + "Write-Output ready; Start-Sleep -Seconds 60";

        using var child = Process.Start(new ProcessStartInfo
        {
            FileName = "powershell.exe",
            ArgumentList = { "-NoProfile", "-NonInteractive", "-Command", script },
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        });

        Assert.NotNull(child);

        try
        {
            // "ready" on stdout means the handle is open, which is more
            // reliable than sleeping and hoping.
            Assert.Equal("ready", child.StandardOutput.ReadLine());

            Assert.False(
                Recovery.IsAbandoned(folder),
                "a folder whose lock another living process holds must not be offered for recovery");
        }
        finally
        {
            child.Kill(entireProcessTree: true);
            child.WaitForExit(10_000);
        }

        Assert.True(
            Recovery.IsAbandoned(folder),
            "once the process holding the lock is gone, its folder is somebody's unsaved work");

        // And the folder is still there. A crash cleans nothing up, which is
        // the whole reason this works.
        Assert.True(File.Exists(lockFile));
    }

    // ------------------------------------------------------------- writing --

    [Fact]
    public void WriteNowWritesAFileThatReadsBack()
    {
        var recovery = New("write");
        recovery.Claim();

        var session = Doc(@"C:\work\notes.docx");

        recovery.WriteNow(session, Document("hello recovery"));

        var file = Path.Combine(recovery.SessionDir, $"{session.Id:N}.json");

        Assert.True(File.Exists(file));

        var read = Recovery.Read(file);

        Assert.NotNull(read);
        Assert.Equal(@"C:\work\notes.docx", read.OriginalPath);
        Assert.Equal("notes.docx", read.Title);
        Assert.Equal("DOCX", read.FormatName);
    }

    [Fact]
    public void TheDocumentItselfSurvivesTheRoundTrip()
    {
        var recovery = New("round");
        recovery.Claim();

        var session = Doc(null);

        recovery.WriteNow(session, Document("the text that must come back"));

        var read = Recovery.Read(Path.Combine(recovery.SessionDir, $"{session.Id:N}.json"));

        Assert.NotNull(read);

        var paragraph = Assert.IsType<Paragraph>(read.Document.Blocks[0]);
        var run = Assert.IsType<TextRun>(paragraph.Inlines[0]);

        Assert.Equal("the text that must come back", run.Text);

        // The things the FlowDocument has nowhere to keep have to come back
        // too, or a restored document loses its paper size and its title.
        Assert.Equal("A4", read.Document.Page.Paper);
        Assert.True(read.Document.Page.Landscape);
        Assert.Equal("A test", read.Document.Meta["title"]);
        Assert.Contains("Calibri", read.Document.FontsUsed);
    }

    [Fact]
    public void AnUntitledDocumentIsIncluded()
    {
        var recovery = Crashed("untitled");

        recovery.WriteNow(Doc(null), Document("never saved anywhere"));

        var leftovers = Recovery.FindLeftovers(_root.FullName);

        var found = Assert.Single(leftovers);

        Assert.Null(found.OriginalPath);
        Assert.StartsWith("Untitled", found.Title);
    }

    [Fact]
    public void WritingSetsTheTimestampOnTheSession()
    {
        var recovery = New("stamp");
        recovery.Claim();

        var session = Doc(null);

        Assert.Equal(default, session.LastRecoveryWriteUtc);

        recovery.WriteNow(session, Document("x"));

        Assert.NotEqual(default, session.LastRecoveryWriteUtc);
    }

    [Fact]
    public async Task WriteAsyncWritesTheFileToo()
    {
        var recovery = New("async");
        recovery.Claim();

        var session = Doc(@"C:\work\async.docx");

        await recovery.WriteAsync(session, Document("written on a background task"));

        Assert.True(File.Exists(Path.Combine(recovery.SessionDir, $"{session.Id:N}.json")));
    }

    [Fact]
    public void ForgetDeletesOneDocumentsFile()
    {
        var recovery = New("forget");
        recovery.Claim();

        var kept = Doc(@"C:\work\kept.docx");
        var gone = Doc(@"C:\work\gone.docx");

        recovery.WriteNow(kept, Document("kept"));
        recovery.WriteNow(gone, Document("gone"));

        recovery.Forget(gone);

        Assert.True(File.Exists(Path.Combine(recovery.SessionDir, $"{kept.Id:N}.json")));
        Assert.False(File.Exists(Path.Combine(recovery.SessionDir, $"{gone.Id:N}.json")));
    }

    // ----------------------------------------------------- what was left --

    [Fact]
    public void OurOwnFolderIsNeverOfferedBack()
    {
        var recovery = New("mine");
        recovery.Claim();

        recovery.WriteNow(Doc(@"C:\work\mine.docx"), Document("still being typed in"));

        Assert.Empty(recovery.FindLeftovers());
    }

    [Fact]
    public void AnotherRunsFolderIsOffered()
    {
        Crashed("crashed").WriteNow(Doc(@"C:\work\lost.docx"), Document("unsaved work"));

        var live = New("live-now");
        live.Claim();
        live.WriteNow(Doc(@"C:\work\mine.docx"), Document("still open here"));

        var leftovers = live.FindLeftovers();

        // One, not two: this run's own folder is not somebody's lost work.
        Assert.Single(leftovers);
        Assert.Equal("lost.docx", leftovers[0].Title);
    }

    [Fact]
    public void LeftoversComeBackNewestFirst()
    {
        var writer = Crashed("several");

        var older = Doc(@"C:\work\older.docx");
        writer.WriteNow(older, Document("older"));

        var newer = Doc(@"C:\work\newer.docx");
        writer.WriteNow(newer, Document("newer"));

        var leftovers = Recovery.FindLeftovers(_root.FullName);

        Assert.Equal(2, leftovers.Count);
        Assert.True(
            leftovers[0].SavedUtc >= leftovers[1].SavedUtc,
            "the most recently saved document belongs at the top of the list");
    }

    [Fact]
    public void AnAbandonedFolderWithNothingInItIsTidiedAway()
    {
        // What a run that was killed having saved everything leaves: a lock
        // file and no documents. Nothing else would ever delete it, so without
        // tidying, every crash leaves one more folder for ever.
        var empty = Crashed("nothing-to-recover");

        var live = New("live");
        live.Claim();

        Assert.Equal(1, live.TidyEmptyAbandonedFolders());
        Assert.False(Directory.Exists(empty.SessionDir));
    }

    [Fact]
    public void TidyingNeverTouchesAFolderWithWorkInIt()
    {
        var hasWork = Crashed("has-work");
        hasWork.WriteNow(Doc(@"C:\work\precious.docx"), Document("do not delete this"));

        var live = New("live");
        live.Claim();

        Assert.Equal(0, live.TidyEmptyAbandonedFolders());
        Assert.True(Directory.Exists(hasWork.SessionDir));
        Assert.Single(live.FindLeftovers());
    }

    [Fact]
    public void TidyingNeverTouchesThisRunsOwnFolder()
    {
        var live = New("live");
        live.Claim();

        Assert.Equal(0, live.TidyEmptyAbandonedFolders());
        Assert.True(Directory.Exists(live.SessionDir));
    }

    [Fact]
    public void AFolderOfNothingButDamagedFilesIsTidiedAway()
    {
        // Unreadable is not recoverable, so there is nothing to offer and
        // nothing to keep.
        var damaged = Crashed("all-damaged");

        File.WriteAllText(Path.Combine(damaged.SessionDir, "broken.json"), "{{{");

        var live = New("live");
        live.Claim();

        Assert.Equal(1, live.TidyEmptyAbandonedFolders());
    }

    [Fact]
    public void ClearOnCleanExitLeavesNothingBehind()
    {
        var recovery = New("clean");
        recovery.Claim();
        recovery.WriteNow(Doc(@"C:\work\clean.docx"), Document("saved and closed"));

        recovery.ClearOnCleanExit();

        Assert.False(Directory.Exists(recovery.SessionDir));
    }

    [Fact]
    public void DiscardOnAFolderThatIsNotThereDoesNothing() =>
        Recovery.Discard(Path.Combine(_root.FullName, "never-existed"));

    [Fact]
    public void AMissingRootIsNoLeftovers() =>
        Assert.Empty(Recovery.FindLeftovers(Path.Combine(_root.FullName, "no-such-folder")));

    [Fact]
    public void ADamagedRecoveryFileIsIgnoredRatherThanFatal()
    {
        var folder = Path.Combine(_root.FullName, "damaged");
        Directory.CreateDirectory(folder);

        File.WriteAllText(Path.Combine(folder, "nonsense.json"), "{ this is not json");

        Assert.Null(Recovery.Read(Path.Combine(folder, "nonsense.json")));
        Assert.Empty(Recovery.FindLeftovers(_root.FullName));
    }

    [Fact]
    public void ADamagedFileDoesNotHideAGoodOneBesideIt()
    {
        var writer = Crashed("mixed");

        writer.WriteNow(Doc(@"C:\work\good.docx"), Document("recoverable"));

        File.WriteAllText(Path.Combine(writer.SessionDir, "broken.json"), "]]not json[[");

        var leftovers = Recovery.FindLeftovers(_root.FullName);

        Assert.Single(leftovers);
        Assert.Equal("good.docx", leftovers[0].Title);
    }

    [Fact]
    public void AWriteThatCannotHappenIsLoggedRatherThanThrown()
    {
        // A folder name no filesystem will take.
        var recovery = new Recovery(Path.Combine(_root.FullName, "bad\0name"), "nope");

        // Claim fails and the write after it does nothing. Neither throws,
        // because a safety net that interrupts the user to say it is missing
        // has made things worse rather than better.
        Assert.False(recovery.Claim());

        recovery.WriteNow(Doc(null), Document("x"));
    }

    [Fact]
    public void TheIntervalIsClampedRatherThanTakenLiterally()
    {
        // A zero-second timer would walk every open document as fast as the
        // dispatcher allows.
        Assert.True(Recovery.MinIntervalSeconds > 0);
        Assert.True(Recovery.MaxIntervalSeconds >= 30 * 60);
    }
}
