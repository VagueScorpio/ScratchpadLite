using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ScratchpadLite.App.Editor;

namespace ScratchpadLite.App.UI.Dialogs;

/// <summary>
/// The find and replace bar (Section 6.10). A bar rather than a dialog, so
/// the document stays visible and the caret keeps its place. Match case,
/// whole word, wrap around, replace, replace all, and the match count.
/// Replace all is one undo unit.
/// </summary>
/// <remarks>
/// The bar holds the options and the text, and raises what the user asked for.
/// It does not search: the search needs the active editor and, for "all open
/// tabs", the other tabs' documents, and all of that belongs to the window.
/// Keeping it out of here is also what makes <see cref="FindEngine"/> testable
/// without a window.
/// </remarks>
public partial class FindReplaceBar : UserControl
{
    public FindReplaceBar() => InitializeComponent();

    /// <summary>Find next or previous.</summary>
    internal event EventHandler<bool>? SearchRequested;

    /// <summary>Replace the current match and move on.</summary>
    internal event EventHandler? ReplaceRequested;

    /// <summary>Replace every match, as one undo step.</summary>
    internal event EventHandler? ReplaceAllRequested;

    /// <summary>
    /// The text or an option changed, so the count is stale. Also the
    /// find-as-you-type trigger.
    /// </summary>
    internal event EventHandler? OptionsChanged;

    /// <summary>The Close button or Escape.</summary>
    internal event EventHandler? CloseRequested;

    /// <summary>What to look for, as the bar currently has it.</summary>
    internal FindEngine.Options Options => new(
        FindBox.Text,
        MatchCaseBox.IsChecked == true,
        WholeWordBox.IsChecked == true,
        RegexBox.IsChecked == true);

    /// <summary>What to put in its place.</summary>
    internal string Replacement => ReplaceBox.Text;

    /// <summary>Whether the search covers every open tab (Section 6.10).</summary>
    internal bool AllTabs => AllTabsBox.IsChecked == true;

    /// <summary>
    /// Shows the bar in Find mode or Replace mode, with the selected text
    /// already in the box - which is what the user has almost always just
    /// selected in order to search for it.
    /// </summary>
    internal void Open(bool replacing, string? selected)
    {
        var replaceVisibility = replacing ? Visibility.Visible : Visibility.Collapsed;

        ReplaceLabel.Visibility = replaceVisibility;
        ReplaceBox.Visibility = replaceVisibility;
        ReplaceButtons.Visibility = replaceVisibility;

        Visibility = Visibility.Visible;

        // Only a selection worth searching for: one that crosses paragraphs
        // can never be a match, so putting it in the box would guarantee
        // "No matches".
        if (selected is { Length: > 0 } && !selected.Contains('\r') && !selected.Contains('\n'))
        {
            FindBox.Text = selected;
        }

        FindBox.Focus();
        FindBox.SelectAll();
    }

    /// <summary>"3 of 17", or what to say when there is nothing (Section 6.10).</summary>
    internal void ShowCount(int index, int total)
    {
        CountText.Text = total switch
        {
            0 when FindBox.Text.Length == 0 => string.Empty,
            0 => "No matches",
            _ => string.Format(CultureInfo.CurrentCulture, "{0} of {1:N0}", index + 1, total),
        };

        MessageText.Text = string.Empty;
    }

    /// <summary>
    /// A message instead of a count: an invalid regular expression, or how many
    /// were replaced.
    /// </summary>
    internal void ShowMessage(string message)
    {
        MessageText.Text = message;
        CountText.Text = string.Empty;
    }

    /// <summary>Puts the caret back in the find box, after a Replace All.</summary>
    internal void FocusFindBox() => FindBox.Focus();

    private void OnFindTextChanged(object sender, TextChangedEventArgs e) =>
        OptionsChanged?.Invoke(this, EventArgs.Empty);

    private void OnOptionChanged(object sender, RoutedEventArgs e) =>
        OptionsChanged?.Invoke(this, EventArgs.Empty);

    private void OnNextClick(object sender, RoutedEventArgs e) =>
        SearchRequested?.Invoke(this, false);

    private void OnPreviousClick(object sender, RoutedEventArgs e) =>
        SearchRequested?.Invoke(this, true);

    private void OnReplaceClick(object sender, RoutedEventArgs e) =>
        ReplaceRequested?.Invoke(this, EventArgs.Empty);

    private void OnReplaceAllClick(object sender, RoutedEventArgs e) =>
        ReplaceAllRequested?.Invoke(this, EventArgs.Empty);

    private void OnCloseClick(object sender, RoutedEventArgs e) =>
        CloseRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Enter searches, Shift+Enter searches backwards, and Enter in the Replace
    /// box replaces - the keys anyone would try without being told.
    /// </summary>
    private void OnBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        if (ReferenceEquals(sender, ReplaceBox))
        {
            ReplaceRequested?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            SearchRequested?.Invoke(this, Keyboard.Modifiers == ModifierKeys.Shift);
        }

        e.Handled = true;
    }

    /// <summary>
    /// Escape closes the bar from anywhere inside it.
    /// </summary>
    /// <remarks>
    /// Handled here rather than on the two text boxes, because the focus could
    /// be on a check box or a button - and Escape would then reach the window,
    /// where it means "cancel the format painter", and the bar would stay open.
    /// </remarks>
    private void OnBarKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
        {
            return;
        }

        CloseRequested?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }
}
