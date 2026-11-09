using System.Globalization;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ScratchpadLite.Core.Platform;

namespace ScratchpadLite.App.UI.Controls;

/// <summary>
/// The font picker (Section 6.7).
/// </summary>
/// <remarks>
/// Section 2.1 is why the list is empty until the dropdown is first opened:
/// <c>Fonts.SystemFontFamilies</c> enumerates every font installed on the PC,
/// and doing that during startup would spend the budget before the window
/// appeared. The first open pays for it, and only once.
/// <para>
/// Phase 1 shows the Windows fonts. Phase 2 adds the font-folder group, the
/// recently-used group, the setting that draws each name in its own font, and
/// the missing-font row.
/// </para>
/// </remarks>
public partial class FontPicker : UserControl
{
    private List<FontEntry>? _allFonts;
    private bool _suppress;

    public FontPicker() => InitializeComponent();

    /// <summary>Raised when the user picks or types a font family that exists.</summary>
    internal event EventHandler<string>? FontChosen;

    /// <summary>
    /// Whether each name is drawn in its own font (the Section 6.2 setting). Off
    /// makes the list instant on a PC with a very large font collection.
    /// </summary>
    internal bool PreviewInOwnFont { get; set; } = true;

    /// <summary>
    /// Shows the family at the caret. A null means the selection spans more
    /// than one font, and the box goes blank.
    /// </summary>
    internal void Show(string? family)
    {
        _suppress = true;
        try
        {
            Box.SelectedItem = null;
            Box.Text = family ?? string.Empty;
        }
        finally
        {
            _suppress = false;
        }
    }

    /// <summary>
    /// Loads the font list if it has not been loaded yet. Called on the first
    /// dropdown open, and available for the idle-time preload in Phase 2.
    /// </summary>
    internal void EnsureLoaded()
    {
        if (_allFonts is not null)
        {
            return;
        }

        var started = DateTime.UtcNow;

        // Fully qualified: a bare "Fonts" would bind to this app's own
        // ScratchpadLite.App.Fonts namespace, not to WPF's font list.
        _allFonts = System.Windows.Media.Fonts.SystemFontFamilies
            .Select(f => new FontEntry(FamilyName(f), PreviewInOwnFont))
            .Where(e => e.Name.Length > 0)
            .GroupBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        Box.ItemsSource = _allFonts;

        Log.Info(
            $"font picker: {_allFonts.Count} Windows families in "
            + $"{(DateTime.UtcNow - started).TotalMilliseconds:F0} ms");
    }

    /// <summary>
    /// The family's name in the user's own language where the font provides one,
    /// falling back to its invariant name.
    /// </summary>
    private static string FamilyName(FontFamily family)
    {
        var names = family.FamilyNames;

        foreach (var tag in new[] { CultureInfo.CurrentUICulture.IetfLanguageTag, "en-us" })
        {
            var key = System.Windows.Markup.XmlLanguage.GetLanguage(tag);
            if (names.TryGetValue(key, out var localised))
            {
                return localised;
            }
        }

        return names.Values.FirstOrDefault() ?? family.Source;
    }

    private void OnDropDownOpened(object sender, EventArgs e) => EnsureLoaded();

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_suppress && Box.SelectedItem is FontEntry entry)
        {
            FontChosen?.Invoke(this, entry.Name);
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
            Box.Text = string.Empty;
            e.Handled = true;
        }
        else
        {
            // Typing filters the list. Deferred to after the keystroke is
            // applied, so Box.Text already holds the new text.
            Dispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.Background,
                (Action)Filter);
        }
    }

    private void OnLostFocus(object sender, System.Windows.RoutedEventArgs e) => CommitTypedValue();

    /// <summary>Case-insensitive contains match (Section 6.7).</summary>
    private void Filter()
    {
        if (_allFonts is null || !Box.IsDropDownOpen)
        {
            return;
        }

        var text = Box.Text.Trim();

        Box.ItemsSource = text.Length == 0
            ? _allFonts
            : _allFonts.Where(f => f.Name.Contains(text, StringComparison.CurrentCultureIgnoreCase)).ToList();
    }

    /// <summary>Applies the typed name if such a family exists.</summary>
    private void CommitTypedValue()
    {
        if (_suppress || string.IsNullOrWhiteSpace(Box.Text))
        {
            return;
        }

        EnsureLoaded();

        var typed = Box.Text.Trim();
        var match = _allFonts?.FirstOrDefault(
            f => string.Equals(f.Name, typed, StringComparison.CurrentCultureIgnoreCase));

        if (match is not null)
        {
            FontChosen?.Invoke(this, match.Name);
        }
    }

    /// <summary>One row of the dropdown.</summary>
    internal sealed class FontEntry(string name, bool previewInOwnFont)
    {
        public string Name { get; } = name;

        /// <summary>
        /// What the row's name is drawn in. With previews off this is the UI
        /// font, which means WPF never opens the font file to draw the row.
        /// </summary>
        public string PreviewFamily { get; } = previewInOwnFont ? name : "Segoe UI";
    }
}
