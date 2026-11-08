using System.Windows;

namespace ScratchpadLite.App.UI.Dialogs;

/// <summary>
/// Paper size, orientation and margins, used by both printing and PDF export
/// (Section 6.17). Named PageSetupDialog so it cannot be confused with the IR
/// PageSetup record.
/// </summary>
/// <remarks>Phase 8.</remarks>
public partial class PageSetupDialog : Window
{
    public PageSetupDialog() => InitializeComponent();
}
