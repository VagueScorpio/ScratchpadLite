using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using ScratchpadLite.App.Editor;
using ScratchpadLite.App.Fonts;
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
/// Since Phase 7 there is one <see cref="EditorView"/> per tab rather than one
/// for the window, and <see cref="Editor"/> is whichever of them is showing.
/// That is why almost nothing in this class had to change: a command bound to
/// "the editor" now reaches the active one because the property is read when
/// the command runs, not when it was bound.
/// </para>
/// </remarks>
public partial class MainWindow : Window
{
    private readonly CommandLineOptions _options;
    private readonly StyleSheet _styles;
    private readonly TabManager _tabs;

    private Settings _settings;

    /// <summary>
    /// The character grid while it is open. It is not modal, so a second
    /// Insert, Special Character brings the one that is already there back to
    /// the front rather than opening another.
    /// </summary>
    private Dialogs.SpecialChars? _specialChars;

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

        _tabs = new TabManager(EditorHost, () => new EditorView());
        _tabs.ViewCreated += (_, session) => WireView(session);
        _tabs.ActiveChanged += (_, session) => OnActiveTabChanged(session);
        _tabs.TabsChanged += (_, _) => RefreshTabStrip();

        Tabs.Chosen += (_, session) => ActivateTab(session);
        Tabs.CloseRequested += (_, session) => _ = CloseTabAsync(session);
        Tabs.NewTabRequested += (_, _) => NewDocument();
        Tabs.Reordered += (_, move) => _tabs.Move(move.From, move.To);

        WireFindBar();

        FormatToolbars.LoadStyles(_styles);
        FormatToolbars.PreviewFontNamesInOwnFont = _settings.PreviewFontNamesInOwnFont;
        FormatToolbars.ShowRow1 = _settings.ShowToolbarRow1;
        FormatToolbars.ShowRow2 = _settings.ShowToolbarRow2;

        ToolbarRow1Item.Click += (_, _) =>
        {
            Apply(_settings with { ShowToolbarRow1 = !_settings.ShowToolbarRow1 });
            FormatToolbars.ShowRow1 = _settings.ShowToolbarRow1;
            SyncViewMenu();
        };

        ToolbarRow2Item.Click += (_, _) =>
        {
            Apply(_settings with { ShowToolbarRow2 = !_settings.ShowToolbarRow2 });
            FormatToolbars.ShowRow2 = _settings.ShowToolbarRow2;
            SyncViewMenu();
        };

        BindCommands();
        BuildParagraphStyleMenu();

        // The first tab, and with it the first editor. Everything above is
        // wiring that has to be in place before a tab exists, because
        // activating one raises the events that use it.
        _tabs.Activate(_tabs.Add());

        Status.ZoomChanged += (_, zoom) => Editor.Zoom = zoom;
        Status.AutoSaveToggled += (_, on) => SetAutoSave(on);
        AutoSaveMenuItem.Click += (_, _) => ToggleAutoSave();
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
    /// The editor showing now. Every command reads this when it runs, which is
    /// what makes one set of bindings work for every tab (Section 6.12).
    /// </summary>
    internal EditorView Editor => _tabs.ViewFor(_tabs.Active!);

    /// <summary>The active editor's RichTextBox, which is what the commands take.</summary>
    private RichTextBox Rtb => Editor.Editor;

    /// <summary>
    /// Every open document, in tab order (Section 6.12).
    /// </summary>
    /// <remarks>
    /// Phase 8 needs this: recovery writes one file per open document and
    /// AutoSave saves each modified one, and neither of them is a tab's
    /// business.
    /// </remarks>
    internal IReadOnlyList<DocumentSession> OpenDocuments => _tabs.Tabs;

    /// <summary>
    /// Everything a new tab's editor needs that belongs to the window rather
    /// than to the control. Called once per tab, the first time it is shown.
    /// </summary>
    private void WireView(DocumentSession session)
    {
        var view = session.View!;

        view.ApplyDefaults(_settings, _styles);

        view.FormatStateChanged += OnFormatStateChanged;
        view.CountsChanged += OnCountsChanged;
        view.DocumentChanged += (_, _) => OnDocumentEdited(session);
        view.Editor.ContextMenuOpening += OnEditorContextMenuOpening;

        PasteFilter.Attach(view.Editor);

        if (_spellCheckStarted)
        {
            ApplySpellCheck(view);
        }
    }

