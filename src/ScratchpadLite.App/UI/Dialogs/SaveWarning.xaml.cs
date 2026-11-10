using System.Windows;

namespace ScratchpadLite.App.UI.Dialogs;

/// <summary>
/// The lossy-format warning (Section 7.1). It names exactly what this format
/// will drop, and offers Save anyway, Save as DOCX instead, or Cancel, with a
/// per-format do-not-warn-again box.
/// </summary>
/// <remarks>
/// Shown only when this document actually uses something the format cannot
/// hold. A warning that lists what a format cannot do in general - rather than
/// what this file would lose - gets dismissed by reflex, and then so does the
/// one that mattered.
/// </remarks>
public partial class SaveWarning : Window
{
    internal enum Answer
    {
        Cancel,
        SaveAnyway,
        SaveAsDocx,
    }

    public SaveWarning() => InitializeComponent();

    internal Answer Result { get; private set; } = Answer.Cancel;

    /// <summary>True when the user asked not to be warned about this format again.</summary>
    internal bool Silence => DoNotWarn.IsChecked == true;

    /// <summary>
    /// Sets the dialog up for one save.
    /// </summary>
    /// <param name="formatName">The format being saved to, for the sentence.</param>
    /// <param name="lost">What this document would lose.</param>
    /// <param name="offerDocx">
    /// Whether "Save as DOCX instead" makes sense - it does not when the
    /// format being saved to is DOCX.
    /// </param>
    internal void Describe(string formatName, IReadOnlyList<string> lost, bool offerDocx)
    {
        ArgumentNullException.ThrowIfNull(lost);

        Headline.Text = $"Saving as {formatName} will lose:";
        LostList.ItemsSource = lost.Select(item => "•  " + item).ToList();

        SaveAsDocxButton.Visibility = offerDocx ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnSaveAnyway(object sender, RoutedEventArgs e)
    {
        Result = Answer.SaveAnyway;
        DialogResult = true;
    }

    private void OnSaveAsDocx(object sender, RoutedEventArgs e)
    {
        Result = Answer.SaveAsDocx;
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        Result = Answer.Cancel;
        DialogResult = false;
    }
}
