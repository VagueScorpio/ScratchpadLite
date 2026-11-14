using System.IO;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using ScratchpadLite.App.Fonts;
using ScratchpadLite.App.Services;
using ScratchpadLite.Core.Formats;
using ScratchpadLite.Core.Model;
using ScratchpadLite.Core.Platform;

namespace ScratchpadLite.App.UI;

/// <summary>
/// Opening and saving, from the window's side (Sections 4.4, 6.12 and 7.1).
/// </summary>
/// <remarks>
/// Kept in its own file because MainWindow is wiring, and this is the one part
/// of it with a flow of its own: dialog, background read, build, warn - and
/// the reverse for a save, with the lossy-format check in the middle.
/// <para>
/// Every one of these works on one <see cref="DocumentSession"/>, which since
/// Phase 7 is one tab of several rather than the only document there is.
/// </para>
/// </remarks>
public partial class MainWindow
{
    /// <summary>The tab being worked on. There is always at least one.</summary>
    private DocumentSession Session => _tabs.Active!;

    /// <summary>Set while a file is being read or written, so two cannot overlap.</summary>
    private bool _busy;

    /// <summary>
    /// Opens the files named on the command line (Section 4.3), and otherwise
    /// the files that were open when the app was last closed (Section 6.12).
    /// </summary>
    /// <remarks>
    /// Only the first file is read. The rest become tabs that carry their
    /// names and read themselves when they are first clicked, which is what
    /// Section 2.1 asks for and also what makes dropping twenty files onto the
    /// .exe reasonable.
    /// </remarks>
    private async Task OpenStartupFilesAsync()
    {
        var files = _options.Files.Count > 0
            ? _options.Files.ToList()
            : _settings.RestoreLastSession
                ? SessionFiles.Load().Where(File.Exists).ToList()
                : [];

        if (files.Count == 0)
        {
            return;
        }

        // Tabs for all of them first, so the strip is complete before the read
        // of the first one begins.
        var tabs = files.Select(path => _tabs.Add(path)).ToList();

        // The one tab that was there at startup is empty and untitled, and
        // keeping it would mean every launch with a file opened an "Untitled 1"
        // nobody asked for.
        if (_tabs.Tabs.Count > files.Count && IsReusable(_tabs.Tabs[0]))
        {
            _tabs.Remove(_tabs.Tabs[0]);
        }

        _tabs.Activate(tabs[0]);

        await LoadPendingAsync(tabs[0]).ConfigureAwait(true);
    }

    /// <summary>
    /// Reads the file a tab stands for, the first time that tab is shown
    /// (Section 6.12).
    /// </summary>
    private async Task LoadPendingAsync(DocumentSession session)
    {
        if (session.PendingPath is not { } path)
        {
            return;
        }

        // Cleared first: a read that fails must not leave the tab trying again
        // every time it is clicked.
        session.PendingPath = null;

        await ReadIntoAsync(session, path).ConfigureAwait(true);
    }

    /// <summary>A new, empty document in a new tab (Section 6.12).</summary>
    private void NewDocument()
    {
        var session = _tabs.Add();

        _tabs.Activate(session);

        var view = session.View!;

        view.Editor.Document = DocumentIo.ToFlowDocument(SpDocument.Empty, _styles);
        view.ApplyDefaults(_settings, _styles);

        AfterDocumentChanged(session);
        Status.ShowMessage("New document.");
    }

    private async Task OpenWithDialogAsync()
    {
        if (_busy)
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = "Open",

            // Built from the static table, so no format package is loaded just
            // to show the dialog (Section 2.1).
            Filter = FormatRegistry.OpenFilter(),
            CheckFileExists = true,
            Multiselect = true,
        };

