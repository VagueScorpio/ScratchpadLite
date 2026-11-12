using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ScratchpadLite.App.Editor;

namespace ScratchpadLite.App.UI.Dialogs;

/// <summary>
/// Rows, columns, and whether to draw borders (Section 6.9).
/// </summary>
/// <remarks>
/// The numbers are clamped rather than merely complained about, because a
/// table of 4000 rows would be a document nobody can scroll and a limit that
/// only scolds is a limit the user has to satisfy by guessing.
/// </remarks>
public partial class InsertTable : Window
{
    public InsertTable()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            RowsBox.Focus();
            RowsBox.SelectAll();
        };
    }

    internal int Rows { get; private set; } = 2;

    internal int Columns { get; private set; } = 2;

    internal bool Borders { get; private set; } = true;

    private void OnInsert(object sender, RoutedEventArgs e)
    {
        if (!TryRead(RowsBox, TableCommands.MaxRows, "rows", out var rows)
            || !TryRead(ColumnsBox, TableCommands.MaxColumns, "columns", out var columns))
        {
            return;
        }

        Rows = rows;
        Columns = columns;
        Borders = BordersBox.IsChecked == true;
        DialogResult = true;
    }

    private bool TryRead(TextBox box, int max, string what, out int value)
    {
        if (!int.TryParse(box.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out value)
            || value < 1)
        {
            Complain($"Give a number of {what} of 1 or more.");
            box.Focus();
            box.SelectAll();

            return false;
        }

        if (value > max)
        {
            Complain($"The most this can insert is {max} {what}.");
            box.Text = max.ToString(CultureInfo.CurrentCulture);
            box.Focus();
            box.SelectAll();
            value = max;

            return false;
        }

        return true;
    }

    private void Complain(string message)
    {
        Problem.Text = message;
        Problem.Visibility = Visibility.Visible;
    }

    private void OnDigitsOnly(object sender, TextCompositionEventArgs e) =>
        e.Handled = !e.Text.All(char.IsAsciiDigit);
}
