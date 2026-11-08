using System.Windows.Controls;

namespace ScratchpadLite.App.UI;

/// <summary>
/// The two toolbar rows (Section 8.2). Icons are glyphs from the Windows icon
/// font, Segoe Fluent Icons falling back to Segoe MDL2 Assets, so there are
/// no icon files to ship or license. Every button has a tooltip naming its
/// shortcut.
/// </summary>
/// <remarks>Phase 1.</remarks>
public partial class Toolbars : UserControl
{
    public Toolbars() => InitializeComponent();
}