        if (Session.FilePath is { } current)
        {
            dialog.InitialDirectory = Path.GetDirectoryName(current);
        }

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        foreach (var path in dialog.FileNames)
        {
            await OpenAsync(path).ConfigureAwait(true);
        }
    }

    /// <summary>
    /// Opens one file in a tab: read in the background, build on the UI thread
    /// (Section 4.4).
    /// </summary>
    internal async Task OpenAsync(string path)
    {
        if (_busy)
        {
            return;
        }

        // Section 6.12: the same file twice is one tab, switched to.
        if (_tabs.ForPath(path) is { } already)
        {
            _tabs.Activate(already);
            await LoadPendingAsync(already).ConfigureAwait(true);

            Status.ShowMessage($"{Path.GetFileName(path)} is already open.");

            return;
        }

        // An untouched Untitled tab is where the file goes, rather than beside
        // it: otherwise every first Open leaves an empty tab behind.
        var session = IsReusable(Session) ? Session : _tabs.Add();

        _tabs.Activate(session);

        await ReadIntoAsync(session, path).ConfigureAwait(true);
    }

    private async Task ReadIntoAsync(DocumentSession session, string path)
    {
        if (_busy)
        {
            return;
        }

        _busy = true;

        try
        {
            Status.ShowMessage($"Opening {Path.GetFileName(path)}...", transient: false);
            Mouse.OverrideCursor = Cursors.AppStarting;

            // The font index has to be loaded before the document is built, or
            // a font from the fonts/ folder resolves to the default and the
            // document opens looking wrong (Section 6.6).
            await FontRegistry.IndexReady.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(true);

            var view = _tabs.ViewFor(session);

            // RTF is the exception (Section 7.2): WPF's converter only speaks
            // FlowDocument, so it is read here on the UI thread rather than in
            // the background, and the IR comes from walking the result.
            var opened = IsRtf(path)
                ? ReadRtf(view, path)
                : await DocumentIo.ReadAsync(path, _styles, CancellationToken.None).ConfigureAwait(true);

            session.FilePath = path;
            session.PendingPath = null;
            session.Meta = opened.Document.Meta;
            session.Page = opened.Document.Page;
            session.OpenedStamp = opened.Stamp;
            session.OpenWarnings = opened.Warnings;
            session.FontsUsed = opened.Document.FontsUsed;
            session.LastSavedIr = opened.Document;
            session.FormatName = FormatRegistry.InfoFor(Path.GetExtension(path))?.Name ?? "TXT";

            // Before the document is built, so a run asking for a family the
            // file brought with it resolves to that font rather than to
            // whatever this PC has under the same name (Section 7.4).
            LoadEmbeddedFonts(session, opened.EmbeddedFonts);

            // RTF has already put its own FlowDocument in the editor, and
            // rebuilding it from the walked IR would throw away whatever WPF's
            // converter kept that the IR has no room for.
            if (!IsRtf(path))
            {
                view.Editor.Document =
                    DocumentIo.ToFlowDocument(opened.Document, _styles, session.Id);
            }

            AfterDocumentChanged(session);

            if (ReferenceEquals(session, _tabs.Active))
            {
                ShowOpenWarnings(opened.Warnings);
                ShowMissingFonts(opened.Document.FontsUsed);
            }

            RecentFiles.Add(path);

            Status.ShowMessage($"Opened {Path.GetFileName(path)}.");
        }
        catch (DocumentIo.OpenException ex)
        {
            Status.ShowMessage(ex.Message, transient: false);
            MessageBox.Show(this, ex.Message, "Open", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (TimeoutException)
        {
            Status.ShowMessage("The fonts are still being prepared. Try again in a moment.", transient: false);
        }
        catch (Exception ex)
        {
            Log.Error($"opening {path} failed", ex);
            Status.ShowMessage($"{Path.GetFileName(path)} could not be opened.", transient: false);
        }
        finally
        {
            Mouse.OverrideCursor = null;
            _busy = false;
        }
    }

    /// <summary>
    /// Saves: walk on the UI thread, warn if the format would lose something,
    /// write in the background (Section 4.4).
    /// </summary>
    /// <param name="session">The document to save.</param>
    /// <param name="saveAs">Ask for a name even when the document has one.</param>
    /// <param name="fromAutoSave">
    /// True when AutoSave asked for this (Section 6.13.2).
    /// </param>
    /// <remarks>
    /// AutoSave comes through here rather than having a save of its own, so
    /// there is one atomic write, one lossy-format rule and one changed-on-disk
    /// rule in the application. What the flag changes is only the asking: an
    /// AutoSave never opens a dialog, because a dialog from a timer is a dialog
    /// that appears while the user is typing in another program. The three
    /// questions it would have asked were all answered before it was called -
    /// <c>AutoSave.Check</c> declines a document that would lose formatting,
    /// one that is read-only and one that changed on disk - which is why
    /// skipping the dialogs here is not skipping the checks.
    /// </remarks>
    private async Task<bool> SaveAsync(DocumentSession session, bool saveAs, bool fromAutoSave = false)
    {
        if (_busy)
        {
            return false;
        }

        var view = _tabs.ViewFor(session);
        var path = session.FilePath;

        if (saveAs || path is null)
        {
            if (fromAutoSave)
            {
                return false;           // never asks; AutoSave.Check said so too
            }

            path = AskWhereToSave(session, path);

            if (path is null)
            {
                return false;
            }
        }

        var document = DocumentIo.ToIr(view.Editor.Document, session, _styles);

        if (!fromAutoSave)
        {
            // The lossy-format check (Section 7.1), on what this document
            // actually uses rather than on the format in general.
            var decided = ConfirmLossyFormat(document, path);

            if (decided is null)
            {
                return false;
            }

            path = decided;

            if (!ConfirmOverwriteIfChangedOnDisk(session, path))
            {
                return false;
            }
        }

        // Section 6.13.2: with AutoSave on, the .bak is the version from when
        // the file was opened, written once. A backup per AutoSave would be a
        // backup of the state two minutes ago, which is worth nothing.
        var keepBak = _settings.KeepBakFile && !(_settings.AutoSaveEnabled && session.BakWritten);

        _busy = true;

        try
        {
            Status.ShowMessage($"Saving {Path.GetFileName(path)}...", transient: false);

            if (IsRtf(path))
            {
                WriteRtf(view, path, keepBak);
            }
            else
            {
                await DocumentIo
                    .WriteAsync(
                        document,
                        path,
                        _styles,
                        keepBak,
                        FontsToEmbed(session, document, path, fromAutoSave),
                        CancellationToken.None)
                    .ConfigureAwait(true);
            }

            session.FilePath = path;
            session.LastSavedIr = document;
            session.OpenedStamp = FileOps.Stamp.Of(path);
            session.IsDirty = false;
            session.BakWritten = true;
            session.FormatName = FormatRegistry.InfoFor(Path.GetExtension(path))?.Name ?? "TXT";

            UpdateTitle();
            RefreshTabStrip();
            RecentFiles.Add(path);

            if (ReferenceEquals(session, _tabs.Active))
            {
                Status.ShowFormat(session.FormatName);
            }

            if (!fromAutoSave)
            {
                Status.ShowMessage($"Saved {Path.GetFileName(path)}.");
            }

            return true;
        }
        catch (FileOps.SaveException ex)
        {
            Status.ShowMessage(ex.Message, transient: false);
            Complain(ex.Message, fromAutoSave);
        }
        catch (Exception ex)
        {
            Log.Error($"saving {path} failed", ex);

            var message = $"{Path.GetFileName(path)} could not be saved.";

            Status.ShowMessage(message, transient: false);
            Complain(message, fromAutoSave);
        }
        finally
        {
            _busy = false;
        }

        return false;
    }

    /// <summary>Every modified tab, one after another (Section 8.1).</summary>
    private async Task SaveAllAsync()
    {
        foreach (var session in _tabs.Modified.ToList())
        {
            if (!await SaveAsync(session, saveAs: false).ConfigureAwait(true))
            {
                return;             // cancelled, so the rest are not asked about either
            }
        }
    }

    /// <summary>Whether this path is the one format handled on the FlowDocument.</summary>
    private static bool IsRtf(string path) =>
        FormatRegistry.InfoFor(Path.GetExtension(path)) is { HandledOnTheFlowDocument: true };

    /// <summary>
    /// Reads an RTF file (Section 7.2).
    /// </summary>
    /// <remarks>
    /// On the UI thread, because WPF's RTF converter works on a FlowDocument
    /// and a FlowDocument belongs to the thread that owns the window. That is
    /// the whole reason this format bypasses DocumentIo. A very large RTF will
    /// therefore pause the window where a .docx of the same size would not -
    /// an honest cost of using the converter that already exists rather than
    /// writing one.
    /// </remarks>
    private DocumentIo.Opened ReadRtf(Editor.EditorView view, string path)
    {
        var stamp = FileOps.Stamp.Of(path);
        var flow = new System.Windows.Documents.FlowDocument();

        using (var stream = File.OpenRead(path))
        {
            new Formats.RtfFormat(_styles).Load(flow, stream);
        }

        view.Editor.Document = flow;

        // Walked for the save warning, the recovery file and the font list;
        // the editor already has the document itself.
        var document = Bridge.FlowWalker.Walk(flow, _styles);

        return new DocumentIo.Opened(
            document,
            ["Paragraph style names are not stored in RTF, so headings were matched by their formatting."],
            stamp);
    }

    private void WriteRtf(Editor.EditorView view, string path, bool keepBak)
    {
        var rtf = new Formats.RtfFormat(_styles);

        FileOps.AtomicWrite(
            path,
            stream => rtf.Save(view.Editor.Document, stream),
            keepBak);
    }

    /// <summary>
    /// Says what went wrong, in a dialog for a save the user asked for and only
    /// in the status bar for one a timer asked for (Section 6.13.2).
    /// </summary>
    private void Complain(string message, bool fromAutoSave)
    {
        if (fromAutoSave)
        {
            Log.Warn($"AutoSave: {message}");

            return;
        }

        MessageBox.Show(this, message, "Save", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private string? AskWhereToSave(DocumentSession session, string? current)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Save As",
            Filter = FormatRegistry.SaveFilter(),
            AddExtension = true,
            OverwritePrompt = true,
            FileName = current is null ? session.Title : Path.GetFileName(current),
        };

        if (current is { } path)
        {
            dialog.InitialDirectory = Path.GetDirectoryName(path);

            // Start on the format the file already is, so saving a .txt does
            // not quietly offer to turn it into a .docx.
            var writable = FormatRegistry.All.Where(f => f.CanWrite).ToList();
            var index = writable.FindIndex(
                f => f.Extensions.Contains(
                    Path.GetExtension(path).ToLowerInvariant(),
                    StringComparer.OrdinalIgnoreCase));

            if (index >= 0)
            {
                dialog.FilterIndex = index + 1;
            }
        }

        if (dialog.ShowDialog(this) != true)
        {
            return null;
        }

        var chosen = dialog.FileName;

        // The dialog adds the extension of the chosen filter, but only when
        // the typed name has none at all.
        if (Path.GetExtension(chosen).Length == 0
            && FormatRegistry.SaveFormatAt(dialog.FilterIndex) is { } format)
        {
            chosen += format.Extensions[0];
        }

        return chosen;
    }

    /// <summary>
    /// Shows the lossy-format warning when it applies, and returns the path to
    /// save to - which changes if the user chooses DOCX instead
    /// (Section 7.1).
    /// </summary>
    private string? ConfirmLossyFormat(SpDocument document, string path)
    {
        var extension = Path.GetExtension(path);
        var format = FormatRegistry.InfoFor(extension);

        if (format is null)
        {
            return path;
        }

        if (DocumentSession.SilencedWarnings.Contains(format.Name))
        {
            return path;
        }

        var lost = FormatRegistry.WhatWouldBeLost(IrFeatures.Used(document), format.Capabilities);

        if (lost.Count == 0)
        {
            return path;
        }

        var dialog = new Dialogs.SaveWarning { Owner = this };

        dialog.Describe(format.Name, lost, offerDocx: !format.Extensions.Contains(".docx"));
        dialog.ShowDialog();

        if (dialog.Silence)
        {
            DocumentSession.SilencedWarnings.Add(format.Name);
        }

        return dialog.Result switch
        {
            Dialogs.SaveWarning.Answer.SaveAnyway => path,
            Dialogs.SaveWarning.Answer.SaveAsDocx => Path.ChangeExtension(path, ".docx"),
            _ => null,
        };
    }

    /// <summary>
    /// Asks before overwriting a file something else has changed since it was
    /// opened (Section 6.14).
    /// </summary>
    private bool ConfirmOverwriteIfChangedOnDisk(DocumentSession session, string path)
    {
        if (!string.Equals(path, session.FilePath, StringComparison.OrdinalIgnoreCase))
        {
            return true;        // a different file; the dialog already asked
        }

        if (!FileOps.ChangedOnDisk(path, session.OpenedStamp))
        {
            return true;
        }

        var answer = MessageBox.Show(
            this,
            $"{Path.GetFileName(path)} has changed on disk since you opened it. "
            + "Saving now will overwrite those changes.\n\nSave anyway?",
            "Save",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        return answer == MessageBoxResult.Yes;
    }

    // ----------------------------------------------------------------- tabs --

    /// <summary>
    /// Whether a tab can be used for a file being opened: never saved, never
    /// typed in, and empty (Section 6.12).
    /// </summary>
    private static bool IsReusable(DocumentSession? session) =>
        session is { FilePath: null, IsDirty: false, PendingPath: null }
        && (session.View is null || IsEmpty(session.View.Editor.Document));

    private static bool IsEmpty(System.Windows.Documents.FlowDocument document) =>
        new System.Windows.Documents.TextRange(document.ContentStart, document.ContentEnd)
            .Text.Trim().Length == 0;

    /// <summary>
    /// Closes a tab, asking about unsaved changes first (Section 6.12). The
    /// last tab closed leaves a new empty one, because a window with no
    /// document in it has nothing to type into.
    /// </summary>
    private async Task CloseTabAsync(DocumentSession session)
    {
        switch (AskAboutOneTab(session))
        {
            case MessageBoxResult.Cancel:
                return;

            case MessageBoxResult.Yes when !await SaveAsync(session, saveAs: false).ConfigureAwait(true):
                return;         // the save was cancelled, so the close is too
        }

        ReleaseEmbeddedFonts(session);
        _recovery.Forget(session);

        var remaining = _tabs.Remove(session);

        if (remaining is null)
        {
            NewDocument();
        }
    }

    /// <summary>
    /// Save / Don't Save / Cancel for one tab (Section 6.12).
    /// </summary>
    private MessageBoxResult AskAboutOneTab(DocumentSession session)
    {
        if (!session.IsDirty)
        {
            return MessageBoxResult.No;
        }

        return MessageBox.Show(
            this,
            $"Save changes to {session.Title}?",
            "Scratchpad Lite",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Warning);
    }

    /// <summary>
    /// Everything that has to happen after a tab's document has been replaced:
    /// the undo history belongs to a document that is no longer open, and
    /// everything attached to the old tree went with it.
    /// </summary>
    private void AfterDocumentChanged(DocumentSession session)
    {
        var view = _tabs.ViewFor(session);

        session.IsDirty = false;

        view.Editor.IsUndoEnabled = false;
        view.Editor.IsUndoEnabled = true;

        view.ReattachToNewDocument(_settings, _styles);

        if (_spellCheckStarted)
        {
            ApplySpellCheck(view);
        }

        if (ReferenceEquals(session, _tabs.Active))
        {
            UpdateTitle();
            Status.ShowFormat(session.FormatName);
            view.Editor.Focus();
        }

        RefreshTabStrip();
    }

    private void UpdateTitle() =>
        Title = $"{(Session.IsDirty ? "*" : "")}{Session.Title} — Scratchpad Lite";

    private void RefreshTabStrip() => Tabs.Show(_tabs.Tabs, _tabs.Active);

    private void OnDocumentEdited(DocumentSession session)
    {
        // Both of these happen on every edit, not only on the first one. The
        // recovery flag says the copy on disk is out of date (Section 6.13.1)
        // and the typing clock is what keeps AutoSave from landing mid-word
        // (Section 6.13.2), and neither is answered by "it was already dirty".
        session.EditedSinceRecoveryWrite = true;
        _autoSave.NoteTyping();

        if (session.IsDirty)
        {
            return;
        }

        session.IsDirty = true;

        if (ReferenceEquals(session, _tabs.Active))
        {
            UpdateTitle();
        }

        RefreshTabStrip();
    }

    private void ShowOpenWarnings(IReadOnlyList<string> warnings)
    {
        if (warnings.Count == 0)
        {
            OpenWarningsBar.Visibility = Visibility.Collapsed;
            return;
        }

        OpenWarningsText.Text = string.Join("  ", warnings);
        OpenWarningsBar.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// Fills the Open Recent menu when it is opened rather than at startup, so
    /// the list is current and startup pays nothing for it (Section 2.1).
    /// </summary>
    private void OnOpenRecentOpened(object sender, RoutedEventArgs e)
    {
        OpenRecentMenu.Items.Clear();

        var recent = RecentFiles.Load(10);

        if (recent.Count == 0)
        {
            OpenRecentMenu.Items.Add(new System.Windows.Controls.MenuItem
            {
                Header = "Nothing yet",
                IsEnabled = false,
            });

            return;
        }

        foreach (var path in recent)
        {
            var exists = File.Exists(path);

            var item = new System.Windows.Controls.MenuItem
            {
                Header = Path.GetFileName(path),
                ToolTip = path,

                // A file that has moved is shown greyed rather than hidden, so
                // it is clear why it is not there any more.
                Opacity = exists ? 1.0 : 0.5,
            };

            var target = path;

            item.Click += (_, _) =>
            {
                if (File.Exists(target))
                {
                    _ = OpenAsync(target);
                }
                else
                {
                    RecentFiles.Remove(target);
                    Status.ShowMessage($"{Path.GetFileName(target)} is not there any more.");
                }
            };

            OpenRecentMenu.Items.Add(item);
        }
    }

    private void OnDismissOpenWarnings(object sender, RoutedEventArgs e) =>
        OpenWarningsBar.Visibility = Visibility.Collapsed;
}
