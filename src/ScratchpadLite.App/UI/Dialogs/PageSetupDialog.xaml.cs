using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using ScratchpadLite.Core.Model;

namespace ScratchpadLite.App.UI.Dialogs;

/// <summary>
/// Paper size, orientation and margins (Section 6.17). What it edits is the
/// document's <see cref="PageSetup"/>, which DOCX and ODT both store, so the
/// setting travels with the file rather than with the application.
/// </summary>
/// <remarks>
/// The margins are shown in inches and stored in points. Inches because that
/// is what the ruler on a page of paper is marked in and what every other word
/// processor asks for; points because that is what the file formats use, and
/// one conversion in one place is better than a unit the formats have to guess
/// at.
/// </remarks>
public partial class PageSetupDialog : Window
{
    private const double PointsPerInch = 72;

    /// <summary>The widest margin that still leaves a page to type on.</summary>
    private const double MaxMarginInches = 4;

    public PageSetupDialog()
    {
        InitializeComponent();

        foreach (var paper in Services.Printing.PaperNames)
        {
            PaperBox.Items.Add(paper);
        }

        PaperBox.SelectedIndex = 0;

        Show(new PageSetup());

        Loaded += (_, _) => PaperBox.Focus();
    }

    /// <summary>The page setup as the dialog was left. Only valid on OK.</summary>
    internal PageSetup Result { get; private set; } = new();

    /// <summary>Fills the dialog from a document's current page setup.</summary>
    internal void Show(PageSetup page)
    {
        ArgumentNullException.ThrowIfNull(page);

        Result = page;

        var index = Services.Printing.PaperNames
            .ToList()
            .FindIndex(p => string.Equals(p, page.Paper, StringComparison.OrdinalIgnoreCase));

        PaperBox.SelectedIndex = index >= 0 ? index : 0;

        PortraitButton.IsChecked = !page.Landscape;
        LandscapeButton.IsChecked = page.Landscape;

        TopBox.Text = Inches(page.MarginTopPt);
        BottomBox.Text = Inches(page.MarginBottomPt);
        LeftBox.Text = Inches(page.MarginLeftPt);
        RightBox.Text = Inches(page.MarginRightPt);
    }

    private static string Inches(double points) =>
        (points / PointsPerInch).ToString("0.##", CultureInfo.CurrentCulture);

    private void OnOk(object sender, RoutedEventArgs e)
    {
        if (Commit())
        {
            DialogResult = true;
        }
    }

    /// <summary>
    /// Reads the controls into <see cref="Result"/>, or shows what is wrong.
    /// </summary>
    /// <remarks>
    /// Separate from the OK handler so a test can call it: setting
    /// <c>DialogResult</c> on a window that was not shown with
    /// <c>ShowDialog</c> throws.
    /// </remarks>
    internal bool Commit()
    {
        if (!TryRead(TopBox, "top", out var top)
            || !TryRead(BottomBox, "bottom", out var bottom)
            || !TryRead(LeftBox, "left", out var left)
            || !TryRead(RightBox, "right", out var right))
        {
            return false;
        }

        Result = new PageSetup(
            PaperBox.SelectedItem as string ?? "Letter",
            LandscapeButton.IsChecked == true,
            top * PointsPerInch,
            bottom * PointsPerInch,
            left * PointsPerInch,
            right * PointsPerInch);

        return true;
    }

    private bool TryRead(TextBox box, string which, out double inches)
    {
        if (!double.TryParse(box.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out inches)
            || inches < 0)
        {
            Complain($"Give the {which} margin as a number of inches, 0 or more.");
            box.Focus();
            box.SelectAll();

            return false;
        }

        if (inches > MaxMarginInches)
        {
            Complain($"The {which} margin cannot be more than {MaxMarginInches} inches.");
            box.Text = Inches(MaxMarginInches * PointsPerInch);
            box.Focus();
            box.SelectAll();

            return false;
        }

        return true;
    }

    private void Complain(string message)
    {
        Problem.Text = message;
        Problem.Visibility = Visibility.Visible;
    }
}
