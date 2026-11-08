using System.Windows.Controls;

namespace ScratchpadLite.App.UI.Dialogs;

/// <summary>
/// The find and replace bar (Section 6.10). A bar rather than a dialog, so
/// the document stays visible and the caret keeps its place. Match case,
/// whole word, wrap around, replace, replace all, and the match count.
/// Replace all is one undo unit.
/// </summary>
/// <remarks>Phase 7.</remarks>
public partial class FindReplaceBar : UserControl
{
    public FindReplaceBar() => InitializeComponent();
}
