using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ScratchpadLite.App.UI.Controls;

/// <summary>
/// Text and highlight colour: a standard palette, recently used colours, a hex
/// box, and Automatic or No Highlight (Section 8.2).
/// </summary>
/// <remarks>Phase 1.</remarks>
public partial class ColorPicker : UserControl
{
    private const int MaxRecent = 10;

    private static readonly List<Swatch> RecentColors = [];

    public ColorPicker()
    {
        InitializeComponent();
        Palette.ItemsSource = StandardPalette;
        Recent.ItemsSource = RecentColors;
    }

    /// <summary>
    /// Raised when a colour is chosen. A null means Automatic for text, or No
    /// Highlight for highlight.
    /// </summary>
    internal event EventHandler<Color?>? ColorChosen;

    /// <summary>The icon-font glyph on the button.</summary>
    internal string Glyph
    {
        get => GlyphText.Text;
        set => GlyphText.Text = value;
    }

    /// <summary>The label on the clear button: "Automatic" or "No Highlight".</summary>
    internal string ClearLabel
    {
        get => (string)ClearButton.Content;
        set => ClearButton.Content = value;
    }

    /// <summary>Shows the colour currently in force under the button.</summary>
    internal void ShowCurrent(Color? color)
    {
        if (color is { } value)
        {
            var brush = new SolidColorBrush(value);
            brush.Freeze();
            CurrentSwatch.Background = brush;
        }
        else
        {
            CurrentSwatch.ClearValue(Border.BackgroundProperty);
        }
    }

    private void OnToggleClick(object sender, RoutedEventArgs e) => Flyout.IsOpen = Toggle.IsChecked == true;

    private void OnFlyoutClosed(object sender, EventArgs e)
    {
        Toggle.IsChecked = false;
        HexError.Visibility = Visibility.Collapsed;
    }

    private void OnClearClick(object sender, RoutedEventArgs e)
    {
        ColorChosen?.Invoke(this, null);
        Flyout.IsOpen = false;
    }

    private void OnSwatchClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: Swatch swatch })
        {
            Choose(swatch.Color);
        }
    }

    private void OnHexKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ApplyHex();
            e.Handled = true;
        }
    }

    private void OnHexApplyClick(object sender, RoutedEventArgs e) => ApplyHex();

    /// <summary>
    /// Takes a typed hex colour. An invalid value shows the hint in the popup
    /// rather than a message box: the popup is where the user is looking.
    /// </summary>
    private void ApplyHex()
    {
        var text = HexBox.Text.Trim();
        if (!text.StartsWith('#'))
        {
            text = '#' + text;
        }

        if (text.Length == 7
            && int.TryParse(text[1..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
        {
            Choose(Color.FromRgb(
                (byte)((rgb >> 16) & 0xFF),
                (byte)((rgb >> 8) & 0xFF),
                (byte)(rgb & 0xFF)));
            return;
        }

        HexError.Visibility = Visibility.Visible;
    }

    private void Choose(Color color)
    {
        RememberRecent(color);
        ColorChosen?.Invoke(this, color);
        Flyout.IsOpen = false;
    }

    private void RememberRecent(Color color)
    {
        RecentColors.RemoveAll(s => s.Color == color);
        RecentColors.Insert(0, new Swatch(color.ToString(CultureInfo.InvariantCulture), color));

        if (RecentColors.Count > MaxRecent)
        {
            RecentColors.RemoveRange(MaxRecent, RecentColors.Count - MaxRecent);
        }

        // ItemsControl is bound to a plain List, so it needs telling.
        Recent.ItemsSource = null;
        Recent.ItemsSource = RecentColors;
    }

    /// <summary>One colour in the palette.</summary>
    internal sealed record Swatch(string Name, Color Color)
    {
        public SolidColorBrush Brush { get; } = Frozen(Color);

        private static SolidColorBrush Frozen(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
    }

    /// <summary>
    /// A plain, legible palette: greys across the top row, then hues. Chosen so
    /// every colour here is readable as text on white paper.
    /// </summary>
    private static IReadOnlyList<Swatch> StandardPalette { get; } =
    [
        new("Black", Color.FromRgb(0x00, 0x00, 0x00)),
        new("Dark grey", Color.FromRgb(0x40, 0x40, 0x40)),
        new("Grey", Color.FromRgb(0x80, 0x80, 0x80)),
        new("Light grey", Color.FromRgb(0xBF, 0xBF, 0xBF)),
        new("White", Color.FromRgb(0xFF, 0xFF, 0xFF)),
        new("Dark red", Color.FromRgb(0x8B, 0x00, 0x00)),
        new("Red", Color.FromRgb(0xC6, 0x28, 0x28)),
        new("Orange", Color.FromRgb(0xE6, 0x7E, 0x22)),
        new("Amber", Color.FromRgb(0xF1, 0xC4, 0x0F)),
        new("Olive", Color.FromRgb(0x80, 0x80, 0x00)),
        new("Dark green", Color.FromRgb(0x1B, 0x5E, 0x20)),
        new("Green", Color.FromRgb(0x2E, 0x7D, 0x32)),
        new("Teal", Color.FromRgb(0x00, 0x79, 0x6B)),
        new("Cyan", Color.FromRgb(0x00, 0xA0, 0xB0)),
        new("Light blue", Color.FromRgb(0x42, 0xA5, 0xF5)),
        new("Blue", Color.FromRgb(0x15, 0x65, 0xC0)),
        new("Navy", Color.FromRgb(0x0D, 0x30, 0x6B)),
        new("Indigo", Color.FromRgb(0x3F, 0x51, 0xB5)),
        new("Purple", Color.FromRgb(0x6A, 0x1B, 0x9A)),
        new("Magenta", Color.FromRgb(0xAD, 0x14, 0x57)),
        new("Pink", Color.FromRgb(0xEC, 0x40, 0x7A)),
        new("Brown", Color.FromRgb(0x5D, 0x40, 0x37)),
        new("Tan", Color.FromRgb(0xA1, 0x88, 0x7F)),
        new("Yellow", Color.FromRgb(0xFF, 0xF5, 0x9D)),
        new("Mint", Color.FromRgb(0xA5, 0xD6, 0xA7)),
    ];
}
