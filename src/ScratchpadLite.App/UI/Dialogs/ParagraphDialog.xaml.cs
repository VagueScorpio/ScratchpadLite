using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using ScratchpadLite.App.Editor;
using ScratchpadLite.Core.Model;

namespace ScratchpadLite.App.UI.Dialogs;

/// <summary>
/// Alignment, indents, first line, space before and after, and line spacing
/// in one place (the Format menu's Paragraph item, Section 8.1). Named
/// ParagraphDialog so it cannot be confused with the IR or WPF Paragraph
/// types.
/// </summary>
/// <remarks>
/// <para>Indents are in inches and spacing is in points, which looks
/// inconsistent and is deliberate: it is what every word processor shows,
/// because an indent is a distance across a page people measure with a ruler
/// and spacing between paragraphs is a multiple of the type size. The IR
/// stores both in points, so the inches are converted here and nowhere
/// else.</para>
///
/// <para>A blank box means "leave this one alone". With several paragraphs
/// selected the dialog has to be able to say they disagree, and a blank is
/// the only honest way to show it; carrying the same meaning into OK is then
/// what keeps the dialog from flattening paragraphs the user never looked
/// at.</para>
/// </remarks>
public partial class ParagraphDialog : Window
{
    private const double PointsPerInch = 72;

    /// <summary>How far in or out an indent may go, in inches.</summary>
    private const double MaxIndentInches = 8;

    private const double MinIndentInches = -4;

    /// <summary>Space before or after a paragraph, in points.</summary>
    private const double MaxSpacingPoints = 1000;

    private const double MinLineSpacing = 0.25;

    private const double MaxLineSpacing = 10;

    private const string Automatic = "Automatic";

    public ParagraphDialog()
    {
        InitializeComponent();

        Show(new FormatCommands.ParagraphLook());

        Loaded += (_, _) => AlignBox.Focus();
    }

    /// <summary>What the dialog was left holding. Only meaningful after OK.</summary>
    internal FormatCommands.ParagraphLook Result { get; private set; } = new();

    /// <summary>Fills the dialog from what the selected paragraphs have in common.</summary>
    internal void Show(FormatCommands.ParagraphLook look)
    {
        ArgumentNullException.ThrowIfNull(look);

        Result = look;

        AlignBox.SelectedIndex = look.Align switch
        {
            Align.Left => 0,
            Align.Center => 1,
            Align.Right => 2,
            Align.Justify => 3,
            _ => -1,
        };

        LeftBox.Text = Inches(look.LeftPt);
        RightBox.Text = Inches(look.RightPt);
        FirstLineBox.Text = Inches(look.FirstLinePt);
        BeforeBox.Text = Points(look.BeforePt);
        AfterBox.Text = Points(look.AfterPt);

        LineSpacingBox.Text = look.LineSpacing switch
        {
            null => "",
            { } spacing when double.IsNaN(spacing) => Automatic,
            { } spacing => spacing.ToString("0.##", CultureInfo.CurrentCulture),
        };
    }

    private static string Inches(double? points) =>
        points is { } value
            ? (value / PointsPerInch).ToString("0.##", CultureInfo.CurrentCulture)
            : "";

    private static string Points(double? points) =>
        points is { } value ? value.ToString("0.##", CultureInfo.CurrentCulture) : "";

    private void OnOk(object sender, RoutedEventArgs e)
    {
        if (Commit())
        {
            DialogResult = true;
        }
    }

    /// <summary>
    /// Reads the controls into <see cref="Result"/>, or says what is wrong and
    /// returns false.
    /// </summary>
    /// <remarks>
    /// Separate from the OK handler so a test can call it: setting
    /// <c>DialogResult</c> on a window that was not shown with
    /// <c>ShowDialog</c> throws.
    /// </remarks>
    internal bool Commit()
    {
        Problem.Visibility = Visibility.Collapsed;

        if (!TryInches(LeftBox, "left indent", out var left)
            || !TryInches(RightBox, "right indent", out var right)
            || !TryInches(FirstLineBox, "first line indent", out var firstLine)
            || !TryPoints(BeforeBox, "space before", out var before)
            || !TryPoints(AfterBox, "space after", out var after)
            || !TryLineSpacing(out var lineSpacing))
        {
            return false;
        }

        Result = new FormatCommands.ParagraphLook
        {
            Align = AlignBox.SelectedIndex switch
            {
                0 => Align.Left,
                1 => Align.Center,
                2 => Align.Right,
                3 => Align.Justify,
                _ => null,
            },
            LeftPt = left,
            RightPt = right,
            FirstLinePt = firstLine,
            BeforePt = before,
            AfterPt = after,
            LineSpacing = lineSpacing,
        };

        return true;
    }

    private bool TryInches(TextBox box, string which, out double? points)
    {
        points = null;

        if (string.IsNullOrWhiteSpace(box.Text))
        {
            return true;
        }

        if (!double.TryParse(box.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var inches))
        {
            return Refuse(box, $"Give the {which} as a number of inches, or leave it blank.");
        }

        if (inches < MinIndentInches || inches > MaxIndentInches)
        {
            return Refuse(
                box,
                $"The {which} has to be between {MinIndentInches} and {MaxIndentInches} inches.");
        }

        points = inches * PointsPerInch;

        return true;
    }

    private bool TryPoints(TextBox box, string which, out double? points)
    {
        points = null;

        if (string.IsNullOrWhiteSpace(box.Text))
        {
            return true;
        }

        if (!double.TryParse(box.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var value))
        {
            return Refuse(box, $"Give the {which} as a number of points, or leave it blank.");
        }

        if (value < 0 || value > MaxSpacingPoints)
        {
            return Refuse(box, $"The {which} has to be between 0 and {MaxSpacingPoints} points.");
        }

        points = value;

        return true;
    }

    private bool TryLineSpacing(out double? spacing)
    {
        spacing = null;

        var text = LineSpacingBox.Text?.Trim() ?? "";

        if (text.Length == 0)
        {
            return true;
        }

        if (string.Equals(text, Automatic, StringComparison.CurrentCultureIgnoreCase))
        {
            // NaN is WPF's own word for an automatic line height, and
            // FormatCommands.ApplyParagraph reads it that way.
            spacing = double.NaN;

            return true;
        }

        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var multiple))
        {
            return Refuse(
                null,
                $"Give the line spacing as a multiple such as 1.5, or as \"{Automatic}\".");
        }

        if (multiple < MinLineSpacing || multiple > MaxLineSpacing)
        {
            return Refuse(
                null,
                $"The line spacing has to be between {MinLineSpacing} and {MaxLineSpacing}.");
        }

        spacing = multiple;

        return true;
    }

    private bool Refuse(TextBox? box, string message)
    {
        Problem.Text = message;
        Problem.Visibility = Visibility.Visible;

        if (box is not null)
        {
            box.Focus();
            box.SelectAll();
        }
        else
        {
            LineSpacingBox.Focus();
        }

        return false;
    }
}
