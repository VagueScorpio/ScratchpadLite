using System.Windows.Controls;

namespace ScratchpadLite.App.Editor;

/// <summary>
/// The RichTextBox host (Section 6.8): the page-width or full-width view,
/// zoom, formatting marks, and the dark-paper option. It owns the undo stack
/// for its own document, which is why each tab gets its own instance.
/// </summary>
/// <remarks>Phase 1.</remarks>
public partial class EditorView : UserControl
{
    public EditorView() => InitializeComponent();
}
