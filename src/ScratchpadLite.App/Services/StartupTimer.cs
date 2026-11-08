using System.Diagnostics;
using ScratchpadLite.Core.Platform;

namespace ScratchpadLite.App.Services;

/// <summary>
/// Measures process start to the first ContentRendered event (Section 2.1),
/// logs it on every launch, and keeps the value for Help, About.
///
/// With --measure-startup the app prints the time to stdout and the log and
/// exits, which is what build.ps1 runs five times to report a median.
/// </summary>
/// <remarks>Phase 0.</remarks>
internal static class StartupTimer
{
    /// <summary>Milliseconds from process start to the first render, once measured.</summary>
    internal static double? ElapsedMs { get; private set; }

    /// <summary>Called from the first ContentRendered. Uses the real process start time.</summary>
    internal static void MarkFirstRender()
    {
        if (ElapsedMs is not null)
        {
            return;
        }

        using var process = Process.GetCurrentProcess();
        ElapsedMs = (DateTime.Now - process.StartTime).TotalMilliseconds;
        Log.Info($"startup: {ElapsedMs:F0} ms");
    }
}
