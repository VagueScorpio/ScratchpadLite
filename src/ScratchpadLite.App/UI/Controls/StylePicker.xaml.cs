using System.Windows;
using System.Windows.Controls;
using ScratchpadLite.Core.Model;

namespace ScratchpadLite.App.UI.Controls;

/// <summary>
/// Paragraph style chooser, showing each style drawn in its own formatting
/// (Section 6.5).
/// </summary>
/// <remarks>Phase 1.</remarks>
public partial class StylePicker : UserControl
{
    /// <summary>
    /// The preview is capped at this size. Title is 26 pt and the headings run
    /// to 20 pt; drawn at full size the dropdown would be enormous, and the
    /// point of the preview is the shape of the style, not its exact size.
    /// </summary>
    private const double MaxPreviewSizePt = 15;

    private bool _suppress;

    public StylePicker() => InitializeComponent();

    /// <summary>Raised when the user picks a style.</summary>
    internal event EventHandler<string>? StyleChosen;

    /// <summary>Fills the list. Called once the style sheet is loaded.</summary>
    internal void Load(StyleSheet styles)
    {
        _suppress = true;
        try
        {
            Box.ItemsSource = styles.All.Select(s => new StylePreview(s)).ToList();
        }
        finally
        {
            _suppress = false;
        }
    }

    /// <summary>
    /// Shows the style at the caret. A null means the selection spans more than
    /// one style, and the box goes blank.
    /// </summary>
    internal void Show(string? styleName)
    {
        _suppress = true;
        try
        {
            Box.SelectedItem = styleName is null
                ? null
                : (Box.ItemsSource as IEnumerable<StylePreview>)
                    ?.FirstOrDefault(p => string.Equals(p.Name, styleName, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            _suppress = false;
        }
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_suppress && Box.SelectedItem is StylePreview preview)
        {
            StyleChosen?.Invoke(this, preview.Name);
        }
    }

    /// <summary>One row of the dropdown: the style's name drawn the style's way.</summary>
    internal sealed class StylePreview(ParagraphStyle style)
    {
        public string Name { get; } = style.Name;

        public string PreviewFamily { get; } = style.Family ?? "Segoe UI";

        public double PreviewSizeDip { get; } =
            Units.PointsToDips(Math.Min(style.SizePt, MaxPreviewSizePt));

        public FontWeight PreviewWeight { get; } = style.Bold ? FontWeights.Bold : FontWeights.Normal;

        public FontStyle PreviewStyle { get; } = style.Italic ? FontStyles.Italic : FontStyles.Normal;
    }
}
