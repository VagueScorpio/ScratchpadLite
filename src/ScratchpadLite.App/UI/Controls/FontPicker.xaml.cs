using System.Windows.Controls;

namespace ScratchpadLite.App.UI.Controls;

/// <summary>
/// The font picker (Section 6.7): grouped by source, virtualized,
/// type-to-find, with each name drawn in its own font when that setting is
/// on. Virtualization matters here, because drawing a name in its own font
/// makes WPF open that font file, so only visible rows pay for it. It must
/// open in under 300 ms with the full bundle.
/// </summary>
/// <remarks>Phase 2.</remarks>
public partial class FontPicker : UserControl
{
    public FontPicker() => InitializeComponent();
}
