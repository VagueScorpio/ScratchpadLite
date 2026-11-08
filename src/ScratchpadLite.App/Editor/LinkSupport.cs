using System.Windows.Controls;

namespace ScratchpadLite.App.Editor;

/// <summary>
/// Hyperlinks (Section 6.9): insert and edit through Ctrl+K, Ctrl+click to
/// follow, and the tooltip that shows the target. A link is only ever opened
/// on a deliberate Ctrl+click, never on a plain click while editing.
/// </summary>
/// <remarks>Phase 6.</remarks>
internal static class LinkSupport
{
    internal static void InsertOrEdit(RichTextBox editor, string url, string? displayText) =>
        throw new NotImplementedException("Phase 6");
}