    /// <summary>
    /// A different tab is showing: point everything that holds "the editor" at
    /// the new one.
    /// </summary>
    private void OnActiveTabChanged(DocumentSession session)
    {
        FormatToolbars.Target = Editor;

        // The menus raise their commands at the editor, so the target has to
        // follow the tab or a menu item would act on the document that is no
        // longer visible.
        RouteMenuCommandsToEditor();

        UpdateTitle();
        RefreshTabStrip();

        Status.ShowFormat(session.FormatName);
        Status.ShowZoomWithoutRaising(Editor.Zoom);

        ShowOpenWarnings(session.OpenWarnings);

        // The matches belonged to the document that was showing.
        _matches = [];
        _matchesFor = null;

        RecountMatches();
    }

    /// <summary>A tab was clicked: show it, and read its file if it has not been read.</summary>
    private void ActivateTab(DocumentSession session)
    {
        _tabs.Activate(session);

        _ = LoadPendingAsync(session);
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

    /// <summary>
    /// Binds every command once, for the life of the window.
    /// </summary>
    /// <remarks>
    /// Each one reaches the editor through <see cref="Rtb"/>, which is a
    /// property and so is read when the command runs. That is the whole of what
    /// tabs needed from this method: a local variable captured here would have
    /// pinned every command to the first tab's editor for ever, and the symptom
    /// would have been formatting applied to an invisible document.
    /// </remarks>
    private void BindCommands()
    {
        // ---- File ----
        Bind(Commands.NewTab, _ => NewDocument());
        Bind(Commands.Open, _ => _ = OpenWithDialogAsync());
        Bind(Commands.Save, _ => _ = SaveAsync(Session, saveAs: false));
        Bind(Commands.SaveAs, _ => _ = SaveAsync(Session, saveAs: true));
        Bind(Commands.SaveAll, _ => _ = SaveAllAsync(), () => _tabs.Modified.Count > 0);
        Bind(Commands.CloseTab, _ => _ = CloseTabAsync(Session));
        Bind(Commands.NextTab, _ => _tabs.Cycle(backwards: false), () => _tabs.Tabs.Count > 1);
        Bind(Commands.PreviousTab, _ => _tabs.Cycle(backwards: true), () => _tabs.Tabs.Count > 1);
        Bind(Commands.Exit, _ => Close());
        Bind(Commands.OpenFontFolder, _ => OpenFontFolder());

        // ---- Edit ----
        Bind(Commands.PastePlainText, _ => PasteFilter.PastePlainText(Rtb));

        // ---- Find and replace: Section 6.10 ----
        Bind(Commands.Find, _ => OpenFindBar(replacing: false));
        Bind(Commands.Replace, _ => OpenFindBar(replacing: true));
        Bind(Commands.FindNext, _ => _ = FindAgainAsync(backwards: false));
        Bind(Commands.FindPrevious, _ => _ = FindAgainAsync(backwards: true));

        // ---- Format: characters ----
        Bind(Commands.Strikethrough, _ => FormatCommands.ToggleStrikethrough(Rtb));
        Bind(Commands.SmallCaps, _ => FormatCommands.ToggleSmallCaps(Rtb));
        Bind(Commands.Superscript, _ => FormatCommands.SetVerticalAlignment(Rtb, VAlign.Super));
        Bind(Commands.Subscript, _ => FormatCommands.SetVerticalAlignment(Rtb, VAlign.Sub));
        Bind(Commands.GrowFont, _ => FormatCommands.GrowFont(Rtb));
        Bind(Commands.ShrinkFont, _ => FormatCommands.ShrinkFont(Rtb));

        Bind(Commands.SetUnderline, p =>
        {
            // A null parameter is the Ctrl+U gesture: toggle a plain single
            // underline. A named kind comes from the Format menu.
            if (p is null)
            {
                var kind = Bridge.SpProps.ReadUnderline(Rtb.Selection) ?? UnderlineKind.None;

                FormatCommands.SetUnderline(
                    Rtb, kind == UnderlineKind.None ? UnderlineKind.Single : UnderlineKind.None);
            }
            else if (Parse<UnderlineKind>(p) is { } named)
            {
                FormatCommands.SetUnderline(Rtb, named);
            }
        });

        Bind(Commands.ChangeCase, p =>
        {
            if (Parse<CaseMode>(p) is { } mode)
            {
                FormatCommands.ChangeCase(Rtb, mode);
            }
        });

        // ---- Format: paragraphs ----
        Bind(Commands.SetParagraphStyle, p =>
        {
            if (p is string name)
            {
                FormatCommands.SetStyle(Rtb, name, _styles);
            }
        });

        Bind(Commands.StyleNormal, _ => FormatCommands.SetStyle(Rtb, "Normal", _styles));
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

            Bind(command, _ => FormatCommands.SetStyle(Rtb, name, _styles));
        }

        Bind(Commands.SetLineSpacing, p =>
        {
            if (ParseDouble(p) is { } multiple)
            {
                FormatCommands.SetLineSpacing(Rtb, multiple);
            }
        });

        Bind(Commands.ClearFormatting, _ => FormatCommands.ClearFormatting(Rtb, _styles));
        Bind(Commands.FormatPainter, _ => FormatToolbars.PickFormatPainter());

        // ---- Insert ----
        Bind(Commands.InsertDateAndTime, _ =>
            Rtb.CaretPosition.InsertTextInRun(
                DateTime.Now.ToString("d MMMM yyyy HH:mm", CultureInfo.CurrentCulture)));

        Bind(Commands.ParagraphSettings, _ => ShowParagraphSettings());
        Bind(Commands.InsertSpecialCharacter, _ => ShowSpecialChars());

        Bind(Commands.InsertImage, _ => InsertImage());
        Bind(Commands.InsertTable, _ => InsertTable());
        Bind(Commands.InsertHorizontalLine, _ => InsertHorizontalLine());
        Bind(Commands.InsertPageBreak, _ => InsertPageBreak());

        // ---- Lists ----
        Bind(Commands.ToggleBulletList, _ => ListCommands.ToggleBullets(Rtb));
        Bind(Commands.ToggleNumberList, _ => ListCommands.ToggleNumbering(Rtb));

        Bind(
            Commands.RestartNumbering,
            _ => ListCommands.RestartNumbering(Rtb),
            () => ListCommands.CaretIsInNumberedList(Rtb));

        Bind(
            Commands.ContinueNumbering,
            _ => ListCommands.ContinueNumbering(Rtb),
            () => ListCommands.CaretIsInNumberedList(Rtb));

        // ---- Table ----
        // Every one of these asks the same question, so the Table menu greys
        // itself out as a whole when the caret leaves a table.
        bool InTable() => TableCommands.CaretIsInTable(Rtb);

        Bind(Commands.InsertRowAbove, _ => TableCommands.InsertRow(Rtb, above: true), InTable);
        Bind(Commands.InsertRowBelow, _ => TableCommands.InsertRow(Rtb, above: false), InTable);
        Bind(Commands.InsertColumnLeft, _ => TableCommands.InsertColumn(Rtb, left: true), InTable);
        Bind(Commands.InsertColumnRight, _ => TableCommands.InsertColumn(Rtb, left: false), InTable);
        Bind(Commands.DeleteRow, _ => TableCommands.DeleteRow(Rtb), InTable);
        Bind(Commands.DeleteColumn, _ => TableCommands.DeleteColumn(Rtb), InTable);
        Bind(Commands.DeleteTable, _ => TableCommands.DeleteTable(Rtb), InTable);
        Bind(Commands.CellShading, _ => PickCellShading(), InTable);
        Bind(Commands.ToggleTableBorders, _ => TableCommands.ToggleBorders(Rtb), InTable);

        Bind(Commands.MergeCells, _ => TableCommands.Merge(Rtb), () => TableCommands.CanMerge(Rtb));
        Bind(Commands.SplitCell, _ => TableCommands.Split(Rtb), () => TableCommands.CanSplit(Rtb));

        // ---- Images and links ----
        Bind(
            Commands.ImageProperties,
            _ => ShowImageProperties(),
            () => ImageSupport.ImageAt(Rtb) is not null);

        Bind(Commands.InsertLink, _ => ShowLinkDialog());

        Bind(
            Commands.EditLink,
            _ => ShowLinkDialog(),
            () => LinkSupport.LinkAt(Rtb) is not null);

        Bind(
            Commands.RemoveLink,
            _ => LinkSupport.Remove(Rtb),
            () => LinkSupport.LinkAt(Rtb) is not null);

        Bind(
            Commands.OpenLink,
            _ =>
            {
                if (LinkSupport.LinkAt(Rtb) is { } link)
                {
                    LinkSupport.Open(link, this);
                }
            },
            () => LinkSupport.LinkAt(Rtb) is not null);

        // ---- Printing and the session: Sections 6.13 to 6.17 ----
        Bind(Commands.PageSetup, _ => ShowPageSetup());
        Bind(Commands.Print, _ => PrintDocument());
        Bind(Commands.ExportPdf, _ => ExportPdf());
        Bind(Commands.ToggleAutoSave, _ => ToggleAutoSave());
        Bind(Commands.RecoverDocuments, _ => OfferLeftovers());
        Bind(Commands.Preferences, _ => ShowPreferences());

        // ---- Tools ----
        Bind(Commands.ToggleSpellCheck, _ => ToggleSpellCheck());
        Bind(Commands.EditUserDictionary, _ => EditUserDictionary());

        Bind(Commands.SetSpellCheckLanguage, p =>
        {
            if (p is string language)
            {
                SetSpellCheckLanguage(language);
            }
        });

        Bind(Commands.WordCount, _ => ShowWordCount());
        Bind(Commands.ReloadFonts, _ => ReloadFonts());
        Bind(Commands.FontReport, _ => ShowFontReport());

        // ---- View ----
        Bind(Commands.SetTheme, p =>
        {
            if (Parse<ThemeChoice>(p) is { } choice)
            {
                Apply(_settings with { Theme = choice });
                ThemeManager.Apply(choice);
                SyncViewMenu();
            }
        });

        // Dark paper and the page view are one setting for the app, so they
        // apply to every open tab rather than only to the one in front - a
        // half-dark set of tabs would just look like a bug.
        Bind(Commands.ToggleDarkPaper, _ =>
        {
            Apply(_settings with { DarkPaper = !_settings.DarkPaper });

            foreach (var view in LiveViews())
            {
                view.DarkPaper = _settings.DarkPaper;
            }

            SyncViewMenu();
        });

        Bind(Commands.ToggleFormattingMarks, _ =>
        {
            Apply(_settings with { ShowFormattingMarks = !_settings.ShowFormattingMarks });

            foreach (var view in LiveViews())
            {
                view.ShowFormattingMarks = _settings.ShowFormattingMarks;
            }

            SyncViewMenu();
        });

        Bind(Commands.SetPageView, p =>
        {
            if (Parse<PageViewMode>(p) is { } mode)
            {
                Apply(_settings with { PageView = mode });

                foreach (var view in LiveViews())
                {
                    view.PageView = mode;
                }

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
            Apply(_settings with { ShowStatusBar = !_settings.ShowStatusBar });
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
    /// <remarks>
    /// The items get no command target here. This runs before the first tab
    /// exists, and <see cref="RouteMenuCommandsToEditor"/> sets the target for
    /// every menu item - these included, since they are descendants of the menu
    /// - each time the active tab changes.
    /// </remarks>
    private void BuildParagraphStyleMenu()
    {
        foreach (var style in _styles.All)
        {
            ParagraphStyleMenu.Items.Add(new MenuItem
            {
                Header = style.Name,
                Command = Commands.SetParagraphStyle,
                CommandParameter = style.Name,
                InputGestureText = ShortcutFor(style.Name),
            });
        }
    }

    /// <summary>
    /// The editors that exist: a tab that has never been shown has none
    /// (Section 2.1), and a setting that applies to every tab can only reach
    /// the ones that have been built. The rest pick it up from
    /// <c>ApplyDefaults</c> when they are.
    /// </summary>
    private IEnumerable<EditorView> LiveViews() =>
        _tabs.Tabs.Select(t => t.View).OfType<EditorView>();

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

        // Everything expensive starts here and not a line earlier: this is the
        // far side of the first frame, so the window is already on screen and
        // typeable (Section 4.3). The font folder is the largest single piece
        // of work the app does, and none of it is on the startup path.
        FontRegistry.DefaultFamily = _settings.DefaultFontFamily;

        // --measure-startup renders one frame and exits, so there is no point
        // starting work that outlives the window - and a measurement run that
        // prepares the font folder is also a measurement run that is doing
        // something a real launch would not.
        if (_options.MeasureStartup)
        {
            return;
        }

        FontRegistry.FontsChanged += OnFontsChanged;
        FontRegistry.PreparationProgress += OnFontProgress;
        FontRegistry.StartBackgroundPreparation();

        // Section 6.11 and Section 2.1: switching spell check on loads the
        // Windows spell checker, so it waits until here. Before this line the
        // window has no spell checking at all, which is a second or two of a
        // document nobody has typed in yet.
        StartSpellCheck();

        // Files named on the command line or dragged onto the .exe, and
        // otherwise the last session's (Section 6.12). Awaited before the
        // session services start, so the recovery dialog does not open over a
        // window that is still filling its tabs.
        _ = StartUpTheRestAsync();
    }

    /// <summary>
    /// The last of Section 4.3: open what was asked for, then start the
    /// recovery timer, AutoSave and the single-instance listener.
    /// </summary>
    private async Task StartUpTheRestAsync()
    {
        try
        {
            await OpenStartupFilesAsync().ConfigureAwait(true);
        }
        finally
        {
            // In a finally, because a file that failed to open is no reason to
            // leave the application without crash recovery.
            StartSessionServices();
        }
    }

    private void OnFontsChanged(object? sender, EventArgs e)
    {
        FormatToolbars.RefreshFonts();

        // The default font is applied before the first frame, from its name
        // alone, which only works for an installed font. If it turns out to be
        // a font from the fonts/ folder, this is the first moment it can be
        // resolved properly - and it asks the folder dictionary directly, so
        // it never enumerates the installed fonts to find out.
        if (FontRegistry.TryFolderFamily(_settings.DefaultFontFamily, out var folderDefault))
        {
            foreach (var view in LiveViews())
            {
                view.Editor.FontFamily = folderDefault;
            }
        }

        // Only worth saying once the first pass is done - during it, the
        // progress line is already saying more.
        if (_fontsAnnounced)
        {
            Status.ShowMessage("New fonts added.");
        }

        _fontsAnnounced = true;
    }

    private bool _fontsAnnounced;

    private void OnFontProgress(object? sender, Core.Fonts.FontPreparer.Progress progress)
    {
        Status.ShowMessage(
            progress.Done >= progress.Total
                ? $"{progress.Total} font files prepared."
                : $"Preparing fonts {progress.Done + 1}/{progress.Total}...",
            transient: progress.Done >= progress.Total);
    }

    private void ReloadFonts()
    {
        Status.ShowMessage("Checking the font folder...");
        FontRegistry.Reload();
    }

    private void ShowFontReport() =>
        new Dialogs.FontReport { Owner = this }.ShowDialog();

    // ------------------------------------------- insert: Section 6.9 ---------

    private void InsertImage()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Insert Image",
            Filter = ImageSupport.OpenFilter,
            CheckFileExists = true,
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            ImageSupport.Insert(Editor.Editor, dialog.FileName);
        }
        catch (ImageSupport.ImageException ex)
        {
            // A picture that will not load is the user's problem to solve, not
            // a crash: they chose the file, so they can choose another.
            MessageBox.Show(this, ex.Message, "Insert Image", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void InsertTable()
    {
        var dialog = new Dialogs.InsertTable { Owner = this };

        if (dialog.ShowDialog() == true)
        {
            TableCommands.Insert(Editor.Editor, dialog.Rows, dialog.Columns, dialog.Borders);
        }
    }

    /// <summary>
    /// Alignment, indents, spacing and line spacing for the selected
    /// paragraphs, in one dialog and one undo step (Section 8.1).
    /// </summary>
    private void ShowParagraphSettings()
    {
        var dialog = new Dialogs.ParagraphDialog { Owner = this };

        dialog.Show(FormatCommands.ReadParagraph(Rtb));

        if (dialog.ShowDialog() == true)
        {
            FormatCommands.ApplyParagraph(Rtb, dialog.Result);
        }
    }

    /// <summary>
    /// The character grid, opened on the font the caret is in (Section 8.1).
    /// </summary>
    /// <remarks>
    /// Not a modal dialog: it stays open while characters are picked, because
    /// the reason to open a character grid is rarely one character. The
    /// insertion happens here rather than in the dialog, so there stays one
    /// piece of code that writes to the document.
    /// </remarks>
    private void ShowSpecialChars()
    {
        if (_specialChars is not null)
        {
            _specialChars.Show(CaretFontFamily(), _settings.RecentCharacters);
            _specialChars.Activate();

            return;
        }

        var dialog = new Dialogs.SpecialChars { Owner = this };

        dialog.CharacterChosen += (_, character) =>
        {
            InsertAtCaret(character);
            Apply(_settings with { RecentCharacters = dialog.Recent });
        };

        dialog.Closed += (_, _) => _specialChars = null;

        _specialChars = dialog;

        dialog.Show(CaretFontFamily(), _settings.RecentCharacters);
        dialog.Show();
    }

    /// <summary>The font family at the caret, or the typing default.</summary>
    private string CaretFontFamily() =>
        Rtb.Selection.GetPropertyValue(TextElement.FontFamilyProperty) is FontFamily family
            ? family.Source
            : _settings.DefaultFontFamily;

    /// <summary>
    /// Puts text in at the caret, replacing the selection, as one edit.
    /// </summary>
    private void InsertAtCaret(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        Rtb.BeginChange();
        try
        {
            Rtb.Selection.Text = text;
            Rtb.CaretPosition = Rtb.Selection.End;
        }
        finally
        {
            Rtb.EndChange();
        }

        Rtb.Focus();
    }

    /// <summary>
    /// A horizontal rule, built the same way the bridge builds one so the
    /// walker recognises it (Section 6.4).
    /// </summary>
    private void InsertHorizontalLine()
    {
        var rtb = Editor.Editor;

        rtb.BeginChange();

        try
        {
            var container = new System.Windows.Documents.BlockUIContainer(
                new Separator { Margin = new Thickness(0, Units.PointsToDips(6), 0, Units.PointsToDips(6)) });

            Bridge.SpProps.SetStyleName(container, Bridge.SpProps.HorizontalRuleStyleName);

            var paragraph = rtb.CaretPosition.Paragraph;

            if (paragraph?.SiblingBlocks is { } siblings)
            {
                siblings.InsertAfter(paragraph, container);

                // Somewhere to carry on typing, since a rule cannot hold a
                // caret.
                if (container.NextBlock is null)
                {
                    siblings.InsertAfter(container, new System.Windows.Documents.Paragraph());
                }

                if (container.NextBlock is { } next)
                {
                    rtb.CaretPosition = next.ContentStart;
                }
            }
            else
            {
                rtb.Document.Blocks.Add(container);
                rtb.Document.Blocks.Add(new System.Windows.Documents.Paragraph());
            }
        }
        finally
        {
            rtb.EndChange();
        }
    }

    /// <summary>
    /// Starts the paragraph at the caret on a new page.
    /// </summary>
    /// <remarks>
    /// A page break in this app is a property of a paragraph rather than a
    /// character in the text, because that is what .docx and .odt both carry
    /// and what the IR records (Section 4.2). So "insert a page break" means
    /// "break before the paragraph the caret is in", and a second press on the
    /// same paragraph takes it off again.
    /// </remarks>
    private void InsertPageBreak()
    {
        var rtb = Editor.Editor;

        if (rtb.CaretPosition.Paragraph is not { } paragraph)
        {
            return;
        }

        rtb.BeginChange();

        try
        {
            paragraph.BreakPageBefore = !paragraph.BreakPageBefore;
        }
        finally
        {
            rtb.EndChange();
        }
    }

    /// <summary>
    /// The cell shading colour, through the same palette the toolbar uses.
    /// </summary>
    /// <remarks>
    /// Hosted in a small window rather than being another dialog of its own:
    /// the picker already has the palette, the recent colours and the hex box,
    /// and a second copy of all that would be a second place for them to
    /// disagree.
    /// </remarks>
    private void PickCellShading()
    {
        var picker = new Controls.ColorPicker
        {
            Glyph = "",
            ClearLabel = "No Shading",
            Margin = new Thickness(12),
        };

        picker.ShowCurrent(TableCommands.ShadingAt(Editor.Editor));

        var window = new Window
        {
            Title = "Cell Shading",
            Owner = this,
            Content = picker,
            SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };

        picker.ColorChosen += (_, color) =>
        {
            TableCommands.SetShading(Editor.Editor, color);
            window.DialogResult = true;
        };

        window.ShowDialog();
    }

    private void ShowImageProperties()
    {
        if (ImageSupport.ImageAt(Editor.Editor) is not { } image)
        {
            return;
        }

        var dialog = new Dialogs.ImageProperties { Owner = this };
        var (naturalWidth, naturalHeight) = ImageSupport.NaturalSize(image);
        var alt = image.Tag is Bridge.SpProps.ImagePayload payload ? payload.Alt : "";

        dialog.Describe(
            Units.DipsToPoints(double.IsNaN(image.Width) ? image.ActualWidth : image.Width),
            Units.DipsToPoints(double.IsNaN(image.Height) ? image.ActualHeight : image.Height),
            alt,
            naturalWidth,
            naturalHeight);

        if (dialog.ShowDialog() == true)
        {
            ImageSupport.SetProperties(Editor.Editor, image, dialog.WidthPt, dialog.HeightPt, dialog.Alt);
        }
    }

    private void ShowLinkDialog()
    {
        var rtb = Editor.Editor;
        var editing = LinkSupport.LinkAt(rtb) is not null;
        var (url, text) = LinkSupport.Current(rtb);

        var dialog = new Dialogs.InsertLink { Owner = this };

        dialog.Describe(url, text, editing);

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        if (dialog.Removed)
        {
            LinkSupport.Remove(rtb);

            return;
        }

        LinkSupport.InsertOrEdit(rtb, dialog.Url, dialog.DisplayText);
    }

    /// <summary>
    /// Lists the fonts a document asked for that this PC cannot show
    /// (Section 6.6). Phase 4 calls this when a document is opened; until
    /// then nothing does, because nothing can open a document yet.
    /// </summary>
    internal void ShowMissingFonts(IEnumerable<string> requestedFamilies)
    {
        var missing = FontRegistry.MissingFrom(requestedFamilies);

        if (missing.Count == 0)
        {
            MissingFontsBar.Visibility = Visibility.Collapsed;
            return;
        }

        MissingFontsText.Text =
            $"This document uses fonts that aren't installed: {string.Join(", ", missing)}. "
            + "Drop them into the font folder and click Reload Fonts.";

        MissingFontsBar.Visibility = Visibility.Visible;
    }

    private void OnDismissMissingFonts(object sender, RoutedEventArgs e) =>
        MissingFontsBar.Visibility = Visibility.Collapsed;

    /// <summary>
    /// The toolbars and the status bar follow the active editor only. Every tab
    /// raises this, and a background tab finishing a throttled readout would
    /// otherwise put another document's formatting in the toolbar.
    /// </summary>
    private void OnFormatStateChanged(object? sender, FormatState state)
    {
        if (!ReferenceEquals(sender, _tabs.Active?.View))
        {
            return;
        }

        FormatToolbars.UpdateFrom(state);
        Status.UpdateFrom(state);

        // A loaded format painter paints the next selection the user makes.
        FormatToolbars.PaintIfArmed();
    }

    private void OnCountsChanged(object? sender, (int Words, int Characters, int? SelectedWords) counts)
    {
        if (!ReferenceEquals(sender, _tabs.Active?.View))
        {
            return;
        }

        Status.ShowCounts(counts.Words, counts.Characters, counts.SelectedWords);
    }

    private void SyncViewMenu()
    {
        ThemeLightItem.IsChecked = _settings.Theme == ThemeChoice.Light;
        ThemeDarkItem.IsChecked = _settings.Theme == ThemeChoice.Dark;
        ThemeFollowItem.IsChecked = _settings.Theme == ThemeChoice.FollowWindows;

        DarkPaperItem.IsChecked = _settings.DarkPaper;
        FormattingMarksItem.IsChecked = _settings.ShowFormattingMarks;
        PageWidthItem.IsChecked = _settings.PageView == PageViewMode.PageWidth;
        FullWidthItem.IsChecked = _settings.PageView == PageViewMode.FullWidth;
        StatusBarItemToggle.IsChecked = _settings.ShowStatusBar;
        ToolbarRow1Item.IsChecked = _settings.ShowToolbarRow1;
        ToolbarRow2Item.IsChecked = _settings.ShowToolbarRow2;
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        // Asked before anything is saved, so Cancel leaves the window open with
        // every document untouched.
        if (!_closingAfterSave && _tabs.Modified.Count > 0)
        {
            switch (AskAboutEverything())
            {
                case Dialogs.UnsavedChanges.Answer.Cancel:
                    e.Cancel = true;
                    return;

                case Dialogs.UnsavedChanges.Answer.Save:
                    // Closing cannot be awaited, so the close is called off,
                    // the saves run, and the window closes itself once they are
                    // done. Letting the close carry on while writes were still
                    // in flight would be a close that lost them.
                    e.Cancel = true;
                    _ = SaveThenCloseAsync();
                    return;
            }
        }

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

        // What to reopen next launch, when the setting asks for it
        // (Section 6.12). Named files only: an untitled document has no name
        // to reopen, and its contents are recovery's business.
        SessionFiles.Save(_tabs.Tabs.Select(t => t.FilePath).OfType<string>());

        foreach (var view in LiveViews())
        {
            PasteFilter.Detach(view.Editor);
        }

        // Last, and only on a close that got this far: the recovery folder is
        // deleted here, and a run that ended any other way leaves it for the
        // next launch to find (Section 6.13.1).
        EndSessionCleanly();
    }

    /// <summary>
    /// Exit asks about every modified tab in one dialog rather than one after
    /// another (Section 6.12).
    /// </summary>
    /// <remarks>
    /// One dialog because a prompt per tab is a prompt the user stops reading:
    /// with four modified documents the fourth Yes/No/Cancel is pressed without
    /// being read, and the whole point of asking is lost.
    /// </remarks>
    private Dialogs.UnsavedChanges.Answer AskAboutEverything()
    {
        var dialog = new Dialogs.UnsavedChanges { Owner = this };

        dialog.Describe(_tabs.Modified.Select(t => t.Title));
        dialog.ShowDialog();

        return dialog.Result;
    }

    /// <summary>Set while the window is closing itself after a Save All on exit.</summary>
    private bool _closingAfterSave;

    private async Task SaveThenCloseAsync()
    {
        foreach (var session in _tabs.Modified.ToList())
        {
            if (!await SaveAsync(session, saveAs: false).ConfigureAwait(true))
            {
                // A Save As that was cancelled, or a write that failed. The
                // window stays open, which is the only safe answer.
                return;
            }
        }

        _closingAfterSave = true;
        Close();
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
