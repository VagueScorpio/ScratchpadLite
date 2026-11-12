using System.Windows;
using System.Windows.Controls;
using ScratchpadLite.App.Editor;

namespace ScratchpadLite.App.Services;

/// <summary>
/// Several documents open at once (Section 6.12): new, close with a save
/// prompt, reorder, next and previous tab, and the window title that tracks
/// the active document.
///
/// Speed rule (Section 2.1): when the last session is restored, only the
/// active tab loads its file. The others show their names and load when
/// they are first clicked.
/// </summary>
/// <remarks>
/// The one design decision in here is the one Section 6.12 insists on, and it
/// is worth saying why. A <c>TabControl</c> keeps one content presenter and
/// re-creates the content of a tab every time you switch to it. For a
/// RichTextBox that means a new control, which means a new undo history and a
/// scroll position back at the top - so switching tabs would throw away the
/// user's ability to undo, silently, and only in the built application.
/// <para>
/// So every editor is made once and kept alive in one <see cref="Panel"/>, and
/// switching tabs only changes which one is visible. The tab strip is separate
/// and holds nothing but headers.
/// </para>
/// </remarks>
internal sealed class TabManager
{
    private readonly Panel _host;
    private readonly Func<EditorView> _newView;
    private readonly List<DocumentSession> _tabs = [];

    private DocumentSession? _active;

    internal TabManager(Panel host, Func<EditorView> newView)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(newView);

        _host = host;
        _newView = newView;
    }

    /// <summary>Every open document, in the order their tabs are shown.</summary>
    internal IReadOnlyList<DocumentSession> Tabs => _tabs;

    /// <summary>The document being edited, or null before the first tab.</summary>
    internal DocumentSession? Active => _active;

    /// <summary>Raised when a different tab becomes the active one.</summary>
    internal event EventHandler<DocumentSession>? ActiveChanged;

    /// <summary>Raised when a tab is added, removed, reordered or renamed.</summary>
    internal event EventHandler? TabsChanged;

    /// <summary>
    /// Raised the first time a tab's editor is built, so the window can wire it
    /// up the way it wires the first one.
    /// </summary>
    internal event EventHandler<DocumentSession>? ViewCreated;

    /// <summary>
    /// Adds a tab. <paramref name="pendingPath"/> names a file the tab stands
    /// for but will not read until it is first shown (Section 6.12).
    /// </summary>
    internal DocumentSession Add(string? pendingPath = null)
    {
        var session = new DocumentSession { PendingPath = pendingPath };

        if (pendingPath is not null)
        {
            // The header has to read as the file from the moment the tab
            // appears, which is the whole point of a tab that has not loaded.
            session.FilePath = pendingPath;
        }

        _tabs.Add(session);
        TabsChanged?.Invoke(this, EventArgs.Empty);

        return session;
    }

    /// <summary>
    /// Shows a tab, building its editor the first time (Section 2.1).
    /// </summary>
    internal void Activate(DocumentSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (!_tabs.Contains(session))
        {
            return;
        }

        var view = ViewFor(session);

        foreach (var other in _tabs)
        {
            if (other.View is { } existing)
            {
                existing.Visibility = ReferenceEquals(other, session)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
        }

        var changed = !ReferenceEquals(_active, session);
        _active = session;

        if (changed)
        {
            ActiveChanged?.Invoke(this, session);
        }

        view.Editor.Focus();
    }

    /// <summary>
    /// This tab's editor, built on demand. The build is the expensive part, so
    /// it happens here and nowhere else.
    /// </summary>
    internal EditorView ViewFor(DocumentSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (session.View is { } already)
        {
            return already;
        }

        var view = _newView();

        view.Visibility = Visibility.Collapsed;
        session.View = view;
        _host.Children.Add(view);

        ViewCreated?.Invoke(this, session);

        return view;
    }

    /// <summary>
    /// Closes a tab. The caller has already asked about unsaved changes - this
    /// does the removing, not the deciding.
    /// </summary>
    /// <returns>The tab that is active afterwards, or null when none is left.</returns>
    internal DocumentSession? Remove(DocumentSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        var index = _tabs.IndexOf(session);

        if (index < 0)
        {
            return _active;
        }

        _tabs.RemoveAt(index);

        if (session.View is { } view)
        {
            // Taken out of the tree as well as out of the list: a kept editor
            // is the point of this class, and a kept editor for a closed
            // document would be a leak of the whole document.
            PasteFilter.Detach(view.Editor);
            _host.Children.Remove(view);
            session.View = null;
        }

        session.Document = null;

        TabsChanged?.Invoke(this, EventArgs.Empty);

        if (!ReferenceEquals(_active, session))
        {
            return _active;
        }

        _active = null;

        if (_tabs.Count == 0)
        {
            return null;
        }

        // The tab to its right, or the last one when it was the rightmost -
        // which is where the eye already is.
        Activate(_tabs[Math.Min(index, _tabs.Count - 1)]);

        return _active;
    }

    /// <summary>Drag reorder (Section 6.12).</summary>
    internal void Move(int from, int to)
    {
        if (from == to
            || from < 0 || from >= _tabs.Count
            || to < 0 || to >= _tabs.Count)
        {
            return;
        }

        var moved = _tabs[from];

        _tabs.RemoveAt(from);
        _tabs.Insert(to, moved);

        TabsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// The tab already showing this file, so opening it again switches rather
    /// than opening a second copy (Section 6.12).
    /// </summary>
    internal DocumentSession? ForPath(string path) =>
        _tabs.FirstOrDefault(
            t => t.FilePath is { } open && string.Equals(open, path, StringComparison.OrdinalIgnoreCase));

    /// <summary>Ctrl+Tab and Ctrl+Shift+Tab, wrapping round at both ends.</summary>
    internal void Cycle(bool backwards)
    {
        if (_tabs.Count < 2 || _active is null)
        {
            return;
        }

        var index = _tabs.IndexOf(_active);
        var next = ((index + (backwards ? -1 : 1)) + _tabs.Count) % _tabs.Count;

        Activate(_tabs[next]);
    }

    /// <summary>Tells the strip a header's text or dirty mark has changed.</summary>
    internal void NotifyChanged() => TabsChanged?.Invoke(this, EventArgs.Empty);

    /// <summary>Every tab with unsaved changes, for the dialog on exit (Section 6.12).</summary>
    internal IReadOnlyList<DocumentSession> Modified => [.._tabs.Where(t => t.IsDirty)];
}
