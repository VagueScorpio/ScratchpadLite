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
/// Opening and saving, from the window's side (Sections 4.4 and 7.1).
/// </summary>
/// <remarks>
/// Kept in its own file because MainWindow is wiring, and this is the one part
/// of it with a flow of its own: dialog, background read, build, warn - and
/// the reverse for a save, with the lossy-format check in the middle.
/// <para>
/// Phase 7 moves this to the tab manager, which will do the same thing once
/// per tab.
/// </para>
/// </remarks>
public partial class MainWindow
{
    private readonly DocumentSession _session = new();

    /// <summary>Set while a file is being read or written, so two cannot overlap.</summary>
    private bool _busy;

    /// <summary>
    /// Opens the files named on the command line (Section 4.3).
    /// </summary>
    /// <remarks>
    /// Only the first is opened until tabs arrive in Phase 7, and the rest are
    /// named rather than silently ignored.
    /// </remarks>
    private async Task OpenCommandLineFilesAsync()
    {
        if (_options.Files.Count == 0)
        {
            return;
        }

        await OpenAsync(_options.Files[0]).ConfigureAwait(true);

        if (_options.Files.Count > 1)
        {
            Status.ShowMessage(
                $"Only {Path.GetFileName(_options.Files[0])} was opened; tabs arrive in a later version.",
                transient: false);
        }
    }

    private void NewDocument()
    {
        if (!ConfirmDiscard())
        {
            return;
        }

        Editor.Editor.Document = DocumentIo.ToFlowDocument(SpDocument.Empty, _styles);
        Editor.ApplyDefaults(_settings, _styles);

        Reset(new DocumentSession());
        Status.ShowMessage("New document.");
    }

    private async Task OpenWithDialogAsync()
    {
        if (_busy || !ConfirmDiscard())
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
        };

        if (_session.FilePath is { } current)
        {
            dialog.InitialDirectory = Path.GetDirectoryName(current);
        }

