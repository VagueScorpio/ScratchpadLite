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
/// <remarks>Phase 7.</remarks>
internal sealed class TabManager
{
    internal DocumentSession? Active => throw new NotImplementedException("Phase 7");

    internal DocumentSession NewTab() => throw new NotImplementedException("Phase 7");

    internal bool CloseTab(DocumentSession session) => throw new NotImplementedException("Phase 7");
}
