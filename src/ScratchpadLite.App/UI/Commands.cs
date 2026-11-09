using System.Windows.Input;

namespace ScratchpadLite.App.UI;

/// <summary>
/// Every command and its shortcut, defined once (Section 8.3). Menus, toolbars
/// and key bindings all point here, so a shortcut cannot come to mean two
/// different things in two different places.
/// </summary>
/// <remarks>
/// Three rules this file exists to keep:
/// <list type="bullet">
///   <item>
///     <b>This class and every command in it must stay public.</b> The menus
///     reach them with <c>{x:Static}</c>, and XAML's StaticExtension resolves
///     its value at run time by public reflection. Internal members compile
///     without complaint and then throw
///     <see cref="ArgumentException"/> — "cannot be resolved to an
///     enumeration, static field, or static property" — the moment
///     MainWindow's XAML is parsed, which kills the app before its first
///     frame. <c>MainWindowTests</c> loads the real window so that this is a
///     failing test rather than a crash on the user's PC.
///   </item>
///   <item>
///     Where a shortcut differs from one the RichTextBox already has, the
///     built-in binding is removed first — see
///     <see cref="Editor.EditorView.RemoveConflictingBuiltInBindings"/>.
///   </item>
///   <item>
///     No two commands share a gesture. <see cref="All"/> exists so a unit test
///     can assert that, rather than it being something to remember.
///   </item>
/// </list>
/// </remarks>
public static class Commands
{
    private static RoutedUICommand Make(string text, string name, params InputGesture[] gestures) =>
        new(text, name, typeof(Commands), [..gestures]);

    private static KeyGesture Key(Key key, ModifierKeys modifiers = ModifierKeys.None) => new(key, modifiers);

    private const ModifierKeys Ctrl = ModifierKeys.Control;
    private const ModifierKeys CtrlShift = ModifierKeys.Control | ModifierKeys.Shift;
    private const ModifierKeys CtrlAlt = ModifierKeys.Control | ModifierKeys.Alt;
    private const ModifierKeys AltShift = ModifierKeys.Alt | ModifierKeys.Shift;

    // ---------------------------------------------------------------- File --
    // Phase 1 defines these so the menu is wired once; the ones that need a
    // file format are enabled in Phase 4.

    public static readonly RoutedUICommand NewTab =
        Make("New Tab", nameof(NewTab), Key(System.Windows.Input.Key.N, Ctrl));

    public static readonly RoutedUICommand Open =
        Make("Open...", nameof(Open), Key(System.Windows.Input.Key.O, Ctrl));

    public static readonly RoutedUICommand Save =
        Make("Save", nameof(Save), Key(System.Windows.Input.Key.S, Ctrl));

    public static readonly RoutedUICommand SaveAs =
        Make("Save As...", nameof(SaveAs), Key(System.Windows.Input.Key.S, CtrlShift));

    public static readonly RoutedUICommand SaveAll = Make("Save All", nameof(SaveAll));

    public static readonly RoutedUICommand ToggleAutoSave = Make("AutoSave", nameof(ToggleAutoSave));

    public static readonly RoutedUICommand CloseTab =
        Make("Close Tab", nameof(CloseTab), Key(System.Windows.Input.Key.W, Ctrl));

    public static readonly RoutedUICommand NextTab =
        Make("Next Tab", nameof(NextTab), Key(System.Windows.Input.Key.Tab, Ctrl));

    public static readonly RoutedUICommand PageSetup = Make("Page Setup...", nameof(PageSetup));

    public static readonly RoutedUICommand Print =
        Make("Print...", nameof(Print), Key(System.Windows.Input.Key.P, Ctrl));

    public static readonly RoutedUICommand ExportPdf = Make("Export as PDF...", nameof(ExportPdf));

    public static readonly RoutedUICommand RecoverDocuments =
        Make("Recover Documents...", nameof(RecoverDocuments));

    public static readonly RoutedUICommand Exit = Make("Exit", nameof(Exit));

    // ---------------------------------------------------------------- Edit --
    // Undo, Redo, Cut, Copy, Paste and Select All stay on WPF's own
    // ApplicationCommands: the RichTextBox already implements them correctly,
    // and replacing them would only be a chance to get them wrong.