        if (dialog.ShowDialog(this) == true)
        {
            await OpenAsync(dialog.FileName).ConfigureAwait(true);
        }
    }

    /// <summary>
    /// Opens one file: read in the background, build on the UI thread
    /// (Section 4.4).
    /// </summary>
    internal async Task OpenAsync(string path)
    {
        if (_busy)
        {
            return;
        }

        _busy = true;

        try
        {
            Status.ShowMessage($"Opening {Path.GetFileName(path)}...", transient: false);
            Mouse.OverrideCursor = System.Windows.Input.Cursors.AppStarting;

            // The font index has to be loaded before the document is built, or
            // a font from the fonts/ folder resolves to the default and the
            // document opens looking wrong (Section 6.6).
            await FontRegistry.IndexReady.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(true);

            // RTF is the exception (Section 7.2): WPF's converter only speaks
            // FlowDocument, so it is read here on the UI thread rather than in
            // the background, and the IR comes from walking the result.
            var opened = IsRtf(path)
                ? ReadRtf(path)
                : await DocumentIo.ReadAsync(path, _styles, CancellationToken.None).ConfigureAwait(true);

            var session = new DocumentSession
            {
                FilePath = path,
                Meta = opened.Document.Meta,
                Page = opened.Document.Page,
                OpenedStamp = opened.Stamp,
                OpenWarnings = opened.Warnings,
                FontsUsed = opened.Document.FontsUsed,
                LastSavedIr = opened.Document,
            };

            // RTF has already put its own FlowDocument in the editor, and
            // rebuilding it from the walked IR would throw away whatever WPF's
            // converter kept that the IR has no room for.
            if (!IsRtf(path))
            {
                Editor.Editor.Document = DocumentIo.ToFlowDocument(opened.Document, _styles);
            }

            Reset(session);

            ShowOpenWarnings(opened.Warnings);
            ShowMissingFonts(opened.Document.FontsUsed);
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
    private async Task SaveAsync(bool saveAs)
    {
        if (_busy)
        {
            return;
        }

        var path = _session.FilePath;

        if (saveAs || path is null)
        {
            path = AskWhereToSave(path);

            if (path is null)
            {
                return;
            }
        }

        var document = DocumentIo.ToIr(Editor.Editor.Document, _session, _styles);

        // The lossy-format check (Section 7.1), on what this document actually
        // uses rather than on the format in general.
        var decided = ConfirmLossyFormat(document, path);

        if (decided is null)
        {
            return;
        }

        path = decided;

        if (!ConfirmOverwriteIfChangedOnDisk(path))
        {
            return;
        }

        _busy = true;

        try
        {
            Status.ShowMessage($"Saving {Path.GetFileName(path)}...", transient: false);

            if (IsRtf(path))
            {
                WriteRtf(path);
            }
            else
            {
                await DocumentIo
                    .WriteAsync(document, path, _styles, _settings.KeepBakFile, CancellationToken.None)
                    .ConfigureAwait(true);
            }

            _session.FilePath = path;
            _session.LastSavedIr = document;
            _session.OpenedStamp = FileOps.Stamp.Of(path);
            _session.IsDirty = false;

            UpdateTitle();
            RecentFiles.Add(path);

            Status.ShowMessage($"Saved {Path.GetFileName(path)}.");
        }
        catch (FileOps.SaveException ex)
        {
            Status.ShowMessage(ex.Message, transient: false);
            MessageBox.Show(this, ex.Message, "Save", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            Log.Error($"saving {path} failed", ex);

            var message = $"{Path.GetFileName(path)} could not be saved.";

            Status.ShowMessage(message, transient: false);
            MessageBox.Show(this, message, "Save", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _busy = false;
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
    private DocumentIo.Opened ReadRtf(string path)
    {
        var stamp = FileOps.Stamp.Of(path);
        var flow = new System.Windows.Documents.FlowDocument();

        using (var stream = File.OpenRead(path))
        {
            new Formats.RtfFormat(_styles).Load(flow, stream);
        }

        Editor.Editor.Document = flow;

        // Walked for the save warning, the recovery file and the font list;
        // the editor already has the document itself.
        var document = Bridge.FlowWalker.Walk(flow, _styles);

        return new DocumentIo.Opened(
            document,
            ["Paragraph style names are not stored in RTF, so headings were matched by their formatting."],
            stamp);
    }

    private void WriteRtf(string path)
    {
        var rtf = new Formats.RtfFormat(_styles);

        FileOps.AtomicWrite(
            path,
            stream => rtf.Save(Editor.Editor.Document, stream),
            _settings.KeepBakFile);
    }

    private string? AskWhereToSave(string? current)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Save As",
            Filter = FormatRegistry.SaveFilter(),
            AddExtension = true,
            OverwritePrompt = true,
            FileName = current is null ? _session.Title : Path.GetFileName(current),
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
    private bool ConfirmOverwriteIfChangedOnDisk(string path)
    {
        if (!string.Equals(path, _session.FilePath, StringComparison.OrdinalIgnoreCase))
        {
            return true;        // a different file; the dialog already asked
        }

        if (!FileOps.ChangedOnDisk(path, _session.OpenedStamp))
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

    /// <summary>
    /// Asks about unsaved changes before they would be lost.
    /// </summary>
    private bool ConfirmDiscard()
    {
        if (!_session.IsDirty)
        {
            return true;
        }

        var answer = MessageBox.Show(
            this,
            $"{_session.Title} has changes you have not saved.\n\nDiscard them?",
            "Scratchpad Lite",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        return answer == MessageBoxResult.Yes;
    }

    private void Reset(DocumentSession session)
    {
        _session.FilePath = session.FilePath;
        _session.Meta = session.Meta;
        _session.Page = session.Page;
        _session.OpenedStamp = session.OpenedStamp;
        _session.OpenWarnings = session.OpenWarnings;
        _session.FontsUsed = session.FontsUsed;
        _session.LastSavedIr = session.LastSavedIr;
        _session.IsDirty = false;

        // The document just arrived, so the undo history belongs to a document
        // that is no longer open.
        Editor.Editor.Document.Blocks.FirstBlock?.ContentStart.Paragraph?.ToString();
        Editor.Editor.IsUndoEnabled = false;
        Editor.Editor.IsUndoEnabled = true;

        UpdateTitle();
        Editor.ReattachToNewDocument(_settings, _styles);

        Editor.Editor.Focus();
    }

    private void UpdateTitle() =>
        Title = $"{(_session.IsDirty ? "*" : "")}{_session.Title} — Scratchpad Lite";

    private void OnDocumentChanged()
    {
        if (_session.IsDirty)
        {
            return;
        }

        _session.IsDirty = true;
        UpdateTitle();
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
