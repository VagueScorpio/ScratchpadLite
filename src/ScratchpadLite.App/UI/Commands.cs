using System.Windows.Input;

namespace ScratchpadLite.App.UI;

/// <summary>
/// Every command and its shortcut, defined once (Section 8.3). Menus,
/// toolbars and key bindings all point here, so a shortcut can never mean
/// two things in two places.
///
/// Where a shortcut differs from one the RichTextBox already has, the
/// built-in binding is removed first. A unit test checks that no two
/// commands share a gesture (Section 8.3).
/// </summary>
/// <remarks>Phase 1.</remarks>
internal static class Commands
{
    internal static readonly RoutedUICommand NewTab =
        new("New Tab", nameof(NewTab), typeof(Commands),
            [new KeyGesture(Key.N, ModifierKeys.Control)]);

    // The rest of Section 8.3 is filled in during Phase 1, in this one place:
    // Open, Save, SaveAs, SaveAll, CloseTab, NextTab, Print, ExportPdf,
    // Bold, Italic, Underline kinds, Strikethrough, Super, Sub, SmallCaps,
    // ChangeCase, TextColor, Highlight, styles Normal and Heading 1 to 6,
    // Grow, Shrink, Align left centre right justify, Bullets, Numbering,
    // Indent, Outdent, LineSpacing, ClearFormatting, FormatPainter,
    // InsertImage, InsertTable, InsertLink, HorizontalLine, PageBreak,
    // SpecialCharacter, DateAndTime, Find, Replace, FindNext, FindPrevious,
    // PastePlainText, SpellCheckToggle, WordCount, FontReport, ReloadFonts,
    // OpenFontFolder, Preferences, ResetZoom, Theme choices, About.
}
