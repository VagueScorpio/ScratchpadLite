using System.Windows;
using System.Windows.Controls;

namespace ScratchpadLite.App.UI.Dialogs;

/// <summary>
/// The Ctrl+K dialog: the address, the text to show, and Remove Link when
/// there is already one at the caret (Section 6.9).
/// </summary>
/// <remarks>
/// An address typed with no scheme gets <c>https://</c>, because "example.com"
/// is what people type and a link that does not work is worse than one that
/// assumed. Anything that already has a scheme is left exactly as typed - a
/// guess about a <c>mailto:</c> or a network path would be a wrong one.
/// </remarks>
public partial class InsertLink : Window
{
    public InsertLink()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            UrlBox.Focus();
            UrlBox.SelectAll();
        };
    }

    internal string Url { get; private set; } = "";

    internal string DisplayText { get; private set; } = "";

    /// <summary>True when the user pressed Remove Link.</summary>
    internal bool Removed { get; private set; }

    /// <summary>
    /// Fills the dialog in for what is at the caret.
    /// </summary>
    /// <param name="url">The existing address, or empty.</param>
    /// <param name="text">The existing or selected text, or empty.</param>
    /// <param name="editing">True when there is a link at the caret to change.</param>
    internal void Describe(string url, string text, bool editing)
    {
        UrlBox.Text = url ?? "";
        TextBox.Text = text ?? "";

        RemoveButton.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
        Title = editing ? "Edit Link" : "Insert Link";

        UpdateNote();
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        var typed = UrlBox.Text.Trim();

        if (typed.Length == 0)
        {
            Problem.Text = "Give an address for the link.";
            Problem.Visibility = Visibility.Visible;
            UrlBox.Focus();

            return;
        }

        Url = WithScheme(typed);
        DisplayText = TextBox.Text;
        DialogResult = true;
    }

    private void OnRemove(object sender, RoutedEventArgs e)
    {
        Removed = true;
        DialogResult = true;
    }

    private void OnUrlChanged(object sender, TextChangedEventArgs e) => UpdateNote();

    /// <summary>
    /// Says what will happen when the link is clicked, before it is made.
    /// </summary>
    /// <remarks>
    /// Section 6.9 only opens http, https and mailto without asking. Saying so
    /// here means the one-off question later is not a surprise.
    /// </remarks>
    private void UpdateNote()
    {
        if (Note is null)
        {
            return;     // during InitializeComponent
        }

        var typed = UrlBox.Text.Trim();

        if (typed.Length == 0)
        {
            Note.Text = "Web addresses can be typed without https://.";

            return;
        }

        var full = WithScheme(typed);

        Note.Text = Uri.TryCreate(full, UriKind.Absolute, out var uri)
            ? uri.Scheme.ToLowerInvariant() switch
            {
                "http" or "https" => "Ctrl+click will open this in your browser.",
                "mailto" => "Ctrl+click will open this in your mail program.",
                _ => $"This is a {uri.Scheme} link. Ctrl+click will ask before opening it.",
            }
            : "Windows may not be able to open this address.";
    }

    private static string WithScheme(string typed) =>
        typed.Contains("://", StringComparison.Ordinal)
        || typed.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)
        || typed.StartsWith(@"\\", StringComparison.Ordinal)
            ? typed
            : "https://" + typed;
}
