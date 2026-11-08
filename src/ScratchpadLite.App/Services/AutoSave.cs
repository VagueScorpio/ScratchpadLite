namespace ScratchpadLite.App.Services;

/// <summary>
/// AutoSave writes the real file (Section 6.13.2). It is off by default and
/// has a visible switch, because silently overwriting a file is not
/// something to turn on for someone.
///
/// It only ever saves a document that already has a path and is dirty, in
/// the format it was opened as, through the same atomic write and lossy
/// format check as a manual save. A document that has never been saved is
/// left alone: crash recovery already covers it.
/// </summary>
/// <remarks>Phase 8.</remarks>
internal sealed class AutoSave
{
    internal bool Enabled { get; set; }

    internal void Start(int intervalSeconds) => throw new NotImplementedException("Phase 8");

    internal void SaveNow(DocumentSession session) => throw new NotImplementedException("Phase 8");
}
