using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using ScratchpadLite.App.Editor;
using ScratchpadLite.App.Services;
using ScratchpadLite.Core.Fonts;
using ScratchpadLite.Core.Model;
using ScratchpadLite.Core.Platform;

namespace ScratchpadLite.App.UI;

/// <summary>
/// Crash recovery, AutoSave, printing, Preferences and the single-instance
/// hand-off (Sections 6.13 to 6.17).
/// </summary>
/// <remarks>
/// <para>
/// What these have in common is that they all act on the whole window rather
/// than on the document in front: recovery copies every tab, AutoSave saves
/// every modified tab, Preferences changes settings that every tab shows, and
/// files arriving from a second copy go into new tabs. That is why they are
/// together here and not in <c>MainWindow.Files.cs</c>, which is about one
/// document at a time.
/// </para>
/// <para>
/// Recovery and AutoSave are deliberately different things and are kept
/// different here (Section 6.13). Recovery is always on, writes only to the
/// data folder, and covers untitled documents. AutoSave is off until the user
/// turns it on, writes the user's own file, and declines the moment that would
/// be presumptuous. Every case AutoSave declines is still covered by recovery,
/// which is what makes declining safe.
/// </para>
/// </remarks>
public partial class MainWindow
{
    private readonly Recovery _recovery = new(AppPaths.RecoveryDir, Guid.NewGuid().ToString("N"));
    private readonly AutoSave _autoSave = new();

    private DispatcherTimer? _settingsTimer;

    // ------------------------------------------------------- settings file --

    /// <summary>
    /// Writes <c>settings.json</c> in half a second's time (Section 6.2).
    /// </summary>
    /// <remarks>
    /// Debounced, because dragging the zoom slider changes a setting on every
    /// pixel and a file write per pixel would be felt. Restarting the timer is
    /// what debounces it: the write happens once the changes stop.
    /// </remarks>
    private void SaveSettingsSoon()
    {
        _settingsTimer ??= CreateSettingsTimer();

        _settingsTimer.Stop();
        _settingsTimer.Start();
    }

    private DispatcherTimer CreateSettingsTimer()
    {
        var timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(500),
        };

        timer.Tick += (_, _) =>
        {
            timer.Stop();
            _settings.Save(AppPaths.SettingsFile);
        };

