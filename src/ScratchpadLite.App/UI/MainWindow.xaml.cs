using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ScratchpadLite.App.Editor;
using ScratchpadLite.App.Services;
using ScratchpadLite.App.UI.Themes;
using ScratchpadLite.Core.Model;
using ScratchpadLite.Core.Platform;

namespace ScratchpadLite.App.UI;

/// <summary>
/// The application window: the Section 8.1 menus, the toolbars, the status bar,
/// and one editor.
/// </summary>
/// <remarks>
/// This class is wiring, not logic. Formatting lives in
/// <see cref="FormatCommands"/>, shortcuts in <see cref="Commands"/>, and the
/// editor's own behaviour in <see cref="EditorView"/>; what happens here is
/// connecting them and keeping the settings file in step.
/// <para>
/// Phase 7 replaces the single editor with the tab control, at which point the
/// per-document wiring moves to the tab manager.
/// </para>
/// </remarks>
public partial class MainWindow : Window
{
    private readonly CommandLineOptions _options;
    private readonly StyleSheet _styles;

    private Settings _settings;

    public MainWindow(Settings settings, CommandLineOptions options)
    {
        _settings = settings;
        _options = options;

        InitializeComponent();

        // styles.json is written on first run so it can be edited, then read
        // back - so a user's edits win over the built-in sheet (Section 6.5).
        StyleSheet.WriteDefaultIfMissing(AppPaths.StylesFile);
        _styles = StyleSheet.LoadOrDefault(AppPaths.StylesFile);

        RestorePlacement();
        BindCommands();
        RouteMenuCommandsToEditor();
        BuildParagraphStyleMenu();

        FormatToolbars.Target = Editor;
        FormatToolbars.LoadStyles(_styles);
        FormatToolbars.PreviewFontNamesInOwnFont = _settings.PreviewFontNamesInOwnFont;

        Editor.ApplyDefaults(_settings, _styles);
        Editor.FormatStateChanged += OnFormatStateChanged;
        Editor.CountsChanged += OnCountsChanged;

        PasteFilter.Attach(Editor.Editor);

        Status.ZoomChanged += (_, zoom) => Editor.Zoom = zoom;
        Status.ShowZoomWithoutRaising(_settings.Zoom);
        Status.ShowLanguage(_settings.SpellCheckLanguage);
        Status.Visibility = _settings.ShowStatusBar ? Visibility.Visible : Visibility.Collapsed;

        ThemeManager.ThemeChanged += (_, dark) => DarkTitleBar.Apply(this, dark);

        // The title bar attribute needs a window handle, which does not exist
        // until the window is sourced, so the theme applied during startup has
        // to be re-applied here.
        SourceInitialized += (_, _) => DarkTitleBar.Apply(this, ThemeManager.IsDark);

        SyncViewMenu();

        ContentRendered += OnContentRendered;
        Closing += OnClosing;

        Log.Info(
            $"window created (portable: {AppPaths.IsPortable}, "
            + $"files on command line: {_options.Files.Count})");
    }

    /// <summary>
    /// Restores the saved window position, but only when it lands on a monitor
    /// that is connected now (Section 6.2). A window restored onto a monitor
    /// that has since been unplugged is invisible and feels like a crash.
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

    // ------------------------------------------------------------ commands --

    private void Bind(RoutedUICommand command, Action<object?> execute, Func<bool>? canExecute = null) =>
        CommandBindings.Add(new CommandBinding(
            command,
            (_, e) =>
            {
                execute(e.Parameter);
                e.Handled = true;
            },
            (_, e) =>
            {
                e.CanExecute = canExecute?.Invoke() ?? true;
                e.Handled = true;
            }));

