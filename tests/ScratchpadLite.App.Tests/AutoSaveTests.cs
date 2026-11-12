using System.IO;
using ScratchpadLite.App.Services;
using ScratchpadLite.Core.Model;
using ScratchpadLite.Core.Platform;
using Xunit;

namespace ScratchpadLite.App.Tests;

/// <summary>
/// AutoSave (Section 6.13.2): that it is off, when it is allowed to write, and
/// when it declines.
/// </summary>
/// <remarks>
/// Every skip rule is a test, because each of them exists to stop AutoSave
/// doing something the user would not have asked for: overwriting a file with
/// a lossy copy of their document, writing to a file somebody else just
/// changed, or inventing a name for a document that has never been saved. A
/// rule that stopped working would not break anything visibly - it would just
/// quietly start doing the thing.
/// </remarks>
public class AutoSaveTests : IDisposable
{
    private readonly DirectoryInfo _folder = Directory.CreateTempSubdirectory("sp-autosave-");

    public void Dispose()
    {
        foreach (var file in _folder.EnumerateFiles())
        {
            try
            {
                file.IsReadOnly = false;
            }
            catch (IOException)
            {
            }
        }

        try
        {
            _folder.Delete(recursive: true);
        }
        catch (IOException)
        {
        }

        GC.SuppressFinalize(this);
    }

    private string Make(string name, string contents = "x")
    {
        var path = Path.Combine(_folder.FullName, name);

        File.WriteAllText(path, contents);

        return path;
    }

    private static DocumentSession Doc(string? path) =>
        new()
        {
            FilePath = path,
            FormatName = path is null ? "TXT" : Path.GetExtension(path).TrimStart('.').ToUpperInvariant(),
            OpenedStamp = path is null ? null : FileOps.Stamp.Of(path),
        };

    private static SpDocument PlainText() =>
        new(
            [new Paragraph { Inlines = [new TextRun("just words", new CharStyle())] }],
            new Dictionary<string, string>(),
            new PageSetup(),
            new HashSet<string>());

    /// <summary>A document with a table, which Markdown and RTF can both carry.</summary>
    private static SpDocument WithATable() =>
        new(
            [
                new Table(
                    [[new Cell([new Paragraph { Inlines = [new TextRun("cell", new CharStyle())] }])]],
                    null),
            ],
            new Dictionary<string, string>(),
            new PageSetup(),
            new HashSet<string>());

    /// <summary>
    /// A document with coloured, underlined text, which Markdown has no syntax
    /// for.
    /// </summary>
    /// <remarks>
    /// A table would not do here. Markdown has pipe tables, so a .md file with
    /// a table in it loses nothing - which is the first thing the Section
    /// 6.13.2 tests found, and a reminder that the rule is about this document
    /// against this format, not about "Markdown is simple".
    /// </remarks>
    private static SpDocument WithColouredText() =>
        new(
            [
                new Paragraph
                {
                    Inlines =
                    [
                        new TextRun(
                            "important",
                            new CharStyle { Color = "#C00000", Underline = UnderlineKind.Wavy }),
                    ],
                },
            ],
            new Dictionary<string, string>(),
            new PageSetup(),
            new HashSet<string>());

    // ------------------------------------------------------------- it is off --

    [Fact]
    public void ItIsOffInAFreshSettingsFile() => Assert.False(new Settings().AutoSaveEnabled);

    [Fact]
    public void ItIsOffOnAFreshAutoSave() => Assert.False(new AutoSave().Enabled);

    // -------------------------------------------------------- the skip rules --

    [Fact]
    public void ADocumentThatWasNeverSavedIsLeftAlone()
    {
        var decision = AutoSave.Check(Doc(null), PlainText());

        Assert.Equal(AutoSave.Skip.NeverSaved, decision.Reason);
        Assert.False(decision.CanSave);
        Assert.Contains("Save this document once", decision.Note);
    }

    [Fact]
    public void ADocxWithATableIsSavedBecauseDocxCanCarryIt()
    {
        var decision = AutoSave.Check(Doc(Make("fine.docx")), WithATable());

        Assert.True(decision.CanSave);
        Assert.Equal(AutoSave.Skip.None, decision.Reason);
    }

    [Fact]
    public void AMarkdownFileWithColouredTextIsNotWrittenOver()
    {
        var decision = AutoSave.Check(Doc(Make("notes.md")), WithColouredText());

        Assert.Equal(AutoSave.Skip.WouldLoseFormatting, decision.Reason);
        Assert.Contains("Save as .docx", decision.Note);
    }

