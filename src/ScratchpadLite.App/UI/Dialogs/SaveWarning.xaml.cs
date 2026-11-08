using System.Windows;

namespace ScratchpadLite.App.UI.Dialogs;

/// <summary>
/// The lossy-format warning (Section 7.1). It names exactly what this format
/// will drop, and offers Save anyway, Save as DOCX instead, or Cancel, with a
/// per-format do-not-warn-again box.
/// </summary>
/// <remarks>Phase 4.</remarks>
public partial class SaveWarning : Window
{
    public SaveWarning() => InitializeComponent();
}
