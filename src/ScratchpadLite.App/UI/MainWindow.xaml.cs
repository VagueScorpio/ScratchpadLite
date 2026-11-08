using System.Diagnostics;
using System.Reflection;
using System.Windows;
using ScratchpadLite.App.Services;
using ScratchpadLite.Core.Platform;

namespace ScratchpadLite.App.UI;

/// <summary>
/// The application window. In Phase 0 it is the shell: the full Section 8.1
/// menu structure, a status bar, and the one-time portable-mode notice.
/// </summary>
/// <remarks>
/// Phase 1 replaces <c>EditorHost</c> with the tab control and
/// <c>EditorView</c>, and <c>Commands.cs</c> takes over the menu wiring.
/// </remarks>
public partial class MainWindow : Window
{
    private readonly Settings _settings;
    private readonly CommandLineOptions _options;

    public MainWindow(Settings settings, CommandLineOptions options)
    {
        _settings = settings;
        _options = options;

        InitializeComponent();
        RestorePlacement();

        ContentRendered += OnContentRendered;
        Log.Info($"window created (portable: {AppPaths.IsPortable}, files on command line: {_options.Files.Count})");
    }

    /// <summary>
    /// Restores the saved window position, but only when it lands on a monitor
    /// that is connected now (Section 6.2).
    /// </summary>
    private void RestorePlacement()
    {
        if (_settings is { WindowWidth: { } w, WindowHeight: { } h } && w > 0 && h > 0)
        {
            Width = w;
            Height = h;
        }

        if (_settings is { WindowLeft: { } left, WindowTop: { } top }
            && left >= SystemParameters.VirtualScreenLeft
            && top >= SystemParameters.VirtualScreenTop
            && left < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth
            && top < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight)
        {
            Left = left;
            Top = top;
            WindowStartupLocation = WindowStartupLocation.Manual;
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        if (_settings.WindowMaximized)
        {
            WindowState = WindowState.Maximized;
        }
    }

    private void OnContentRendered(object? sender, EventArgs e)
    {
        ContentRendered -= OnContentRendered;

        if (StartupTimer.ElapsedMs is { } ms)
        {
            StartupText.Text = $"startup: {ms:F0} ms";
        }

        // The notice only appears when the program folder could not be written
        // to, so the data folder fell back to LocalAppData (Section 6.1).
        if (!AppPaths.IsPortable)
        {
            StatusText.Text = AppPaths.NonPortableNotice;
        }
    }

    private void OnExitClick(object sender, RoutedEventArgs e) => Close();

    private void OnOpenFontFolderClick(object sender, RoutedEventArgs e)
    {
        // Opening the folder in Explorer is also what creates it on a first run,
        // and it never touches what is already in there (Section 6.1).
        var dir = AppPaths.FontDir;
        Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true });
    }

    private void OnAboutClick(object sender, RoutedEventArgs e)
    {
        // Phase 8 replaces this with the About dialog. The version comes from
        // the assembly, which comes from Directory.Build.props (Section 0).
        var version = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? "unknown";

        var startup = StartupTimer.ElapsedMs is { } ms ? $"{ms:F0} ms" : "not measured";

        MessageBox.Show(
            this,
            $"""
             Scratchpad Lite {version}

             Last startup: {startup}
             Data folder: {AppPaths.DataDir}
             Font folder: {AppPaths.FontDir}
             Portable: {(AppPaths.IsPortable ? "yes" : "no")}
             """,
            "About Scratchpad Lite",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }
}