    public static readonly RoutedUICommand PastePlainText =
        Make("Paste as Plain Text", nameof(PastePlainText), Key(System.Windows.Input.Key.V, CtrlShift));

    // -------------------------------------------------------------- Format --

    public static readonly RoutedUICommand Strikethrough =
        Make("Strikethrough", nameof(Strikethrough), Key(System.Windows.Input.Key.D5, AltShift));

    /// <summary>Takes the <see cref="Core.Model.UnderlineKind"/> as its parameter.</summary>
    public static readonly RoutedUICommand SetUnderline =
        Make("Underline", nameof(SetUnderline), Key(System.Windows.Input.Key.U, Ctrl));

    public static readonly RoutedUICommand Superscript =
        Make("Superscript", nameof(Superscript), Key(System.Windows.Input.Key.OemPlus, CtrlShift));

    public static readonly RoutedUICommand Subscript =
        Make("Subscript", nameof(Subscript), Key(System.Windows.Input.Key.OemPlus, Ctrl));

    public static readonly RoutedUICommand SmallCaps = Make("Small Caps", nameof(SmallCaps));

    /// <summary>Takes a <see cref="Editor.CaseMode"/> as its parameter.</summary>
    public static readonly RoutedUICommand ChangeCase = Make("Change Case", nameof(ChangeCase));

    public static readonly RoutedUICommand TextColor = Make("Text Color...", nameof(TextColor));

    public static readonly RoutedUICommand Highlight = Make("Highlight...", nameof(Highlight));

    /// <summary>Takes the style name as its parameter.</summary>
    public static readonly RoutedUICommand SetParagraphStyle =
        Make("Paragraph Style", nameof(SetParagraphStyle));

    public static readonly RoutedUICommand StyleNormal =
        Make("Normal", nameof(StyleNormal), Key(System.Windows.Input.Key.D0, CtrlAlt));

    public static readonly RoutedUICommand StyleHeading1 =
        Make("Heading 1", nameof(StyleHeading1), Key(System.Windows.Input.Key.D1, CtrlAlt));

    public static readonly RoutedUICommand StyleHeading2 =
        Make("Heading 2", nameof(StyleHeading2), Key(System.Windows.Input.Key.D2, CtrlAlt));

    public static readonly RoutedUICommand StyleHeading3 =
        Make("Heading 3", nameof(StyleHeading3), Key(System.Windows.Input.Key.D3, CtrlAlt));

    public static readonly RoutedUICommand StyleHeading4 =
        Make("Heading 4", nameof(StyleHeading4), Key(System.Windows.Input.Key.D4, CtrlAlt));

    public static readonly RoutedUICommand StyleHeading5 =
        Make("Heading 5", nameof(StyleHeading5), Key(System.Windows.Input.Key.D5, CtrlAlt));

    public static readonly RoutedUICommand StyleHeading6 =
        Make("Heading 6", nameof(StyleHeading6), Key(System.Windows.Input.Key.D6, CtrlAlt));

    public static readonly RoutedUICommand GrowFont =
        Make("Grow Font", nameof(GrowFont), Key(System.Windows.Input.Key.OemPeriod, CtrlShift));

    public static readonly RoutedUICommand ShrinkFont =
        Make("Shrink Font", nameof(ShrinkFont), Key(System.Windows.Input.Key.OemComma, CtrlShift));

    /// <summary>Takes the size in points as its parameter.</summary>
    public static readonly RoutedUICommand SetFontSize = Make("Font Size", nameof(SetFontSize));

    /// <summary>Takes the family name as its parameter.</summary>
    public static readonly RoutedUICommand SetFontFamily = Make("Font", nameof(SetFontFamily));

    /// <summary>Takes the line spacing multiple as its parameter.</summary>
    public static readonly RoutedUICommand SetLineSpacing = Make("Line Spacing", nameof(SetLineSpacing));

    public static readonly RoutedUICommand ParagraphSettings = Make("Paragraph...", nameof(ParagraphSettings));

    public static readonly RoutedUICommand ClearFormatting =
        Make("Clear Formatting", nameof(ClearFormatting), Key(System.Windows.Input.Key.Space, Ctrl));

