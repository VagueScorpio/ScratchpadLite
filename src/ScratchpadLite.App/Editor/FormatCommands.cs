using System.Windows.Controls;

namespace ScratchpadLite.App.Editor;

/// <summary>
/// Every character and paragraph formatting action (Section 6.8): bold,
/// italic, the five underline kinds, strikethrough, super and subscript,
/// small caps, text and highlight color, change case, paragraph styles,
/// alignment, indents, line spacing, space before and after, grow and
/// shrink, and clear formatting. All of it goes through the RichTextBox
/// undo stack so one Ctrl+Z reverses one action.
///
/// Speed rule (Section 2.1): the toolbar state update that follows a
/// selection change runs at Background dispatcher priority and is throttled
/// to one per fifty milliseconds, so holding an arrow key stays smooth.
/// </summary>
/// <remarks>Phase 1.</remarks>
internal static class FormatCommands
{
    internal static void Apply(RichTextBox editor, string action, object? argument = null) =>
        throw new NotImplementedException("Phase 1");
}
