using System.Windows.Controls;

namespace ScratchpadLite.App.Editor;

/// <summary>
/// The Table menu (Section 8.1), enabled only when the caret sits inside a
/// table: insert row above or below, insert column left or right, delete
/// row, column or table, merge and split cells, cell shading, and borders
/// on or off.
/// </summary>
/// <remarks>Phase 6.</remarks>
internal static class TableCommands
{
    internal static bool CaretIsInTable(RichTextBox editor) => throw new NotImplementedException("Phase 6");

    internal static void Insert(RichTextBox editor, int rows, int columns) =>
        throw new NotImplementedException("Phase 6");
}