    public static readonly RoutedUICommand FormatPainter = Make("Format Painter", nameof(FormatPainter));

    // -------------------------------------------------------------- Insert --

    public static readonly RoutedUICommand InsertImage = Make("Image...", nameof(InsertImage));

    public static readonly RoutedUICommand InsertTable = Make("Table...", nameof(InsertTable));

    public static readonly RoutedUICommand InsertLink =
        Make("Link...", nameof(InsertLink), Key(System.Windows.Input.Key.K, Ctrl));

    public static readonly RoutedUICommand InsertHorizontalLine =
        Make("Horizontal Line", nameof(InsertHorizontalLine));

    public static readonly RoutedUICommand InsertPageBreak = Make("Page Break", nameof(InsertPageBreak));

    public static readonly RoutedUICommand InsertSpecialCharacter =
        Make("Special Character...", nameof(InsertSpecialCharacter));

    public static readonly RoutedUICommand InsertDateAndTime = Make("Date and Time", nameof(InsertDateAndTime));

    // --------------------------------------------------------------- Tools --

    public static readonly RoutedUICommand ToggleSpellCheck =
        Make("Spell Check", nameof(ToggleSpellCheck), Key(System.Windows.Input.Key.F7));

    public static readonly RoutedUICommand EditUserDictionary =
        Make("Edit User Dictionary...", nameof(EditUserDictionary));

    public static readonly RoutedUICommand WordCount = Make("Word Count...", nameof(WordCount));

    public static readonly RoutedUICommand FontReport = Make("Font Report...", nameof(FontReport));

    public static readonly RoutedUICommand ReloadFonts = Make("Reload Fonts", nameof(ReloadFonts));

    public static readonly RoutedUICommand OpenFontFolder = Make("Open Font Folder", nameof(OpenFontFolder));

    public static readonly RoutedUICommand Preferences = Make("Preferences...", nameof(Preferences));

    // ---------------------------------------------------------------- View --

    /// <summary>Takes a <see cref="Core.Platform.ThemeChoice"/> as its parameter.</summary>
    public static readonly RoutedUICommand SetTheme = Make("Theme", nameof(SetTheme));

    public static readonly RoutedUICommand ToggleDarkPaper = Make("Dark Paper", nameof(ToggleDarkPaper));

    /// <summary>Takes a <see cref="Core.Platform.PageViewMode"/> as its parameter.</summary>
    public static readonly RoutedUICommand SetPageView = Make("Page View", nameof(SetPageView));

    /// <summary>Takes the zoom factor as its parameter, where 1.0 is 100%.</summary>
    public static readonly RoutedUICommand SetZoom = Make("Zoom", nameof(SetZoom));

    public static readonly RoutedUICommand ResetZoom =
        Make("Reset Zoom", nameof(ResetZoom), Key(System.Windows.Input.Key.D0, Ctrl));

    public static readonly RoutedUICommand ToggleFormattingMarks =
        Make("Show Formatting Marks", nameof(ToggleFormattingMarks));

    public static readonly RoutedUICommand ToggleStatusBar = Make("Status Bar", nameof(ToggleStatusBar));

    // ---------------------------------------------------------------- Help --

    public static readonly RoutedUICommand UserGuide = Make("User Guide", nameof(UserGuide));

    public static readonly RoutedUICommand KeyboardShortcuts =
        Make("Keyboard Shortcuts", nameof(KeyboardShortcuts));

    public static readonly RoutedUICommand ThirdPartyLicenses =
        Make("Third-Party Licenses", nameof(ThirdPartyLicenses));

    public static readonly RoutedUICommand About = Make("About", nameof(About));

    /// <summary>
    /// Every command declared above, found by reflection over this type's own
    /// fields rather than kept as a second hand-maintained list — a list that
    /// can fall out of step is exactly what this class exists to avoid.
    /// Reflection over one type at test time costs nothing at startup.
    /// </summary>
    public static IReadOnlyList<RoutedUICommand> All { get; } =
        typeof(Commands)
            .GetFields(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public)
            .Where(f => f.FieldType == typeof(RoutedUICommand))
            .Select(f => (RoutedUICommand)f.GetValue(null)!)
            .ToList();
}
