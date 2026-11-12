using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using ScratchpadLite.App.Services;

namespace ScratchpadLite.App.UI.Dialogs;

/// <summary>
/// Shown when a previous run left recovery files behind (Section 6.13.1). It
/// lists what was found with its timestamp and lets the user open or discard
/// each one. Nothing is ever discarded without being asked.
/// </summary>
/// <remarks>
/// The fourth button, Later, is not in Section 6.13.1 and is here on purpose.
/// Restore / Restore All / Discard are the three things to do about the files,
/// but a dialog whose Escape key means "throw my work away" is a dialog that
/// eventually throws someone's work away. Closing this one leaves everything
/// on disk and offers it again next launch, which is the only answer that
/// cannot be regretted.
/// </remarks>
public partial class RecoveryDialog : Window
{
    private readonly List<(CheckBox Box, Recovery.Leftover Leftover)> _rows = [];

    public RecoveryDialog() => InitializeComponent();

    internal enum Answer
    {
        /// <summary>Leave the files alone and ask again next time.</summary>
        Later,

        /// <summary>Open the ticked documents.</summary>
        Restore,

        /// <summary>Delete every recovery file listed.</summary>
        Discard,
    }

    internal Answer Result { get; private set; } = Answer.Later;

    /// <summary>The documents the user ticked. Only meaningful for Restore.</summary>
    internal IReadOnlyList<Recovery.Leftover> Chosen { get; private set; } = [];

    /// <summary>Everything that was offered, for Discard to clean up.</summary>
    internal IReadOnlyList<Recovery.Leftover> All { get; private set; } = [];

    /// <summary>Fills the list. Every row starts ticked.</summary>
    internal void Describe(IReadOnlyList<Recovery.Leftover> leftovers)
    {
        ArgumentNullException.ThrowIfNull(leftovers);

        All = leftovers;
        _rows.Clear();
        Items.Items.Clear();

        foreach (var leftover in leftovers)
        {
            var box = new CheckBox
            {
                IsChecked = true,
                Margin = new Thickness(0, 4, 0, 4),
                Content = Describe(leftover),
                ToolTip = leftover.OriginalPath ?? "This document had never been saved.",
            };

            _rows.Add((box, leftover));
            Items.Items.Add(box);
        }

        RestoreButton.IsEnabled = leftovers.Count > 0;
    }

    /// <summary>
    /// One row: the name, where it came from, and when it was saved.
    /// </summary>
    /// <remarks>
    /// The time is shown as the user's local time even though it is stored in
    /// UTC. "Saved 14:32" is a fact someone can check against their own memory
    /// of when the power went out; the same moment in UTC is not.
    /// </remarks>
    private static StackPanel Describe(Recovery.Leftover leftover)
    {
        var panel = new StackPanel();

        panel.Children.Add(new TextBlock
        {
            Text = leftover.Title,
            FontWeight = FontWeights.SemiBold,
        });

        var where = leftover.OriginalPath is { } path
            ? Path.GetDirectoryName(path) is { Length: > 0 } folder ? folder : path
            : "never saved to a file";

        panel.Children.Add(new TextBlock
        {
            Text = $"{leftover.FormatName} · {where}",
            Opacity = 0.75,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });

        panel.Children.Add(new TextBlock
        {
            Text = $"Saved {leftover.SavedUtc.ToLocalTime().ToString("f", CultureInfo.CurrentCulture)}",
            Opacity = 0.75,
        });

        return panel;
    }

    private void OnRestore(object sender, RoutedEventArgs e) => DialogResult = ChooseRestore();

    /// <summary>
    /// Takes the ticked rows (Restore) or all of them (Restore All).
    /// </summary>
    /// <returns>True when there is something to restore.</returns>
    /// <remarks>
    /// Separate from the button handlers so a test can call it: setting
    /// <c>DialogResult</c> on a window that was not shown with
    /// <c>ShowDialog</c> throws.
    /// </remarks>
    internal bool ChooseRestore(bool everything = false)
    {
        Chosen = everything
            ? All
            : [.._rows.Where(r => r.Box.IsChecked == true).Select(r => r.Leftover)];

        if (Chosen.Count == 0)
        {
            // Nothing ticked and Restore pressed is a request to restore, not a
            // request to lose everything, so it is treated as Later.
            Result = Answer.Later;

            return false;
        }

        Result = Answer.Restore;

        return true;
    }

    private void OnRestoreAll(object sender, RoutedEventArgs e) => DialogResult = ChooseRestore(everything: true);

    private void OnDiscard(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show(
            this,
            All.Count == 1
                ? $"Throw away the recovered copy of {All[0].Title}?\n\nThis cannot be undone."
                : $"Throw away all {All.Count} recovered documents?\n\nThis cannot be undone.",
            "Recover Documents",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        Result = Answer.Discard;
        DialogResult = true;
    }

    private void OnLater(object sender, RoutedEventArgs e) => Result = Answer.Later;
}
