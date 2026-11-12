using System.Windows;
using System.Windows.Controls;

namespace ScratchpadLite.App.UI.Dialogs;

/// <summary>
/// What to do about every modified document when the window is closing
/// (Section 6.12).
/// </summary>
/// <remarks>
/// Cancel is the answer if the dialog is dismissed any other way - by Escape,
/// or by closing it - because the one thing that must never happen by accident
/// is losing the work.
/// </remarks>
public partial class UnsavedChanges : Window
{
    public UnsavedChanges() => InitializeComponent();

    internal enum Answer { Cancel, Save, Discard }

    internal Answer Result { get; private set; } = Answer.Cancel;

    internal void Describe(IEnumerable<string> titles)
    {
        ArgumentNullException.ThrowIfNull(titles);

        var names = titles.ToList();

        Heading.Text = names.Count == 1
            ? "This document has changes you have not saved:"
            : $"These {names.Count} documents have changes you have not saved:";

        foreach (var name in names)
        {
            Names.Items.Add(new TextBlock
            {
                Text = $"•  {name}",
                Margin = new Thickness(0, 2, 0, 2),
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
        }
    }

    private void OnSaveAll(object sender, RoutedEventArgs e) => Finish(Answer.Save);

    private void OnDiscard(object sender, RoutedEventArgs e) => Finish(Answer.Discard);

    private void OnCancel(object sender, RoutedEventArgs e) => Finish(Answer.Cancel);

    private void Finish(Answer answer)
    {
        Result = answer;
        DialogResult = answer != Answer.Cancel;
    }
}
