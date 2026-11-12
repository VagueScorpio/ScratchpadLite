using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ScratchpadLite.App.Editor;
using ScratchpadLite.App.Fonts;

namespace ScratchpadLite.App.UI.Dialogs;

/// <summary>
/// A character grid for the current font, with recently used characters kept
/// at the top (Section 8.1, Insert, Special Character).
/// </summary>
/// <remarks>
/// <para>The grid is drawn in the font the caret is sitting in, and shows
/// only the characters that font has - <see cref="CharacterPalette"/> does
/// the asking. A grid of empty boxes would be worse than no grid, because
/// every one of them would insert a character the document cannot
/// show.</para>
///
/// <para>The dialog does not insert anything itself. It raises
/// <see cref="CharacterChosen"/> and the window puts the character in the
/// document, for the same reason AutoSave does not save: the one piece of
/// code that writes to the document stays the one piece of code that writes
/// to the document, undo included.</para>
/// </remarks>
public partial class SpecialChars : Window
{
    private readonly List<string> _recent = [];

    private string _family = "Calibri";

    public SpecialChars()
    {
        InitializeComponent();

        foreach (var group in CharacterPalette.Groups)
        {
            GroupBox.Items.Add(group.Name);
        }

        GroupBox.SelectedIndex = 0;

        Loaded += (_, _) => GroupBox.Focus();
    }

    /// <summary>Raised when the user asks for a character to be inserted.</summary>
    internal event EventHandler<string>? CharacterChosen;

    /// <summary>The character the grid is sitting on, or an empty string.</summary>
    internal string Selected { get; private set; } = "";

    /// <summary>The recently used list, newest first, to be saved in settings.</summary>
    internal IReadOnlyList<string> Recent => _recent;

    /// <summary>
    /// Opens the grid on a font, with the recently used characters from
    /// settings.
    /// </summary>
    internal void Show(string fontFamily, IEnumerable<string> recent)
    {
        ArgumentNullException.ThrowIfNull(recent);

        _family = string.IsNullOrWhiteSpace(fontFamily) ? "Calibri" : fontFamily.Trim();

        _recent.Clear();
        _recent.AddRange(recent.Where(c => !string.IsNullOrEmpty(c)).Take(CharacterPalette.RecentLimit));

        Title = $"Special Character - {_family}";

        FillRecent();
        FillGrid();
    }

    /// <summary>
    /// Picks a character as though it had been clicked. Here so a test can
    /// drive the dialog without a mouse.
    /// </summary>
    internal void Pick(string character)
    {
        Selected = character ?? "";
        InsertButton.IsEnabled = Selected.Length > 0;

        Chosen.Text = Selected.Length > 0
            ? $"{Selected}      {CharacterPalette.Describe(Selected)}"
            : "Pick a character.";
    }

    /// <summary>
    /// Inserts the selected character: raises the event, moves it to the top
    /// of the recently used list, and leaves the dialog open.
    /// </summary>
    internal void InsertSelected()
    {
        if (Selected.Length == 0)
        {
            return;
        }

        CharacterChosen?.Invoke(this, Selected);

        var kept = CharacterPalette.Remember(_recent, Selected);

        _recent.Clear();
        _recent.AddRange(kept);

        FillRecent();
    }

    private void OnGroupChanged(object sender, SelectionChangedEventArgs e) => FillGrid();

    private void OnInsert(object sender, RoutedEventArgs e) => InsertSelected();

    private void FillGrid()
    {
        if (CharacterGrid is null || GroupBox.SelectedIndex < 0)
        {
            return;
        }

        var group = CharacterPalette.Groups[GroupBox.SelectedIndex];
        var characters = CharacterPalette.CharactersIn(group, _family);

        CharacterGrid.Children.Clear();

        foreach (var character in characters)
        {
            CharacterGrid.Children.Add(Cell(character));
        }

        NothingHere.Text = $"{_family} has no characters in {group.Name}. "
            + "Try another range, or another font.";

        NothingHere.Visibility = characters.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void FillRecent()
    {
        if (RecentGrid is null)
        {
            return;
        }

        RecentGrid.Children.Clear();

        foreach (var character in _recent)
        {
            RecentGrid.Children.Add(Cell(character));
        }

        RecentPanel.Visibility = _recent.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private Button Cell(string character)
    {
        var button = new Button
        {
            Content = character,
            Style = (Style)Resources["CharacterCell"],
            FontFamily = FontRegistry.Resolve(_family),
            ToolTip = CharacterPalette.Describe(character),
            Tag = character,
        };

        button.Click += (_, _) => Pick(character);

        // A double-click inserts, which is what a grid of things to insert
        // ought to do; the Insert button is there for the keyboard. This is
        // the preview event and a click count rather than MouseDoubleClick,
        // because ButtonBase marks the button-down as handled and the
        // double-click event never arrives.
        button.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount != 2)
            {
                return;
            }

            Pick(character);
            InsertSelected();
        };

        return button;
    }
}
