using System.IO;

namespace ScratchpadLite.App.Services;

/// <summary>
/// Safe saving (Section 6.14). The new content goes to a temporary file in
/// the same folder, is flushed to disk, and only then replaces the original
/// through a replace that keeps the old copy as a bak file when that setting
/// is on. An interrupted save therefore never leaves a half-written
/// document.
///
/// Read-only files, files on a disconnected network share and files another
/// program has locked each produce a plain sentence with a Details expander
/// (Section 9), not an exception dialog.
/// </summary>
/// <remarks>Phase 4.</remarks>
internal static class FileOps
{
    internal static void AtomicWrite(string path, Action<Stream> writeContent, bool keepBak) =>
        throw new NotImplementedException("Phase 4");
}
