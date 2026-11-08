using ScratchpadLite.Core.Platform;

namespace ScratchpadLite.App.Services;

/// <summary>
/// One running copy owns the window (Section 6.15). A second launch finds
/// the first through a named mutex, sends it the file paths over a named
/// pipe, and exits.
///
/// This is the very first thing that happens at startup (Section 4.3 step
/// 1), so it has to be cheap: a mutex and, only when another copy exists, a
/// short pipe write.
/// </summary>
/// <remarks>Phase 0.</remarks>
internal static class SingleInstance
{
    /// <summary>True when this process is the first copy and should show a window.</summary>
    internal static bool Claim() => throw new NotImplementedException("Phase 0");

    /// <summary>Hands the file paths to the running copy, then the caller exits.</summary>
    internal static void ForwardFiles(IReadOnlyList<string> files) =>
        throw new NotImplementedException("Phase 0");

    /// <summary>Starts the pipe listener. Raised on the UI thread for each delivery.</summary>
    internal static void StartListening(Action<IReadOnlyList<string>> onFilesReceived) =>
        throw new NotImplementedException("Phase 0");
}