    private void BindCommands()
    {
        var rtb = Editor.Editor;

        // ---- File ----
        Bind(Commands.Exit, _ => Close());
        Bind(Commands.OpenFontFolder, _ => OpenFontFolder());

        // ---- Edit ----
        Bind(Commands.PastePlainText, _ => PasteFilter.PastePlainText(rtb));

        // ---- Format: characters ----
        Bind(Commands.Strikethrough, _ => FormatCommands.ToggleStrikethrough(rtb));
        Bind(Commands.SmallCaps, _ => FormatCommands.ToggleSmallCaps(rtb));
        Bind(Commands.Superscript, _ => FormatCommands.SetVerticalAlignment(rtb, VAlign.Super));
        Bind(Commands.Subscript, _ => FormatCommands.SetVerticalAlignment(rtb, VAlign.Sub));
        Bind(Commands.GrowFont, _ => FormatCommands.GrowFont(rtb));
        Bind(Commands.ShrinkFont, _ => FormatCommands.ShrinkFont(rtb));

        Bind(Commands.SetUnderline, p =>
        {
            // A null parameter is the Ctrl+U gesture: toggle a plain single
            // underline. A named kind comes from the Format menu.
            if (p is null)
            {
                var kind = Bridge.SpProps.ReadUnderline(rtb.Selection) ?? UnderlineKind.None;

                FormatCommands.SetUnderline(
                    rtb, kind == UnderlineKind.None ? UnderlineKind.Single : UnderlineKind.None);
            }
            else if (Parse<UnderlineKind>(p) is { } named)
            {
                FormatCommands.SetUnderline(rtb, named);
            }
        });

        Bind(Commands.ChangeCase, p =>
        {
            if (Parse<CaseMode>(p) is { } mode)
            {
                FormatCommands.ChangeCase(rtb, mode);
            }
        });

        // ---- Format: paragraphs ----
        Bind(Commands.SetParagraphStyle, p =>
        {
            if (p is string name)
            {
                FormatCommands.SetStyle(rtb, name, _styles);
            }
        });

        Bind(Commands.StyleNormal, _ => FormatCommands.SetStyle(rtb, "Normal", _styles));
        for (var level = 1; level <= 6; level++)
        {
            var name = $"Heading {level}";
            var command = level switch
            {
                1 => Commands.StyleHeading1,
                2 => Commands.StyleHeading2,
                3 => Commands.StyleHeading3,
                4 => Commands.StyleHeading4,
                5 => Commands.StyleHeading5,
                _ => Commands.StyleHeading6,
            };

            Bind(command, _ => FormatCommands.SetStyle(rtb, name, _styles));
        }

        Bind(Commands.SetLineSpacing, p =>
        {
            if (ParseDouble(p) is { } multiple)
            {
                FormatCommands.SetLineSpacing(rtb, multiple);
            }
        });

        Bind(Commands.ClearFormatting, _ => FormatCommands.ClearFormatting(rtb, _styles));
        Bind(Commands.FormatPainter, _ => FormatToolbars.PickFormatPainter());

        // ---- Insert ----
        Bind(Commands.InsertDateAndTime, _ =>
            rtb.CaretPosition.InsertTextInRun(
                DateTime.Now.ToString("d MMMM yyyy HH:mm", CultureInfo.CurrentCulture)));

        // ---- Tools ----
        Bind(Commands.WordCount, _ => ShowWordCount());

        // ---- View ----
        Bind(Commands.SetTheme, p =>
        {
            if (Parse<ThemeChoice>(p) is { } choice)
            {
                _settings = _settings with { Theme = choice };
                ThemeManager.Apply(choice);
                SyncViewMenu();
            }
        });

        Bind(Commands.ToggleDarkPaper, _ =>
        {
            _settings = _settings with { DarkPaper = !_settings.DarkPaper };
            Editor.DarkPaper = _settings.DarkPaper;
            SyncViewMenu();
        });

        Bind(Commands.SetPageView, p =>
        {
            if (Parse<PageViewMode>(p) is { } mode)
            {
                _settings = _settings with { PageView = mode };
                Editor.PageView = mode;
                SyncViewMenu();
            }
        });

        Bind(Commands.SetZoom, p =>
        {
            if (ParseDouble(p) is { } zoom)
            {
                Editor.Zoom = zoom;
                Status.ShowZoomWithoutRaising(Editor.Zoom);
            }
        });

        Bind(Commands.ResetZoom, _ =>
        {
            Editor.Zoom = 1.0;
            Status.ShowZoomWithoutRaising(Editor.Zoom);
        });

        Bind(Commands.ToggleStatusBar, _ =>
        {
            _settings = _settings with { ShowStatusBar = !_settings.ShowStatusBar };
            Status.Visibility = _settings.ShowStatusBar ? Visibility.Visible : Visibility.Collapsed;
        });

        // ---- Help ----
        Bind(Commands.UserGuide, _ => OpenDocument("USER_GUIDE.md"));
        Bind(Commands.ThirdPartyLicenses, _ => OpenDocument("THIRD_PARTY_LICENSES.txt"));
        Bind(Commands.KeyboardShortcuts, _ => ShowKeyboardShortcuts());
        Bind(Commands.About, _ => ShowAbout());

        // Escape cancels a sticky format painter (Section 6.8).
        InputBindings.Add(new KeyBinding(
            new RelayCommand(_ => FormatToolbars.CancelFormatPainter()), Key.Escape, ModifierKeys.None));

        // Every command's gesture becomes a window-level binding, so a shortcut
        // works wherever the focus happens to be.
        foreach (var command in Commands.All)
        {
            foreach (var gesture in command.InputGestures.OfType<KeyGesture>())
            {
                InputBindings.Add(new KeyBinding(command, gesture));
            }
        }
    }

