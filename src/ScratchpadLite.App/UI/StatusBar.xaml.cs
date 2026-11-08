using System.Windows.Controls;

namespace ScratchpadLite.App.UI;

/// <summary>
/// The status bar (Section 6.18): caret line and column, word and character
/// count (debounced, never on every keystroke), zoom, the AutoSave indicator,
/// and the font preparation progress when there is any.
/// </summary>
/// <remarks>Phase 1.</remarks>
public partial class StatusBar : UserControl
{
    public StatusBar() => InitializeComponent();
}