    [Fact]
    public void AMarkdownFileWithATableIsSavedBecauseMarkdownHasTables()
    {
        // The rule asks what this document uses against what this format
        // carries, not whether the format feels simple.
        Assert.True(AutoSave.Check(Doc(Make("tables.md")), WithATable()).CanSave);
    }

    [Fact]
    public void APlainTextFileWithATableIsNotWrittenOver()
    {
        var decision = AutoSave.Check(Doc(Make("notes.txt")), WithATable());

        Assert.Equal(AutoSave.Skip.WouldLoseFormatting, decision.Reason);
    }

    [Fact]
    public void APlainTextFileOfPlainTextIsFine()
    {
        // The rule is about what this document uses, not about the format in
        // general: a .txt of words is a .txt that loses nothing.
        var decision = AutoSave.Check(Doc(Make("plain.txt")), PlainText());

        Assert.True(decision.CanSave);
    }

    [Fact]
    public void RtfIsFine()
    {
        // Section 6.13.2 says so, and the reason is that RTF goes through WPF's
        // own converter rather than the IR, so the format check has nothing to
        // compare and nothing is lost.
        var decision = AutoSave.Check(Doc(Make("doc.rtf")), WithATable());

        Assert.True(decision.CanSave);
    }

    [Fact]
    public void AReadOnlyFileIsSkipped()
    {
        var path = Make("locked.docx");
        var session = Doc(path);

        new FileInfo(path).IsReadOnly = true;

        var decision = AutoSave.Check(session, PlainText());

        Assert.Equal(AutoSave.Skip.NotWritable, decision.Reason);
        Assert.Contains("read-only", decision.Note);
    }

    [Fact]
    public void AFileSomethingElseChangedIsSkipped()
    {
        var path = Make("shared.docx", "as opened");
        var session = Doc(path);

        // Something else wrote to it. Length is part of the stamp, so the
        // content has to differ in size as well as in time.
        File.WriteAllText(path, "changed by another program");

        var decision = AutoSave.Check(session, PlainText());

        Assert.Equal(AutoSave.Skip.ChangedOnDisk, decision.Reason);
        Assert.Contains("changed on disk", decision.Note);
    }

    [Fact]
    public void AnExtensionNothingCanWriteIsSkipped()
    {
        var decision = AutoSave.Check(Doc(Make("thing.xyz")), PlainText());

        Assert.False(decision.CanSave);
    }

    [Fact]
    public void WithoutTheWalkedDocumentTheFormatCheckIsSkippedButTheRestIsNot()
    {
        // The status-bar tooltip asks without walking, which is fine: the
        // answer it needs is about the file, not about the formatting.
        Assert.Equal(AutoSave.Skip.NeverSaved, AutoSave.Check(Doc(null), null).Reason);
        Assert.True(AutoSave.Check(Doc(Make("tooltip.md")), null).CanSave);
    }

    // ------------------------------------------------------- the idle pause --

    [Fact]
    public void ASaveDoesNotLandWhileSomebodyIsTyping()
    {
        var autoSave = new AutoSave();
        var session = Doc(null);

        autoSave.NoteTyping();

        Assert.False(autoSave.IsIdleEnough(session, DateTime.UtcNow));
    }

    [Fact]
    public void ASaveHappensOnceTypingHasStopped()
    {
        var autoSave = new AutoSave();
        var session = Doc(null);

        autoSave.NoteTyping();

        var later = DateTime.UtcNow + AutoSave.IdlePause + TimeSpan.FromSeconds(1);

        Assert.True(autoSave.IsIdleEnough(session, later));
    }

    [Fact]
    public void ADocumentWhoseWalkIsSlowWaitsLonger()
    {
        var autoSave = new AutoSave();
        var session = Doc(null);

        autoSave.NoteWalk(session, TimeSpan.FromMilliseconds(AutoSave.SlowWalkMilliseconds + 50));

        Assert.True(autoSave.IsSlow(session));

        autoSave.NoteTyping();

        var afterTheShortPause = DateTime.UtcNow + AutoSave.IdlePause + TimeSpan.FromMilliseconds(100);

        Assert.False(
            autoSave.IsIdleEnough(session, afterTheShortPause),
            "a document that takes 150 ms to walk must not be walked two seconds after a keystroke");

        var afterTheLongPause = DateTime.UtcNow + AutoSave.SlowDocumentPause + TimeSpan.FromSeconds(1);

        Assert.True(autoSave.IsIdleEnough(session, afterTheLongPause));
    }