    /// <summary>
    /// Points every menu item at the editor, so a command raised from the menu
    /// reaches the RichTextBox rather than dying at the menu, which is where the
    /// focus is when the item is clicked. Our own commands are handled by this
    /// window's bindings and still reach them, because routing starts at the
    /// target and bubbles up.
    /// </summary>
    private void RouteMenuCommandsToEditor()
    {
        foreach (var item in Descendants(MainMenu))
        {
            item.CommandTarget = Editor.Editor;
        }
    }

    private static IEnumerable<MenuItem> Descendants(ItemsControl parent)
    {
        foreach (var child in parent.Items.OfType<MenuItem>())
        {
            yield return child;

            foreach (var grandChild in Descendants(child))
            {
                yield return grandChild;
            }
        }
    }

    /// <summary>
    /// Fills Format, Paragraph Style from the style sheet, so a style added to
    /// styles.json appears in the menu without a code change.
    /// </summary>
    private void BuildParagraphStyleMenu()
    {
        foreach (var style in _styles.All)
        {
            ParagraphStyleMenu.Items.Add(new MenuItem
            {
                Header = style.Name,
                Command = Commands.SetParagraphStyle,
                CommandParameter = style.Name,
                CommandTarget = Editor.Editor,
                InputGestureText = ShortcutFor(style.Name),
            });
        }
    }

    private static string ShortcutFor(string styleName) => styleName switch
    {
        "Normal" => "Ctrl+Alt+0",
        "Heading 1" => "Ctrl+Alt+1",
        "Heading 2" => "Ctrl+Alt+2",
        "Heading 3" => "Ctrl+Alt+3",
        "Heading 4" => "Ctrl+Alt+4",
        "Heading 5" => "Ctrl+Alt+5",
        "Heading 6" => "Ctrl+Alt+6",
        _ => string.Empty,
    };

    private static T? Parse<T>(object? parameter) where T : struct, Enum =>
        parameter switch
        {
            T already => already,
            string text when Enum.TryParse<T>(text, ignoreCase: true, out var parsed) => parsed,
            _ => null,
        };

