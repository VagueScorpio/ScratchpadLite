using System.Windows;

namespace ScratchpadLite.App.UI.Dialogs;

/// <summary>
/// Alignment, indents, first line, space before and after, and line spacing
/// in one place (the Format menu Paragraph item, Section 8.1). Named
/// ParagraphDialog so it cannot be confused with the IR or WPF Paragraph
/// types.
/// </summary>
/// <remarks>Phase 1.</remarks>
public partial class ParagraphDialog : Window
{
    public ParagraphDialog() => InitializeComponent();
}
