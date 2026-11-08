using System.Windows;

namespace ScratchpadLite.App.UI.Dialogs;

/// <summary>
/// The version read from the assembly, the last measured startup time
/// (Section 2.1), the data and font folder paths, and whether portable mode
/// is on.
/// </summary>
/// <remarks>Phase 8.</remarks>
public partial class About : Window
{
    public About() => InitializeComponent();
}
