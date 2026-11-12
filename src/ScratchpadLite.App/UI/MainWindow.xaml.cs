using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
        Editor.DocumentChanged += (_, _) => OnDocumentChanged();

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
        Bind(Commands.NewTab, _ => NewDocument());
        Bind(Commands.Open, _ => _ = OpenWithDialogAsync());
        Bind(Commands.Save, _ => _ = SaveAsync(saveAs: false));
        Bind(Commands.SaveAs, _ => _ = SaveAsync(saveAs: true));
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

        Bind(Commands.InsertImage, _ => InsertImage());
        Bind(Commands.InsertTable, _ => InsertTable());
        Bind(Commands.InsertHorizontalLine, _ => InsertHorizontalLine());
        Bind(Commands.InsertPageBreak, _ => InsertPageBreak());

        // ---- Lists ----
        Bind(Commands.ToggleBulletList, _ => ListCommands.ToggleBullets(rtb));
        Bind(Commands.ToggleNumberList, _ => ListCommands.ToggleNumbering(rtb));

        Bind(
            Commands.RestartNumbering,
            _ => ListCommands.RestartNumbering(rtb),
            () => ListCommands.CaretIsInNumberedList(rtb));

        Bind(
            Commands.ContinueNumbering,
            _ => ListCommands.ContinueNumbering(rtb),
            () => ListCommands.CaretIsInNumberedList(rtb));

        // ---- Table ----
        // Every one of these asks the same question, so the Table menu greys
        // itself out as a whole when the caret leaves a table.
        bool InTable() => TableCommands.CaretIsInTable(rtb);

        Bind(Commands.InsertRowAbove, _ => TableCommands.InsertRow(rtb, above: true), InTable);
        Bind(Commands.InsertRowBelow, _ => TableCommands.InsertRow(rtb, above: false), InTable);
        Bind(Commands.InsertColumnLeft, _ => TableCommands.InsertColumn(rtb, left: true), InTable);
        Bind(Commands.InsertColumnRight, _ => TableCommands.InsertColumn(rtb, left: false), InTable);
        Bind(Commands.DeleteRow, _ => TableCommands.DeleteRow(rtb), InTable);
        Bind(Commands.DeleteColumn, _ => TableCommands.DeleteColumn(rtb), InTable);
        Bind(Commands.DeleteTable, _ => TableCommands.DeleteTable(rtb), InTable);
        Bind(Commands.CellShading, _ => PickCellShading(), InTable);
        Bind(Commands.ToggleTableBorders, _ => TableCommands.ToggleBorders(rtb), InTable);

        Bind(Commands.MergeCells, _ => TableCommands.Merge(rtb), () => TableCommands.CanMerge(rtb));
        Bind(Commands.SplitCell, _ => TableCommands.Split(rtb), () => TableCommands.CanSplit(rtb));

        // ---- Images and links ----
        Bind(
            Commands.ImageProperties,
            _ => ShowImageProperties(),
            () => ImageSupport.ImageAt(rtb) is not null);

        Bind(Commands.InsertLink, _ => ShowLinkDialog());

        Bind(
            Commands.EditLink,
            _ => ShowLinkDialog(),
            () => LinkSupport.LinkAt(rtb) is not null);

        Bind(
            Commands.RemoveLink,
            _ => LinkSupport.Remove(rtb),
            () => LinkSupport.LinkAt(rtb) is not null);

        Bind(
            Commands.OpenLink,
            _ =>
            {
                if (LinkSupport.LinkAt(rtb) is { } link)
                {
                    LinkSupport.Open(link, this);
                }
            },
            () => LinkSupport.LinkAt(rtb) is not null);

        // ---- Tools ----
        Bind(Commands.WordCount, _ => ShowWordCount());
        Bind(Commands.ReloadFonts, _ => ReloadFonts());
        Bind(Commands.FontReport, _ => ShowFontReport());

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

        // Files named on the command line, or dragged onto the .exe.
        _ = OpenCommandLineFilesAsync();
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
            Editor.Editor.FontFamily = folderDefault;
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
        // Asked before anything is saved, so answering No leaves the window
        // open with the document untouched.
        if (!ConfirmDiscard())
        {
            e.Cancel = true;
            return;
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
