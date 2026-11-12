using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ScratchpadLite.App.Bridge;
using ScratchpadLite.Core.Model;
using ScratchpadLite.Core.Platform;

using Block = System.Windows.Documents.Block;
using Inline = System.Windows.Documents.Inline;
using Paragraph = System.Windows.Documents.Paragraph;

namespace ScratchpadLite.App.Editor;

/// <summary>
/// Hosts one <see cref="RichTextBox"/> (Section 6.8): the paper view, zoom, and
/// the formatting readout that drives the toolbar and status bar.
/// </summary>
/// <remarks>
/// Section 2.1 governs two things here. Nothing heavy may run on every
/// keystroke, so the selection readout runs at
/// <see cref="DispatcherPriority.Background"/> throttled to one per 50 ms, and
/// the word count waits for a 300 ms pause in typing. And spell check is not
/// switched on in the constructor: it loads the Windows spell checker, so
/// <see cref="EnableSpellCheck"/> is called after the first frame (Phase 7).
/// </remarks>
public partial class EditorView : UserControl
{
    /// <summary>Letter at 1 inch margins: 8.5 in x 96 DIP (Section 6.8).</summary>
    private const double PaperWidthDip = 8.5 * 96;

    private const double PageMarginDip = 96;
    private const double FullWidthPaddingDip = 24;

    internal const double MinZoom = 0.5;
    internal const double MaxZoom = 4.0;

    private readonly DispatcherTimer _wordCountTimer;
    private bool _formatReadoutQueued;
    private double _zoom = 1.0;
    private PageViewMode _pageView = PageViewMode.PageWidth;

    public EditorView()
    {
        InitializeComponent();

        _wordCountTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            // Section 6.18: count words after typing pauses, not per keystroke.
            Interval = TimeSpan.FromMilliseconds(300),
        };
        _wordCountTimer.Tick += OnWordCountTick;

