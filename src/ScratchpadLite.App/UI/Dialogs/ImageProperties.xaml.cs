using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace ScratchpadLite.App.UI.Dialogs;

/// <summary>
/// Width, height and alt text for the picture at the caret (Section 6.9).
/// </summary>
/// <remarks>
/// With "Keep the picture's shape" ticked, typing in one box fills in the
/// other. The guard against the two handlers triggering each other for ever is
/// a flag rather than unsubscribing, because a TextChanged raised from code is
/// indistinguishable from one raised by typing.
/// </remarks>
public partial class ImageProperties : Window
{
    private double _ratio = 1;
    private bool _echo;

    public ImageProperties()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            WidthBox.Focus();
            WidthBox.SelectAll();
        };
    }

    internal double WidthPt { get; private set; }

    internal double HeightPt { get; private set; }

    internal string Alt { get; private set; } = "";

    /// <summary>
    /// Fills the dialog in for one picture.
    /// </summary>
    /// <param name="widthPt">Its current width in points.</param>
    /// <param name="heightPt">Its current height in points.</param>
    /// <param name="alt">Its current alt text.</param>
    /// <param name="naturalWidthPt">The width of the picture itself.</param>
    /// <param name="naturalHeightPt">The height of the picture itself.</param>
    internal void Describe(
        double widthPt,
        double heightPt,
        string alt,
        double naturalWidthPt,
        double naturalHeightPt)
    {
        WidthPt = widthPt;
        HeightPt = heightPt;
        NaturalWidthPt = naturalWidthPt;
        NaturalHeightPt = naturalHeightPt;
        _ratio = widthPt > 0 ? heightPt / widthPt : 1;

        _echo = true;
        WidthBox.Text = Format(widthPt);
        HeightBox.Text = Format(heightPt);
        _echo = false;

        AltBox.Text = alt ?? "";
        Natural.Text = $"The picture itself is {Format(naturalWidthPt)} × "
                       + $"{Format(naturalHeightPt)} pt.";
    }

    private double NaturalWidthPt { get; set; }

    private double NaturalHeightPt { get; set; }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        if (!TryRead(WidthBox, out var width) || !TryRead(HeightBox, out var height))
        {
            return;
        }

        WidthPt = width;
        HeightPt = height;
        Alt = AltBox.Text;
        DialogResult = true;
    }

    private void OnOriginalSize(object sender, RoutedEventArgs e)
    {
        _ratio = NaturalWidthPt > 0 ? NaturalHeightPt / NaturalWidthPt : 1;

        _echo = true;
        WidthBox.Text = Format(NaturalWidthPt);
        HeightBox.Text = Format(NaturalHeightPt);
        _echo = false;
    }

    private void OnWidthChanged(object sender, TextChangedEventArgs e)
    {
        if (_echo || LockBox?.IsChecked != true || HeightBox is null)
        {
            return;
        }

        if (!double.TryParse(WidthBox.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var width))
        {
            return;
        }

        _echo = true;
        HeightBox.Text = Format(width * _ratio);
        _echo = false;
    }

    private void OnHeightChanged(object sender, TextChangedEventArgs e)
    {
        if (_echo || LockBox?.IsChecked != true || WidthBox is null)
        {
            return;
        }

        if (!double.TryParse(HeightBox.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var height)
            || _ratio <= 0)
        {
            return;
        }

        _echo = true;
        WidthBox.Text = Format(height / _ratio);
        _echo = false;
    }

    private bool TryRead(TextBox box, out double value)
    {
        if (double.TryParse(box.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out value)
            && value is >= 1 and <= 20000)
        {
            return true;
        }

        Problem.Text = "Give a size in points between 1 and 20000.";
        Problem.Visibility = Visibility.Visible;
        box.Focus();
        box.SelectAll();

        return false;
    }

    private static string Format(double value) =>
        Math.Round(value, 1).ToString("0.#", CultureInfo.CurrentCulture);
}
