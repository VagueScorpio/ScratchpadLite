using System.Globalization;
using System.Windows.Controls;
using System.Windows.Input;
using ScratchpadLite.App.Editor;

namespace ScratchpadLite.App.UI.Controls;

/// <summary>
/// Font size, from the preset list or typed freely including half sizes such as
/// 10.5 (Section 6.7).
/// </summary>
/// <remarks>Phase 1.</remarks>
public partial class SizePicker : UserControl
{
    private bool _suppress;

    public SizePicker()
    {
        InitializeComponent();
        Box.ItemsSource = FormatCommands.PresetSizesPt;
    }

    /// <summary>Raised when the user picks or types a valid size, in points.</summary>
    internal event EventHandler<double>? SizeChosen;

    /// <summary>
    /// Shows the size at the caret. A null means the selection spans several
    /// sizes, and the box goes blank rather than claiming one of them.
    /// </summary>
    internal void Show(double? sizePt)
    {
        _suppress = true;
        try
        {
            Box.SelectedItem = null;
            Box.Text = sizePt?.ToString("0.##", CultureInfo.CurrentCulture) ?? string.Empty;
        }
        finally
        {
            _suppress = false;
        }
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_suppress && Box.SelectedItem is double size)
        {
            SizeChosen?.Invoke(this, size);
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitTypedValue();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            // Escape puts the box back to the real size rather than leaving a
            // half-typed number sitting there.
            Box.Text = string.Empty;
            e.Handled = true;
        }
    }

    private void OnLostFocus(object sender, System.Windows.RoutedEventArgs e) => CommitTypedValue();

    /// <summary>
    /// Accepts a typed size. Out-of-range and non-numeric input is ignored
    /// rather than reported: the box is a small control, and the next selection
    /// change puts the real value back.
    /// </summary>
    private void CommitTypedValue()
    {
        if (_suppress || string.IsNullOrWhiteSpace(Box.Text))
        {
            return;
        }

        if (double.TryParse(
                Box.Text.Trim(),
                NumberStyles.Float,
                CultureInfo.CurrentCulture,
                out var typed)
            && typed is >= FormatCommands.MinSizePt and <= FormatCommands.MaxSizePt)
        {
            SizeChosen?.Invoke(this, typed);
        }
    }
}