        return timer;
    }

    /// <summary>
    /// Takes a changed settings record and remembers it (Section 6.2).
    /// </summary>
    /// <remarks>
    /// Everything that changes a setting goes through here, so there is one
    /// place that knows a change has to reach the file. A direct assignment
    /// would be a setting that works until the application is restarted,
    /// which is the kind of bug nobody reports because it looks like they
    /// imagined it.
    /// </remarks>
    private void Apply(Settings settings)
    {
        _settings = settings;

        SaveSettingsSoon();
    }

    // ------------------------------------------------- start and stop them --

    /// <summary>
    /// Starts everything in Section 4.3 steps 9 and 11, after the first frame.
    /// </summary>
    private void StartSessionServices()
    {
        _recovery.DueForWrite += (_, _) => WriteRecoveryForEverything();
        _recovery.Claim();
        _recovery.Start(_settings.RecoveryIntervalSeconds);

        _autoSave.Enabled = _settings.AutoSaveEnabled;
        _autoSave.DueForSave += (_, _) => _ = AutoSaveEverythingAsync();
        _autoSave.Start(_settings.AutoSaveIntervalSeconds);

        SyncAutoSaveSwitch();

        // Section 6.15: the listener is started here rather than before the
        // first frame, and on a background thread, so a window nobody is
        // sending files to pays nothing for the ability to receive them.
        if (_settings.SingleInstance)
        {
            SingleInstance.StartListening(OnFilesFromAnotherCopy);
        }

        // Section 6.13.1: anything a previous run left behind. Offered after
        // the startup files, so a crash and a command line do not fight over
        // which tab is in front.
        OfferLeftovers();
    }

    // ------------------------------------------------ recovery: 6.13.1 -----

    /// <summary>
    /// Copies every document that has changed since its last copy
    /// (Section 6.13.1).
    /// </summary>
    /// <remarks>
    /// The walk is on the UI thread because a FlowDocument belongs to this
    /// thread; the serialize and the write are not. A tab that has never been
    /// shown has no editor and nothing in it that is not already in its file,
    /// so it is skipped.
    /// </remarks>
    private void WriteRecoveryForEverything()
    {
        foreach (var session in _tabs.Tabs)
        {
            if (session.View is null || !session.EditedSinceRecoveryWrite)
            {
                continue;
            }

            try
            {
                var ir = DocumentIo.ToIr(session.View.Editor.Document, session, _styles);

                session.EditedSinceRecoveryWrite = false;

                _ = _recovery.WriteAsync(session, ir);
            }
            catch (Exception ex)
            {
                // A failed recovery write must never reach the user as a
                // dialog, and must never stop the other documents being
                // copied.
                Log.Warn($"{session.Title} could not be copied for recovery: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Copies every open document now, synchronously, for the crash handlers
    /// (Section 6.13.1).
    /// </summary>
    /// <remarks>
    /// Called from <c>App</c> while an unhandled exception is on its way up.
    /// There is no time for a background task and no second chance, so this
    /// blocks - and swallows everything, because an exception thrown while
    /// handling an exception loses the first one.
    /// </remarks>
    internal void WriteRecoveryNow()
    {
        foreach (var session in _tabs.Tabs)
        {
            if (session.View is null)
            {
                continue;
            }

            try
            {
                _recovery.WriteNow(session, DocumentIo.ToIr(session.View.Editor.Document, session, _styles));
            }
            catch (Exception ex)
            {
                Log.Warn($"{session.Title} could not be copied during a crash: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Offers whatever a previous run left behind (Section 6.13.1).
    /// </summary>
    private void OfferLeftovers()
    {
        // A run that was killed having saved everything leaves a folder with
        // nothing in it, and nothing else would ever delete it.
        _recovery.TidyEmptyAbandonedFolders();

        // Embedded fonts unpacked by a previous run (Section 7.4). WPF holds
        // a font file open for the life of the process, so this is the only
        // moment they can be removed: at launch, when every one of them
        // belongs to a run that has ended.
        var fonts = FontEmbedding.TidyUnpacked(_tabs.Tabs.Select(t => t.Id));

        if (fonts > 0)
        {
            Log.Info($"tidied {fonts} unpacked embedded font folders from earlier runs");
        }

        var leftovers = _recovery.FindLeftovers();

        if (leftovers.Count == 0)
        {
            return;
        }

        Log.Info($"{leftovers.Count} document(s) were left for recovery by a previous run");

        var dialog = new Dialogs.RecoveryDialog { Owner = this };

        dialog.Describe(leftovers);
        dialog.ShowDialog();

        switch (dialog.Result)
        {
            case Dialogs.RecoveryDialog.Answer.Restore:
                foreach (var leftover in dialog.Chosen)
                {
                    Restore(leftover);
                }

                // Only the folders whose every document was taken are cleared,
                // so a half-restored set is offered again rather than lost.
                foreach (var folder in dialog.Chosen
                             .Select(c => c.Folder)
                             .Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    if (dialog.All.Where(a => a.Folder == folder).All(dialog.Chosen.Contains))
                    {
                        Recovery.Discard(folder);
                    }
                }

                Status.ShowMessage($"Recovered {dialog.Chosen.Count} document(s). None of your files were changed.");
                break;

            case Dialogs.RecoveryDialog.Answer.Discard:
                foreach (var folder in dialog.All.Select(a => a.Folder).Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    Recovery.Discard(folder);
                }

                Status.ShowMessage("The recovered documents were thrown away.");
                break;
        }
    }

    /// <summary>
    /// Opens one recovered document as a modified tab (Section 6.13.1).
    /// </summary>
    /// <remarks>
    /// Modified on purpose: what is on screen is not what is in the file, so
    /// the tab's dot and the title's star are telling the truth, and closing
    /// without saving asks. Nothing is written over the original until the user
    /// saves it themselves.
    /// </remarks>
    private void Restore(Recovery.Leftover leftover)
    {
        if (Recovery.Read(leftover.File) is not { } saved)
        {
            Status.ShowMessage($"{leftover.Title} could not be read back.", transient: false);

            return;
        }

        var session = IsReusable(Session) ? Session : _tabs.Add();

        _tabs.Activate(session);

        var view = _tabs.ViewFor(session);

        view.Editor.Document = DocumentIo.ToFlowDocument(saved.Document, _styles);

        session.FilePath = saved.OriginalPath;
        session.PendingPath = null;
        session.FormatName = saved.FormatName;
        session.Meta = saved.Document.Meta;
        session.Page = saved.Document.Page;
        session.FontsUsed = saved.Document.FontsUsed;
        session.OpenedStamp = saved.OriginalPath is { } path ? FileOps.Stamp.Of(path) : null;

        // Not the IR as last saved: the file on disk is the older version, and
        // claiming otherwise would let a save decide there was nothing to do.
        session.LastSavedIr = null;

        AfterDocumentChanged(session);

        session.IsDirty = true;
        session.EditedSinceRecoveryWrite = true;

        UpdateTitle();
        RefreshTabStrip();
    }

    // ------------------------------------------------- AutoSave: 6.13.2 ----

    /// <summary>
    /// Saves every modified document that AutoSave is allowed to save
    /// (Section 6.13.2).
    /// </summary>
    private async Task AutoSaveEverythingAsync()
    {
        if (!_autoSave.Enabled)
        {
            return;
        }

        var now = DateTime.UtcNow;

        foreach (var session in _tabs.Modified.ToList())
        {
            if (session.View is null || !_autoSave.IsIdleEnough(session, now))
            {
                continue;
            }

            SpDocument ir;

            try
            {
                // Timed, because Section 6.13.2 gives a slow document a longer
                // pause rather than letting the walk land between keystrokes.
                var clock = Stopwatch.StartNew();

                ir = DocumentIo.ToIr(session.View.Editor.Document, session, _styles);

                _autoSave.NoteWalk(session, clock.Elapsed);
            }
            catch (Exception ex)
            {
                Log.Warn($"{session.Title} could not be walked for AutoSave: {ex.Message}");

                continue;
            }

            var decision = AutoSave.Check(session, ir);

            if (!decision.CanSave)
            {
                // Once per document per reason, not on every tick
                // (Section 6.13.2).
                if (_autoSave.IsWorthSaying(session, decision))
                {
                    Status.ShowMessage(decision.Note, transient: false);
                }

                continue;
            }

            if (await SaveAsync(session, saveAs: false, fromAutoSave: true).ConfigureAwait(true))
            {
                _autoSave.Clear(session);
                session.LastAutoSaveUtc = DateTime.UtcNow;

                Status.ShowMessage($"Saved {DateTime.Now:HH:mm}");
            }
        }

        SyncAutoSaveSwitch();
    }

    private void ToggleAutoSave() => SetAutoSave(!_autoSave.Enabled);

    private void SetAutoSave(bool on)
    {
        _autoSave.Enabled = on;

        Apply(_settings with { AutoSaveEnabled = on });

        SyncAutoSaveSwitch();

        // Section 6.13.2: turning it on with modified documents open saves them
        // at the next check rather than all at once, so this says nothing about
        // saving and starts nothing.
        Status.ShowMessage(on
            ? "AutoSave is on. Your documents will be saved for you."
            : "AutoSave is off. Crash recovery is still on.");
    }

    /// <summary>
    /// Puts the switch, the menu tick and the hover note in step with the
    /// setting and the document in front (Section 6.13.2).
    /// </summary>
    private void SyncAutoSaveSwitch()
    {
        Status.ShowAutoSaveWithoutRaising(_autoSave.Enabled, _autoSave.TooltipFor(_tabs.Active));

        AutoSaveMenuItem.IsChecked = _autoSave.Enabled;
    }

    /// <summary>
    /// Section 6.13.2: AutoSave when the window loses focus, if that setting is
    /// on.
    /// </summary>
    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);

        if (_autoSave.Enabled && _settings.AutoSaveOnFocusLoss)
        {
            _ = AutoSaveEverythingAsync();
        }
    }

    // -------------------------------------------------- printing: 6.17 -----

    private void PrintDocument()
    {
        try
        {
            if (Printing.Print(Rtb.Document, Session.Title, Session.Page, this))
            {
                Status.ShowMessage($"Sent {Session.Title} to the printer.");
            }
        }
        catch (Printing.PrintFailed ex)
        {
            Status.ShowMessage(ex.Message, transient: false);
            MessageBox.Show(this, ex.Message, "Print", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ExportPdf()
    {
        try
        {
            if (Printing.ExportPdf(Rtb.Document, Session.Title, Session.Page))
            {
                Status.ShowMessage("Windows will ask where to put the PDF.");

                return;
            }

            MessageBox.Show(
                this,
                Printing.PdfPrinterMissingMessage,
                "Export as PDF",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Printing.PrintFailed ex)
        {
            Status.ShowMessage(ex.Message, transient: false);
            MessageBox.Show(this, ex.Message, "Export as PDF", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ShowPageSetup()
    {
        var dialog = new Dialogs.PageSetupDialog { Owner = this };

        dialog.Show(Session.Page);

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        Session.Page = dialog.Result;

        // The page setup is part of the document, so changing it is an edit:
        // DOCX and ODT store it, and a document whose margins changed without
        // the dot appearing would close without offering to save them.
        OnDocumentEdited(Session);

        Status.ShowMessage($"Page set to {dialog.Result.Paper}, "
            + (dialog.Result.Landscape ? "landscape." : "portrait."));
    }

    // ----------------------------------------------- Preferences: 6.2 ------

    private void ShowPreferences()
    {
        var dialog = new Dialogs.Preferences { Owner = this };

        dialog.Show(_settings, _styles, SpellSupport.Languages());

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        ApplyPreferences(dialog.Result);
    }

    /// <summary>
    /// Puts everything the Preferences dialog changed into effect
    /// (Section 6.2).
    /// </summary>
    /// <remarks>
    /// Written as "apply all of it" rather than "work out what changed",
    /// because every one of these is idempotent and a diff would be a second
    /// place for a setting to be forgotten. Applying a theme that is already
    /// applied costs one dictionary swap.
    /// </remarks>
    private void ApplyPreferences(Settings wanted)
    {
        var previous = _settings;

        Apply(wanted);

        Themes.ThemeManager.Apply(wanted.Theme);

        foreach (var view in LiveViews())
        {
            view.DarkPaper = wanted.DarkPaper;
            view.PageView = wanted.PageView;
        }

        Status.Visibility = wanted.ShowStatusBar ? Visibility.Visible : Visibility.Collapsed;
        FormatToolbars.PreviewFontNamesInOwnFont = wanted.PreviewFontNamesInOwnFont;
        FormatToolbars.ShowRow1 = wanted.ShowToolbarRow1;
        FormatToolbars.ShowRow2 = wanted.ShowToolbarRow2;

        Fonts.FontRegistry.DefaultFamily = wanted.DefaultFontFamily;

        if (wanted.SpellCheckEnabled != previous.SpellCheckEnabled
            || wanted.SpellCheckLanguage != previous.SpellCheckLanguage
            || wanted.SpellCheckMaxCharacters != previous.SpellCheckMaxCharacters)
        {
            foreach (var view in LiveViews())
            {
                ApplySpellCheck(view);
            }

            Status.ShowLanguage(wanted.SpellCheckLanguage);
        }

        _recovery.Start(wanted.RecoveryIntervalSeconds);

        _autoSave.Enabled = wanted.AutoSaveEnabled;
        _autoSave.Start(wanted.AutoSaveIntervalSeconds);

        SyncAutoSaveSwitch();
        SyncViewMenu();

        // Section 6.15: switched off, the listener stays up until the next
        // launch. Stopping it would leave a second copy with nowhere to send
        // its files and no way to know, which is worse than one more window.
        if (wanted.SingleInstance && !previous.SingleInstance)
        {
            SingleInstance.StartListening(OnFilesFromAnotherCopy);
        }

        Status.ShowMessage("Preferences saved.");
    }

    // ------------------------------------------- a second copy: 6.15 -------

    /// <summary>
    /// Files handed over by a second launch (Section 6.15).
    /// </summary>
    /// <remarks>
    /// What arrives is a list of paths from another process, so each one is
    /// checked against the filesystem before anything is done with it, and
    /// anything that is not a file that exists is logged and dropped. An empty
    /// list means "come to the front", which is what a second launch with no
    /// command line is asking for.
    /// </remarks>
    private void OnFilesFromAnotherCopy(IReadOnlyList<string> files)
    {
        ComeToTheFront();

        _ = OpenHandedOverAsync(files);
    }

    private async Task OpenHandedOverAsync(IReadOnlyList<string> files)
    {
        foreach (var path in files)
        {
            if (!IsAFileThatExists(path))
            {
                Log.Warn($"a second copy sent something that is not a file and it was ignored: {path}");

                continue;
            }

            await OpenAsync(path).ConfigureAwait(true);
        }
    }

    private static bool IsAFileThatExists(string path)
    {
        try
        {
            return Path.IsPathFullyQualified(path) && File.Exists(path);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException)
        {
            return false;
        }
    }

    /// <summary>
    /// Brings this window up where the user can see it (Section 6.15).
    /// </summary>
    /// <remarks>
    /// <c>Activate</c> alone is not enough for a minimized window: Windows
    /// restores it only if the state is changed too, and a window that comes
    /// back as an icon in the taskbar looks like nothing happened.
    /// </remarks>
    private void ComeToTheFront()
    {
        if (WindowState == WindowState.Minimized)
        {
            WindowState = _settings.WindowMaximized ? WindowState.Maximized : WindowState.Normal;
        }

        Activate();
        Focus();
    }

    // ---------------------------------------------------- clean exit -------

    /// <summary>
    /// Releases the recovery lock and deletes this run's folder
    /// (Section 6.13.1).
    /// </summary>
    /// <remarks>
    /// Only ever called from a close that went all the way through, which is
    /// what makes a leftover folder mean something: a run that ended any other
    /// way leaves its files, and that is the signal the next launch reads.
    /// </remarks>
    internal void EndSessionCleanly()
    {
        _autoSave.Stop();
        _recovery.ClearOnCleanExit();

        SingleInstance.Release();
    }
}