    private static double? ParseDouble(object? parameter) =>
        parameter switch
        {
            double already => already,
            string text when double.TryParse(
                text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => null,
        };

    // -------------------------------------------------------------- events --

    private void OnContentRendered(object? sender, EventArgs e)
    {
        ContentRendered -= OnContentRendered;

        if (StartupTimer.ElapsedMs is { } ms)
        {
            Status.ShowMessage($"startup: {ms:F0} ms");
        }

        // Only shown when the program folder could not be written to and the
        // data folder fell back to LocalAppData (Section 6.1).
        if (!AppPaths.IsPortable)
        {
            Status.ShowMessage(AppPaths.NonPortableNotice, transient: false);
        }

        Editor.Editor.Focus();
    }

    private void OnFormatStateChanged(object? sender, FormatState state)
    {
        FormatToolbars.UpdateFrom(state);
        Status.UpdateFrom(state);

        // A loaded format painter paints the next selection the user makes.
        FormatToolbars.PaintIfArmed();
    }

    private void OnCountsChanged(object? sender, (int Words, int Characters, int? SelectedWords) counts) =>
        Status.ShowCounts(counts.Words, counts.Characters, counts.SelectedWords);

    private void SyncViewMenu()
    {
        ThemeLightItem.IsChecked = _settings.Theme == ThemeChoice.Light;
        ThemeDarkItem.IsChecked = _settings.Theme == ThemeChoice.Dark;
        ThemeFollowItem.IsChecked = _settings.Theme == ThemeChoice.FollowWindows;

        DarkPaperItem.IsChecked = _settings.DarkPaper;
        PageWidthItem.IsChecked = _settings.PageView == PageViewMode.PageWidth;
        FullWidthItem.IsChecked = _settings.PageView == PageViewMode.FullWidth;
        StatusBarItemToggle.IsChecked = _settings.ShowStatusBar;
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        // The window's own geometry and the view settings are saved here; the
        // App saves the file on exit (Section 6.2).
        var restoreBounds = WindowState == WindowState.Normal
            ? new Rect(Left, Top, Width, Height)
            : RestoreBounds;

        _settings = _settings with
        {
            WindowLeft = restoreBounds.Left,
            WindowTop = restoreBounds.Top,
            WindowWidth = restoreBounds.Width,
            WindowHeight = restoreBounds.Height,
            WindowMaximized = WindowState == WindowState.Maximized,
            Zoom = Editor.Zoom,
        };

        PasteFilter.Detach(Editor.Editor);
    }

    /// <summary>
    /// The settings as the window last had them, for <c>App.OnExit</c> to save.
    /// The window is where theme, zoom, page view and geometry actually change,
    /// so the App has to read them back from here rather than saving the copy
    /// it loaded at startup.
    /// </summary>
    internal Settings CurrentSettings => _settings;

    // ------------------------------------------------------------- dialogs --

    private static void OpenFontFolder()
    {
        // Opening the folder is also what creates it on a first run, and it
        // never touches what is already in there (Section 6.1).
        var dir = AppPaths.FontDir;
        Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true });
    }

    /// <summary>
    /// Opens one of the documents that ship beside the .exe. Phase 9 writes
    /// these into the dist folder; in a development build they may not be there,
    /// and saying so is better than nothing happening.
    /// </summary>
    private void OpenDocument(string fileName)
    {
        var path = Path.Combine(AppPaths.ProgramDir, fileName);

        if (!File.Exists(path))
        {
            MessageBox.Show(
                this,
                $"{fileName} is not next to the program. It ships with the released copy.",
                "Scratchpad Lite",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    private void ShowWordCount()
    {
        var document = Editor.Editor.Document;
        var text = new System.Windows.Documents.TextRange(
            document.ContentStart, document.ContentEnd).Text;

        var words = EditorView.CountWords(text);
        var characters = text.Replace("\r", string.Empty).Replace("\n", string.Empty).Length;
        var paragraphs = document.Blocks.Count;

        MessageBox.Show(
            this,
            $"""
             Words: {words:N0}
             Characters: {characters:N0}
             Paragraphs: {paragraphs:N0}
             """,
            "Word Count",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    /// <summary>
    /// The shortcut list, built from <see cref="Commands.All"/> rather than
    /// written out again, so it cannot drift from what the keys actually do.
    /// </summary>
    private void ShowKeyboardShortcuts()
    {
        var lines = Commands.All
            .Select(c => (c.Text, Gesture: c.InputGestures.OfType<KeyGesture>().FirstOrDefault()))
            .Where(x => x.Gesture is not null)
            .Select(x => $"{x.Gesture!.GetDisplayStringForCulture(CultureInfo.CurrentCulture),-18}{x.Text}")
            .OrderBy(x => x, StringComparer.CurrentCulture);

        MessageBox.Show(
            this,
            string.Join(Environment.NewLine, lines),
            "Keyboard Shortcuts",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void ShowAbout()
    {
        var version = Assembly.GetExecutingAssembly()
                          .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                          ?.InformationalVersion
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

/// <summary>
/// A one-line <see cref="ICommand"/> for the few places a key binding needs an
/// action rather than one of the <see cref="Commands"/> entries.
/// </summary>
internal sealed class RelayCommand(Action<object?> execute) : ICommand
{
    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => true;

    public void Execute(object? parameter) => execute(parameter);

    internal void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