    [Fact]
    public void ADocumentThatBecomesFastAgainStopsWaitingLonger()
    {
        var autoSave = new AutoSave();
        var session = Doc(null);

        autoSave.NoteWalk(session, TimeSpan.FromMilliseconds(500));
        Assert.True(autoSave.IsSlow(session));

        // Half the document was deleted, so it walks quickly now.
        autoSave.NoteWalk(session, TimeSpan.FromMilliseconds(3));

        Assert.False(autoSave.IsSlow(session));
    }

    // -------------------------------------------- saying it once, not always --

    [Fact]
    public void AReasonIsReportedOnceRatherThanEveryTick()
    {
        var autoSave = new AutoSave();
        var session = Doc(null);
        var decision = AutoSave.Check(session, PlainText());

        Assert.True(autoSave.IsWorthSaying(session, decision));
        Assert.False(autoSave.IsWorthSaying(session, decision));
        Assert.False(autoSave.IsWorthSaying(session, decision));
    }

    [Fact]
    public void ADifferentReasonIsWorthSaying()
    {
        var autoSave = new AutoSave();
        var session = Doc(null);

        autoSave.IsWorthSaying(session, AutoSave.Check(session, PlainText()));

        var somethingElse = new AutoSave.Decision(
            AutoSave.Skip.ChangedOnDisk, "AutoSave is paused: it changed on disk.");

        Assert.True(autoSave.IsWorthSaying(session, somethingElse));
    }

    [Fact]
    public void AfterASuccessfulSaveTheReasonIsWorthSayingAgain()
    {
        var autoSave = new AutoSave();
        var session = Doc(null);
        var decision = AutoSave.Check(session, PlainText());

        autoSave.IsWorthSaying(session, decision);
        autoSave.Clear(session);

        Assert.True(autoSave.IsWorthSaying(session, decision));
    }

    [Fact]
    public void TwoDocumentsAreToldAboutSeparately()
    {
        var autoSave = new AutoSave();
        var first = Doc(null);
        var second = Doc(null);

        Assert.True(autoSave.IsWorthSaying(first, AutoSave.Check(first, PlainText())));
        Assert.True(autoSave.IsWorthSaying(second, AutoSave.Check(second, PlainText())));
    }

    // ----------------------------------------------------------- the tooltip --

    [Fact]
    public void TheTooltipSaysItIsOffWhenItIsOff()
    {
        var tooltip = new AutoSave().TooltipFor(Doc(Make("anything.docx")));

        Assert.Contains("AutoSave is off", tooltip);
        Assert.Contains("recovery", tooltip);
    }

    [Fact]
    public void TheTooltipNamesTheDocumentWhenItIsSavingIt()
    {
        var autoSave = new AutoSave { Enabled = true };

        Assert.Contains("report.docx", autoSave.TooltipFor(Doc(Make("report.docx"))));
    }

    [Fact]
    public void TheTooltipExplainsWhyItIsNotSavingAnUntitledDocument()
    {
        var autoSave = new AutoSave { Enabled = true };

        Assert.Contains("Save this document once", autoSave.TooltipFor(Doc(null)));
    }

    // ----------------------------------------------------------- the timer --

    [Fact]
    public void TheTimerDoesNotFireWhileAutoSaveIsOff() =>
        AppHost.Run(() =>
        {
            var autoSave = new AutoSave();
            var fired = 0;

            autoSave.DueForSave += (_, _) => fired++;
            autoSave.Start(AutoSave.MinIntervalSeconds);

            AppHost.Settle();
            autoSave.Stop();

            Assert.Equal(0, fired);
        });

    [Fact]
    public void StopLeavesNoTimerBehind() =>
        AppHost.Run(() =>
        {
            var autoSave = new AutoSave { Enabled = true };

            autoSave.Start(AutoSave.MinIntervalSeconds);
            autoSave.Stop();

            // Stopping twice is what a Preferences change followed by an exit
            // does, and it must not throw.
            autoSave.Stop();
        });

    [Fact]
    public void TheIntervalHasAFloorAndACeiling()
    {
        Assert.True(AutoSave.MinIntervalSeconds > 0);
        Assert.True(AutoSave.MaxIntervalSeconds >= 30 * 60);
    }

    [Fact]
    public void TheIdlePauseIsTheTwoSecondsSection613PointTwoAsksFor() =>
        Assert.Equal(TimeSpan.FromSeconds(2), AutoSave.IdlePause);

    [Fact]
    public void TheSlowDocumentPauseIsTheFiveSecondsSection613PointTwoAsksFor() =>
        Assert.Equal(TimeSpan.FromSeconds(5), AutoSave.SlowDocumentPause);
}
