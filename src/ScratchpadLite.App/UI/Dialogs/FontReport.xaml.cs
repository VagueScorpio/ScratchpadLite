using System.Windows;

namespace ScratchpadLite.App.UI.Dialogs;

/// <summary>
/// What happened to every file in the font folder (Section 6.6): loaded
/// directly, converted, a duplicate of another file, or unsupported with the
/// reason why. Created the first time it is opened, never at startup (Section
/// 2.1).
/// </summary>
/// <remarks>Phase 2.</remarks>
public partial class FontReport : Window
{
    public FontReport() => InitializeComponent();
}
