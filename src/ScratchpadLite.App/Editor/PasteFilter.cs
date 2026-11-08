using System.Windows.Controls;

namespace ScratchpadLite.App.Editor;

/// <summary>
/// Decides what survives a paste (Section 6.8). A normal paste keeps
/// formatting the editor can represent and drops the rest; Ctrl+Shift+V
/// pastes plain text. Nothing executable ever comes in from the clipboard:
/// embedded objects and scripts are dropped (Section 7.5).
/// </summary>
/// <remarks>Phase 1.</remarks>
internal static class PasteFilter
{
    internal static void Attach(RichTextBox editor) => throw new NotImplementedException("Phase 1");

    internal static void PastePlainText(RichTextBox editor) => throw new NotImplementedException("Phase 1");
}
