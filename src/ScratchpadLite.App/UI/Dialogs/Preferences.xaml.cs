using System.Windows;

namespace ScratchpadLite.App.UI.Dialogs;

/// <summary>
/// Every setting in Section 6.2, including the AutoSave switch and interval,
/// the recovery interval, the theme, and whether font names are previewed in
/// their own font.
/// </summary>
/// <remarks>Phase 8.</remarks>
public partial class Preferences : Window
{
    public Preferences() => InitializeComponent();
}
