namespace ScratchpadLite.App.Services;

/// <summary>
/// The File, Open Recent list, kept in the data folder
/// (Section 5.2). Entries that no longer exist are shown greyed and removed
/// when clicked.
/// </summary>
/// <remarks>Phase 4.</remarks>
internal static class RecentFiles
{
    internal static IReadOnlyList<string> Load(int max) => throw new NotImplementedException("Phase 4");

    internal static void Add(string path) => throw new NotImplementedException("Phase 4");
}
