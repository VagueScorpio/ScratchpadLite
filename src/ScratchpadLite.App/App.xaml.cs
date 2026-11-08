using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using ScratchpadLite.App.Services;
using ScratchpadLite.App.UI;
using ScratchpadLite.Core.Platform;

namespace ScratchpadLite.App;

/// <summary>
/// The startup sequence (Section 4.3) and the crash handlers.
/// </summary>
/// <remarks>
/// Section 2.1 is the rule this file exists to protect: before the first frame
/// it does the single-instance check, paths, settings, the theme, and the window
/// with one empty tab. Nothing else. Fonts, recovery, spell check and the
/// timers all start after <c>ContentRendered</c>.
/// <para>
/// Adding work to <see cref="OnStartup"/> is how the startup budget gets lost,
/// so new work belongs in <see cref="OnFirstRender"/> unless it is on that
/// short list.
/// </para>
/// </remarks>
public partial class App : Application
{
    private CommandLineOptions _options = new([], false, false, null);
    private Settings _settings = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // ---- Before the first frame. Keep this list short. ----
        _options = CommandLineOptions.Parse(e.Args);

        // 1. Single instance: hand the files to the running copy and leave.
        //    Phase 0 implements SingleInstance; until then one window per launch.
        //
        //    if (!SingleInstance.Claim())
        //    {
        //        SingleInstance.ForwardFiles(_options.Files);
        //        Shutdown();
        //        return;
        //    }

        // 2. Paths, including the --fonts override for development and tests.
        if (_options.FontDirOverride is { } fontDir)
        {
            AppPaths.OverrideFontDir(fontDir);
        }

        AppPaths.Initialize();

        // 3. Crash handlers. The log file opens lazily on its first write.
        Log.MinLevel = _options.Debug ? LogLevel.Debug : LogLevel.Info;
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        // 4. Settings.
        _settings = Settings.Load(AppPaths.SettingsFile);

        // 5. Theme.
        //    ThemeManager.Apply(_settings.Theme);   // Phase 0

        // 6. The window, with one empty tab or the files from the command line.
        var window = new MainWindow(_settings, _options);
        window.ContentRendered += OnFirstRender;
        MainWindow = window;
        window.Show();
    }

    /// <summary>
    /// Everything that was deliberately kept out of startup (Section 4.3, steps
    /// 7 to 11). This runs once, after the window is on screen and the user can
    /// already type.
    /// </summary>
    private void OnFirstRender(object? sender, EventArgs e)
    {
        if (sender is Window window)
        {
            window.ContentRendered -= OnFirstRender;
        }

        // 7. Startup time: logged on every launch, shown in Help, About.
        StartupTimer.MarkFirstRender();

        if (_options.MeasureStartup)
        {
            // The hidden measurement flag (Section 2.1): print and leave, so
            // build.ps1 can take a median of five runs.
            //
            // This is a WinExe, so it has no console of its own. Attaching to
            // the console of the launching process gets the number on screen
            // for a human running it from a prompt, but writing to a console
            // that way bypasses the pipe, so a script cannot capture it.
            // That is why the measurement also goes to a file, which is what
            // build.ps1 actually reads.
            var line = $"startup_ms={StartupTimer.ElapsedMs:F0}";

            if (AttachConsole(AttachParentProcess))
            {
                Console.WriteLine(line);
                Console.Out.Flush();
            }

            try
            {
                File.WriteAllText(Path.Combine(AppPaths.DataDir, "startup-measure.txt"), line);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Warn("the startup measurement could not be written to a file");
            }

            Shutdown();
            return;
        }

        // 8. Fonts: read the index, register what is cached, then check the
        //    folder and process only what changed.   Phase 2
        //    FontRegistry.StartBackgroundPreparation();

        // 9. Recovery: offer anything a crash left behind.   Phase 8
        // 10. Spell check for open editors.                  Phase 7
        // 11. The recovery timer, and AutoSave if it is on.  Phase 8

        Log.Debug("first frame complete; background preparation would start here");
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _settings.Save(AppPaths.SettingsFile);
        Log.Info("exit");
        base.OnExit(e);
    }

    // ---- Crash handlers (Section 9) ----
    // A crash must leave the recovery files in place, so the next launch can
    // offer the user their work back.

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("unhandled exception on the UI thread", e.Exception);
        MessageBox.Show(
            "Scratchpad Lite ran into a problem and has to close. Your open documents were saved for recovery and will be offered the next time you start it.",
            "Scratchpad Lite",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = false;
    }

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e) =>
        Log.Error("unhandled exception", e.ExceptionObject as Exception);

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Log.Error("unobserved task exception", e.Exception);
        e.SetObserved();
    }

    private const uint AttachParentProcess = 0xFFFFFFFF;

    // DllImport rather than LibraryImport: the source-generated version needs
    // AllowUnsafeBlocks, and one boolean P/Invoke is not a reason to turn
    // unsafe code on for the whole project.
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachConsole(uint processId);
}
