using System.Globalization;
using System.Windows.Controls;
using System.Windows.Threading;
using ScratchpadLite.App.Editor;

namespace ScratchpadLite.App.UI;

/// <summary>
/// The status bar (Section 6.18): counts, caret position, format, language,
/// zoom, the AutoSave switch, and temporary messages.
/// </summary>
/// <remarks>Phase 1. The AutoSave switch is enabled in Phase 8.</remarks>
public partial class StatusBar : UserControl
{
    private readonly DispatcherTimer _messageTimer;

    public StatusBar()
    {
        InitializeComponent();

        _messageTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(6),
        };
        _messageTimer.Tick += (_, _) =>
        {
            _messageTimer.Stop();
            MessageText.Text = string.Empty;
        };

        // A named handler, not a lambda: ShowZoomWithoutRaising has to detach it
        // to set the slider from code without looping back into the editor.
        ZoomSlider.ValueChanged += OnSliderChanged;

        ShowZoom(1.0);
        ShowCounts(0, 0, null);
        ShowCaret(1, 1);
        LanguageText.Text = "en-US";
        FormatText.Text = "TXT";
        ShowAutoSaveWithoutRaising(false, null);
    }

    /// <summary>Raised when the user drags the zoom slider.</summary>
    internal event EventHandler<double>? ZoomChanged;

    /// <summary>Raised when the user clicks the AutoSave switch (Section 6.13.2).</summary>
    internal event EventHandler<bool>? AutoSaveToggled;

    private bool _suppressAutoSave;

    /// <summary>
    /// Shows the AutoSave switch as it is, without raising the event.
    /// </summary>
    /// <param name="on">Whether AutoSave is on.</param>
    /// <param name="tooltip">
    /// Why it is not saving the document in front, when it is not
    /// (Section 6.13.2). Null keeps the plain explanation.
    /// </param>
    internal void ShowAutoSaveWithoutRaising(bool on, string? tooltip)
    {
        _suppressAutoSave = true;
        try
        {
            AutoSaveToggle.IsChecked = on;
            AutoSaveToggle.Content = on ? "AutoSave: On" : "AutoSave: Off";

            if (tooltip is { Length: > 0 })
            {
                AutoSaveToggle.ToolTip = tooltip;
            }
        }
        finally
        {
            _suppressAutoSave = false;
        }
    }

    private void OnAutoSaveToggled(object sender, System.Windows.RoutedEventArgs e)
    {
        if (!_suppressAutoSave)
        {
            AutoSaveToggled?.Invoke(this, AutoSaveToggle.IsChecked == true);
        }
    }

    internal void ShowZoomWithoutRaising(double zoom)
    {
        ZoomSlider.ValueChanged -= OnSliderChanged;
        ZoomSlider.Value = zoom;
        ShowZoom(zoom);
        ZoomSlider.ValueChanged += OnSliderChanged;
    }

    private void OnSliderChanged(object sender, System.Windows.RoutedPropertyChangedEventArgs<double> e)
    {
        ShowZoom(e.NewValue);
        ZoomChanged?.Invoke(this, e.NewValue);
    }

    private void ShowZoom(double zoom) =>
        ZoomText.Text = (zoom * 100).ToString("0", CultureInfo.CurrentCulture) + "%";

    /// <summary>
    /// Section 6.18: a selection shows "Words: 34 of 1,208", because the
    /// interesting number when text is selected is how much of it is selected.
    /// </summary>
    internal void ShowCounts(int words, int characters, int? selectedWords)
    {
        WordsText.Text = selectedWords is { } selected
            ? $"Words: {selected:N0} of {words:N0}"
            : $"Words: {words:N0}";

        CharactersText.Text = $"Characters: {characters:N0}";
    }

    internal void ShowCaret(int line, int column) => CaretText.Text = $"Ln {line}, Col {column}";

    internal void ShowFormat(string format) => FormatText.Text = format.ToUpperInvariant();

    internal void ShowLanguage(string language) => LanguageText.Text = language;

    /// <summary>
    /// A temporary message on the left, cleared after a few seconds. Used for
    /// "Preparing fonts 12/480", "Saved 10:42" and "New fonts added".
    /// </summary>
    internal void ShowMessage(string message, bool transient = true)
    {
        MessageText.Text = message;

        _messageTimer.Stop();
        if (transient && message.Length > 0)
        {
            _messageTimer.Start();
        }
    }

    internal void UpdateFrom(FormatState state) => ShowCaret(state.Line, state.Column);
}
