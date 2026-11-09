using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using ScratchpadLite.App.Fonts;
using ScratchpadLite.Core.Platform;

namespace ScratchpadLite.App.UI.Controls;

/// <summary>
/// The font picker (Section 6.7).
/// </summary>
/// <remarks>
/// Section 2.1 is why the list is empty until the dropdown is first opened:
/// the Windows font list enumerates every font installed on the PC, and doing
/// that during startup would spend the budget before the window appeared. The
/// first open pays for it, and only once.
/// <para>
/// Three groups, in this order: the font folder (what the user put there on
/// purpose), recently used, then everything. The font-folder group comes first
/// because a font someone dropped in is the one they are looking for.
/// </para>
/// </remarks>
public partial class FontPicker : UserControl
{
    private const string FolderGroup = "Font folder";
    private const string RecentGroup = "Recently used";
    private const string AllGroup = "All fonts";

    /// <summary>
    /// Shown beside a font that cannot draw its own name. Han, hiragana and
    /// Hangul between them cover most of the fonts this applies to.
    /// </summary>
    private const string Sample = "文字 あ 가";

    private List<FontEntry>? _entries;
    private bool _suppress;
    private string? _shown;

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
            _shown = family;
            Box.SelectedItem = null;
            Box.Text = family ?? string.Empty;

            // A font the document asked for and this PC does not have is shown
            // in italics with a warning glyph, rather than silently looking
            // like any other font (Section 6.7).
            var missing = family is not null && !FontRegistry.IsAvailable(family);

            Box.FontStyle = missing ? FontStyles.Italic : FontStyles.Normal;
            MissingGlyph.Visibility = missing ? Visibility.Visible : Visibility.Collapsed;
        }
        finally
        {
            _suppress = false;
        }
    }

    /// <summary>
    /// Rebuilds the list after the font folder check has added families
    /// (Section 6.7). Cheap, and batched by FontRegistry to at most once a
    /// second while fonts are still being prepared.
    /// </summary>
    internal void Refresh()
    {
        if (_entries is null)
        {
            return;        // never opened, so there is nothing to rebuild
        }

        Build();
        Show(_shown);
    }

    /// <summary>
    /// Loads the font list if it has not been loaded yet. Called on the first
    /// dropdown open.
    /// </summary>
    internal void EnsureLoaded()
    {
        if (_entries is not null)
        {
            return;
        }

        var started = System.Diagnostics.Stopwatch.StartNew();

        Build();

        Log.Info(
            $"font picker: {_entries!.Count} rows built in {started.ElapsedMilliseconds} ms "
            + $"(previews {(PreviewInOwnFont ? "on" : "off")})");
    }

    private void Build()
    {
        var folder = FontRegistry.FolderFamilyNames();
        var recent = FontRegistry.Recent();
        var all = FontRegistry.AllFamilies;

        var entries = new List<FontEntry>(folder.Count + recent.Count + all.Count);

        entries.AddRange(folder.Select(name => Row(name, FolderGroup)));
        entries.AddRange(recent.Select(name => Row(name, RecentGroup)));
        entries.AddRange(all.Select(name => Row(name, AllGroup)));

        _entries = entries;
        Apply(entries);
    }

    private FontEntry Row(string name, string group) =>
        new(name, group, PreviewInOwnFont, FontRegistry.HasLatinLetters(name));

    /// <summary>
    /// Puts the rows into the box as a grouped view. The grouping has to be on
    /// the view rather than done by hand, because that is what the combo box's
    /// GroupStyle and the virtualizing panel both work from.
    /// </summary>
    private void Apply(List<FontEntry> entries)
    {
        var source = new CollectionViewSource { Source = entries };
        source.GroupDescriptions.Add(new PropertyGroupDescription(nameof(FontEntry.Group)));

        Box.ItemsSource = source.View;
    }

    /// <summary>The group headers now in the list, in order. For the tests.</summary>
    internal IReadOnlyList<string> GroupNames() =>
        Box.ItemsSource is ICollectionView { Groups: { } groups }
            ? groups.OfType<CollectionViewGroup>().Select(g => g.Name?.ToString() ?? "").ToList()
            : [];

    private void OnDropDownOpened(object sender, EventArgs e) => EnsureLoaded();

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_suppress && Box.SelectedItem is FontEntry entry)
        {
            FontRegistry.NoteUsed(entry.Name);
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
            Show(_shown);
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

    private void OnLostFocus(object sender, RoutedEventArgs e) => CommitTypedValue();

    /// <summary>Case-insensitive contains match (Section 6.7).</summary>
    private void Filter()
    {
        if (_entries is null || !Box.IsDropDownOpen)
        {
            return;
        }

        var text = Box.Text.Trim();

        if (Box.ItemsSource is ICollectionView view)
        {
            // Filtering the view rather than rebuilding it keeps the groups and
            // the virtualized rows in place.
            view.Filter = text.Length == 0
                ? null
                : item => item is FontEntry entry
                          && entry.Name.Contains(text, StringComparison.CurrentCultureIgnoreCase);
        }
    }

    /// <summary>Applies the typed name if such a family exists.</summary>
    private void CommitTypedValue()
    {
        if (_suppress || string.IsNullOrWhiteSpace(Box.Text))
        {
            return;
        }

        var typed = Box.Text.Trim();

        if (string.Equals(typed, _shown, StringComparison.CurrentCultureIgnoreCase))
        {
            return;
        }

        // Asked of the registry rather than the dropdown list, so a name typed
        // before the list has ever been built still works.
        if (FontRegistry.IsAvailable(typed))
        {
            FontRegistry.NoteUsed(typed);
            FontChosen?.Invoke(this, typed);
            return;
        }

        // Not a font on this PC: put back what was there rather than leaving a
        // name that does nothing.
        Show(_shown);
    }

    /// <summary>One row of the dropdown.</summary>
    internal sealed class FontEntry(string name, string group, bool previewInOwnFont, bool hasLatinLetters)
    {
        public string Name { get; } = name;

        public string Group { get; } = group;

        /// <summary>The family itself, for the sample text.</summary>
        public string OwnFamily { get; } = name;

        /// <summary>
        /// What the row's name is drawn in. With previews off, or for a font
        /// that has no Latin letters to draw its name with, this is the UI
        /// font - which also means WPF never opens the font file for that row.
        /// </summary>
        public string PreviewFamily { get; } =
            previewInOwnFont && hasLatinLetters ? name : "Segoe UI";

        /// <summary>A short sample, shown only for fonts that cannot draw their own name.</summary>
        public string Sample { get; } = previewInOwnFont && !hasLatinLetters ? FontPicker.Sample : "";

        public Visibility SampleVisibility { get; } =
            previewInOwnFont && !hasLatinLetters ? Visibility.Visible : Visibility.Collapsed;

        public FontStyle NameStyle { get; } = FontStyles.Normal;
    }
}
