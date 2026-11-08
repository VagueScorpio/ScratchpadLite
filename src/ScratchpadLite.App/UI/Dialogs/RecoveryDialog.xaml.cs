using System.Windows;

namespace ScratchpadLite.App.UI.Dialogs;

/// <summary>
/// Shown when a previous run left recovery files behind (Section 6.13.1). It
/// lists what was found with its timestamp and lets the user open or discard
/// each one. Nothing is ever discarded without being asked.
/// </summary>
/// <remarks>Phase 8.</remarks>
public partial class RecoveryDialog : Window
{
    public RecoveryDialog() => InitializeComponent();
}
