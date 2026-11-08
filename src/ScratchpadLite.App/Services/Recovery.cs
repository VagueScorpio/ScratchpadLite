namespace ScratchpadLite.App.Services;

/// <summary>
/// Crash recovery, always on and never touching the real file
/// (Section 6.13.1). Every open document is written to its own file in the
/// data recovery folder on a timer, as IR JSON. A clean exit deletes them.
/// Anything left over means the last run did not finish, so the Recovery
/// dialog offers those documents on the next launch.
///
/// This is separate from AutoSave, which does write the real file.
/// The check runs after the first frame (Section 4.3 step 9).
/// </summary>
/// <remarks>Phase 8.</remarks>
internal sealed class Recovery
{
    internal void Start(int intervalSeconds) => throw new NotImplementedException("Phase 8");

    internal IReadOnlyList<string> FindLeftovers() => throw new NotImplementedException("Phase 8");

    internal void ClearOnCleanExit() => throw new NotImplementedException("Phase 8");
}