        RemoveConflictingBuiltInBindings();
        ApplyPageView();
    }

    /// <summary>The hosted control. Every command in <see cref="FormatCommands"/> takes it.</summary>
    internal RichTextBox Editor => EditorBox;

    /// <summary>Raised after a selection change, throttled. Null means no document.</summary>
    internal event EventHandler<FormatState>? FormatStateChanged;

    /// <summary>Raised when the word and character count has been recounted.</summary>
    internal event EventHandler<(int Words, int Characters, int? SelectedWords)>? CountsChanged;

    /// <summary>Raised when the document is edited, so a tab can mark itself dirty.</summary>
    internal event EventHandler? DocumentChanged;

    internal double Zoom
    {
        get => _zoom;
        set
        {
            var clamped = Math.Clamp(value, MinZoom, MaxZoom);
            if (Math.Abs(clamped - _zoom) < 0.001)
            {
                return;
            }

            _zoom = clamped;
            ZoomTransform.ScaleX = clamped;
            ZoomTransform.ScaleY = clamped;
        }
    }

    internal PageViewMode PageView
    {
        get => _pageView;
        set
        {
            if (_pageView == value)
            {
                return;
            }

            _pageView = value;
            ApplyPageView();
        }
    }

    /// <summary>
    /// Section 6.16: the page stays white in every theme by default, like paper.
    /// Dark Paper is a separate setting because it is a matter of taste, not of
    /// following the system theme.
    /// </summary>
    internal bool DarkPaper
    {
        get;
        set
        {
            field = value;
            Paper.SetResourceReference(
                Border.BackgroundProperty, value ? "PaperBackgroundDark" : "PaperBackground");
            EditorBox.SetResourceReference(
                ForegroundProperty, value ? "PaperForegroundDark" : "PaperForeground");
        }
    }

    /// <summary>
    /// Applies the typing defaults from settings and gives the first paragraph
    /// its style name, so a brand new document is already a Normal paragraph
    /// rather than an unnamed one.
    /// </summary>
    internal void ApplyDefaults(Settings settings, StyleSheet styles)
    {
        var normal = styles[settings.DefaultParagraphStyle];

        EditorBox.FontFamily = new FontFamily(normal.Family ?? settings.DefaultFontFamily);
        EditorBox.FontSize = Units.PointsToDips(
            normal.SizePt > 0 ? normal.SizePt : settings.DefaultFontSizePt);

        Zoom = settings.Zoom;
        PageView = settings.PageView;
        DarkPaper = settings.DarkPaper;

        if (EditorBox.Document.Blocks.FirstBlock is Paragraph first)
        {
            SpProps.SetStyleName(first, normal.Name);
            FormatCommands.ApplyStyleToParagraph(first, normal, styles);
        }
    }

    /// <summary>
    /// Re-applies everything that belongs to the control rather than to the
    /// document, after the document has been replaced.
    /// </summary>
    /// <remarks>
    /// Setting <c>RichTextBox.Document</c> swaps the whole tree, and anything
    /// attached to the old one goes with it: the paste filter's handler, the
    /// zoom, the page view. Without this, opening a file silently turns off
    /// the paste filter and resets the zoom to 100%.
    /// </remarks>
    internal void ReattachToNewDocument(Settings settings, StyleSheet styles)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(styles);

        PasteFilter.Attach(EditorBox);

        Zoom = settings.Zoom;
        PageView = settings.PageView;
        DarkPaper = settings.DarkPaper;

        ApplyPageView();
        OnWordCountTick(this, EventArgs.Empty);
    }

    /// <summary>
    /// Switches on the spell checker. Called after the first frame, never in the
    /// constructor: turning it on loads the Windows spell checker (Section 2.1).
    /// </summary>
    internal void EnableSpellCheck(bool enabled, string language)
    {
        EditorBox.SpellCheck.IsEnabled = enabled;
        if (enabled)
        {
            EditorBox.Language = System.Windows.Markup.XmlLanguage.GetLanguage(language);
        }
    }

    /// <summary>
    /// Removes the built-in bindings this app replaces, so a shortcut does one
    /// thing (Section 8.3).
    /// </summary>
    /// <remarks>
    /// Three of them.
    /// <list type="bullet">
    ///   <item>
    ///     Ctrl+U. WPF binds it to <c>EditingCommands.ToggleUnderline</c>,
    ///     which can only produce a plain single underline; this app has five
    ///     kinds (Section 6.8), so <see cref="UI.Commands.SetUnderline"/>
    ///     takes the gesture over.
    ///   </item>
    ///   <item>
    ///     Ctrl+Shift+L and Ctrl+Shift+N, WPF's own bullet and number
    ///     toggles. Two reasons. The shortcuts this app uses are the ones
    ///     everybody knows from Word, Ctrl+Shift+8 and Ctrl+Shift+7
    ///     (Section 8.3), so WPF's would be two more ways to do the same
    ///     thing. And WPF's toggles alone leave every level of a nested list
    ///     with the same marker, so <see cref="ListCommands"/> wraps them
    ///     and fixes the markers afterwards (Section 6.9) - which means the
    ///     wrapper has to get the keystroke, not the raw command.
    ///   </item>
    /// </list>
    /// Ctrl+B, Ctrl+I, the four alignment shortcuts and the indent shortcuts
    /// are deliberately left alone: WPF's own commands already do exactly what
    /// is wanted, and reimplementing them would only add ways to get them
    /// wrong.
    /// </remarks>
    internal void RemoveConflictingBuiltInBindings()
    {
        var toRemove = EditorBox.InputBindings
            .OfType<KeyBinding>()
            .Where(b =>
                (b.Key == Key.U && b.Modifiers == ModifierKeys.Control)
                || (b.Key is Key.L or Key.N
                    && b.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift)))
            .ToList();

        foreach (var binding in toRemove)
        {
            EditorBox.InputBindings.Remove(binding);
        }
    }

    private void ApplyPageView()
    {
        if (_pageView == PageViewMode.PageWidth)
        {
            PaperHost.Width = PaperWidthDip;
            PaperHost.HorizontalAlignment = HorizontalAlignment.Center;
            EditorBox.Document.PagePadding = new Thickness(PageMarginDip);
        }
        else
        {
            PaperHost.Width = double.NaN;
            PaperHost.HorizontalAlignment = HorizontalAlignment.Stretch;
            EditorBox.Document.PagePadding = new Thickness(FullWidthPaddingDip);
        }
    }

    /// <summary>
    /// Ctrl+wheel zooms (Section 6.8). Without handling it here the scroll
    /// viewer would scroll instead.
    /// </summary>
    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control)
        {
            return;
        }

        Zoom += e.Delta > 0 ? 0.1 : -0.1;
        e.Handled = true;
    }

    /// <summary>
    /// Reads the formatting at the caret, at background priority and at most
    /// once per 50 ms (Section 2.1). Holding an arrow key fires this constantly,
    /// and reading twenty properties off the selection each time is exactly the
    /// kind of per-keystroke work the speed rules forbid.
    /// </summary>
    private void OnSelectionChanged(object sender, RoutedEventArgs e)
    {
        if (_formatReadoutQueued)
        {
            return;
        }

        _formatReadoutQueued = true;

        Dispatcher.BeginInvoke(
            DispatcherPriority.Background,
            (Action)(() =>
            {
                _formatReadoutQueued = false;

                // The resize handles follow the caret, and this is the only
                // place that knows it moved. At background priority with the
                // rest of the readout, because it walks up from the caret and
                // must not run per keystroke either (Section 2.1).
                ImageSupport.UpdateHandles(EditorBox);

                FormatStateChanged?.Invoke(this, ReadFormatState());
            }));
    }

    /// <summary>
    /// The keys that mean something different inside a list or a table
    /// (Section 6.9).
    /// </summary>
    /// <remarks>
    /// Each handler says whether it dealt with the key, and the event is only
    /// marked handled when one did - so Tab still inserts a tab in ordinary
    /// text, and Enter still starts a paragraph.
    /// <para>
    /// A table is checked before a list, because a list inside a table cell
    /// should indent on Tab rather than jump to the next cell: the inner
    /// structure wins, which is what Word does too. Hence the order: list
    /// first, then table.
    /// </para>
    /// </remarks>
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Tab when Keyboard.Modifiers == ModifierKeys.None:
                e.Handled = ListCommands.Indent(EditorBox)
                            || TableCommands.MoveToNextCell(EditorBox, backwards: false);
                break;

            case Key.Tab when Keyboard.Modifiers == ModifierKeys.Shift:
                e.Handled = ListCommands.Outdent(EditorBox)
                            || TableCommands.MoveToNextCell(EditorBox, backwards: true);
                break;

            case Key.Return when Keyboard.Modifiers == ModifierKeys.None:
                e.Handled = ListCommands.EndListOnEmptyItem(EditorBox);
                break;

            case Key.Delete or Key.Back when ImageSupport.ImageAt(EditorBox) is not null:
                // The handles have to come off before the picture goes, or the
                // adorner is left pointing at an element no longer in the tree.
                ImageSupport.Remove();
                break;
        }
    }

    /// <summary>
    /// Ctrl+click follows a link (Section 6.9).
    /// </summary>
    /// <remarks>
    /// Only ever on Ctrl+click. A plain click inside a link has to put the
    /// caret there, because the text of a link is text and has to be editable;
    /// an editor that opened a browser every time someone tried to fix a typo
    /// in a link would be unusable.
    /// </remarks>
    private void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control)
        {
            return;
        }

        var at = EditorBox.GetPositionFromPoint(e.GetPosition(EditorBox), snapToText: true);

        if (LinkSupport.Containing(at) is { } link)
        {
            LinkSupport.Open(link, Window.GetWindow(this));
            e.Handled = true;
        }
    }

    /// <summary>
    /// Remembers which cell a drag began in, for the table commands
    /// (Section 6.9).
    /// </summary>
    private void OnMouseDownForTables(object sender, MouseButtonEventArgs e) =>
        TableCommands.BeginDrag(
            EditorBox,
            EditorBox.GetPositionFromPoint(e.GetPosition(EditorBox), snapToText: true));

    /// <summary>And which cell it ended in.</summary>
    private void OnMouseUpForTables(object sender, MouseButtonEventArgs e) =>
        TableCommands.EndDrag(
            EditorBox,
            EditorBox.GetPositionFromPoint(e.GetPosition(EditorBox), snapToText: true));

    /// <summary>
    /// The hand cursor over a link while Ctrl is held, so it is visible that
    /// the click will do something.
    /// </summary>
    private void OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control)
        {
            if (EditorBox.Cursor == Cursors.Hand)
            {
                EditorBox.Cursor = null;
            }

            return;
        }

        var at = EditorBox.GetPositionFromPoint(e.GetPosition(EditorBox), snapToText: true);

        EditorBox.Cursor = LinkSupport.Containing(at) is not null ? Cursors.Hand : null;
    }

    private void OnTextChanged(object sender, TextChangedEventArgs e)
    {
        DocumentChanged?.Invoke(this, EventArgs.Empty);

        // Restarting the timer is what debounces it: the count only runs once
        // typing actually stops.
        _wordCountTimer.Stop();
        _wordCountTimer.Start();
    }

    private void OnWordCountTick(object? sender, EventArgs e)
    {
        _wordCountTimer.Stop();

        var all = new TextRange(EditorBox.Document.ContentStart, EditorBox.Document.ContentEnd).Text;
        var selected = EditorBox.Selection.IsEmpty ? null : EditorBox.Selection.Text;

        CountsChanged?.Invoke(
            this,
            (CountWords(all), all.Replace("\r", "").Replace("\n", "").Length,
             selected is null ? null : CountWords(selected)));
    }

    internal static int CountWords(string text) =>
        text.Split(
                [' ', '\t', '\r', '\n', ' '],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Length;

    /// <summary>
    /// Reads the formatting at the caret. <c>DependencyProperty.UnsetValue</c>
    /// from <c>GetPropertyValue</c> means the selection spans more than one
    /// value, and that becomes a null in <see cref="FormatState"/> so the
    /// pickers can show a blank box rather than pick one and be wrong.
    /// </summary>
    private FormatState ReadFormatState()
    {
        var selection = EditorBox.Selection;
        var caret = EditorBox.CaretPosition;
        var paragraph = caret.Paragraph;

        var (line, column) = CaretLineAndColumn(caret);

        return new FormatState
        {
            Bold = Tri(selection, TextElement.FontWeightProperty, FontWeights.Bold),
            Italic = Tri(selection, TextElement.FontStyleProperty, FontStyles.Italic),
            Strike = HasDecoration(selection, TextDecorations.Strikethrough),
            SmallCaps = TriCapitals(selection),
            Underline = paragraph is null ? null : ReadUnderlineKind(selection),
            Vertical = ReadVAlign(selection),
            FontFamily = Value<FontFamily>(selection, TextElement.FontFamilyProperty)?.Source,
            SizePt = Value<double?>(selection, TextElement.FontSizeProperty) is { } dip
                ? Math.Round(Units.DipsToPoints(dip), 2)
                : null,
            StyleName = paragraph is null ? null : SpProps.GetStyleName(paragraph),
            Align = Value<TextAlignment?>(selection, Block.TextAlignmentProperty) switch
            {
                TextAlignment.Left => Core.Model.Align.Left,
                TextAlignment.Center => Core.Model.Align.Center,
                TextAlignment.Right => Core.Model.Align.Right,
                TextAlignment.Justify => Core.Model.Align.Justify,
                _ => null,
            },
            LineSpacing = paragraph?.LineHeight is > 0 && paragraph.FontSize > 0
                ? Math.Round(paragraph.LineHeight / paragraph.FontSize, 2)
                : null,
            Line = line,
            Column = column,
            HasSelection = !selection.IsEmpty,
        };
    }

    private static T? Value<T>(TextSelection selection, DependencyProperty property)
    {
        var value = selection.GetPropertyValue(property);
        return value == DependencyProperty.UnsetValue ? default : (T?)value;
    }

    private static bool? Tri(TextSelection selection, DependencyProperty property, object onValue)
    {
        var value = selection.GetPropertyValue(property);
        return value == DependencyProperty.UnsetValue ? null : Equals(value, onValue);
    }

    private static bool? TriCapitals(TextSelection selection)
    {
        var value = selection.GetPropertyValue(Typography.CapitalsProperty);
        return value == DependencyProperty.UnsetValue
            ? null
            : Equals(value, FontCapitals.SmallCaps);
    }

    private static bool? HasDecoration(TextSelection selection, TextDecorationCollection wanted)
    {
        var value = selection.GetPropertyValue(Inline.TextDecorationsProperty);
        if (value == DependencyProperty.UnsetValue)
        {
            return null;
        }

        if (value is not TextDecorationCollection decorations)
        {
            return false;
        }

        var location = wanted[0].Location;
        return decorations.Any(d => d.Location == location);
    }

    /// <summary>
    /// The underline kind comes from <see cref="SpProps.UnderlineProperty"/>,
    /// not from the decorations: WPF's decorations hold pens, and reading a kind
    /// back out of a pen does not round-trip (Section 6.8).
    /// </summary>
    private static UnderlineKind? ReadUnderlineKind(TextSelection selection) =>
        SpProps.ReadUnderline(selection);

    private static VAlign? ReadVAlign(TextSelection selection) => SpProps.ReadVAlign(selection);

    /// <summary>
    /// Line and column for the status bar. The line number counts paragraphs
    /// and hard line breaks, which is what a user means by "line" in a word
    /// processor; it is not the visual wrapped line, because that changes with
    /// the window width.
    /// </summary>
    private (int Line, int Column) CaretLineAndColumn(TextPointer caret)
    {
        var paragraph = caret.Paragraph;
        if (paragraph is null)
        {
            return (1, 1);
        }

        var line = 1;
        foreach (var block in EditorBox.Document.Blocks)
        {
            if (ReferenceEquals(block, paragraph))
            {
                break;
            }

            line++;
        }

        var column = new TextRange(paragraph.ContentStart, caret).Text.Length + 1;
        return (line, column);
    }
}
