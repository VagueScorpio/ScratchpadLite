using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ScratchpadLite.App.Services;

namespace ScratchpadLite.App.UI;

/// <summary>
/// The row of tab headers (Section 6.12): the document name, a dot when there
/// are unsaved changes, the full path as a tooltip, a close button,
/// middle-click to close, and drag to reorder.
/// </summary>
/// <remarks>
/// This control knows nothing about documents beyond their names. It raises
/// what the user did and the window decides what that means, so closing a
/// modified tab can ask about saving without this class knowing what saving is.
/// </remarks>
public partial class TabStrip : UserControl
{
    /// <summary>How far the pointer moves before a press becomes a drag.</summary>
    private const double DragThreshold = 6;

    /// <summary>Marks the TextBlock that holds a tab's name.</summary>
    internal const string LabelTag = "tab-label";

    private readonly List<Header> _headers = [];

    private Point _pressedAt;
    private int _pressedIndex = -1;
    private bool _dragging;

    public TabStrip() => InitializeComponent();

    /// <summary>Raised when a header is clicked.</summary>
    internal event EventHandler<DocumentSession>? Chosen;

    /// <summary>Raised by the close button, middle-click, or Ctrl+W on a header.</summary>
    internal event EventHandler<DocumentSession>? CloseRequested;

    /// <summary>Raised by the + button.</summary>
    internal event EventHandler? NewTabRequested;

    /// <summary>Raised when a header has been dragged somewhere else in the strip.</summary>
    internal event EventHandler<(int From, int To)>? Reordered;

    /// <summary>
    /// Rebuilds the strip. Called whenever a tab is added, removed, renamed,
    /// reordered, or its dirty mark changes - which is often enough to be worth
    /// saying that the whole thing is a handful of Borders and costs nothing to
    /// rebuild, and rare enough that it never happens while typing.
    /// </summary>
    internal void Show(IReadOnlyList<DocumentSession> tabs, DocumentSession? active)
    {
        ArgumentNullException.ThrowIfNull(tabs);

        Headers.Children.Clear();
        _headers.Clear();

        foreach (var session in tabs)
        {
            var header = new Header(session, ReferenceEquals(session, active));

            header.Root.MouseLeftButtonDown += OnHeaderPressed;
            header.Root.MouseMove += OnHeaderMoved;
            header.Root.MouseLeftButtonUp += OnHeaderReleased;
            header.Root.MouseDown += OnHeaderMouseDown;
            header.Close.Click += (_, _) => CloseRequested?.Invoke(this, session);

            _headers.Add(header);
            Headers.Children.Add(header.Root);
        }
    }

    private void OnNewTabClick(object sender, RoutedEventArgs e) =>
        NewTabRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>Middle-click closes a tab (Section 6.12).</summary>
    private void OnHeaderMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle)
        {
            return;
        }

        if (IndexOf(sender) is >= 0 and var index)
        {
            CloseRequested?.Invoke(this, _headers[index].Session);
            e.Handled = true;
        }
    }

    private void OnHeaderPressed(object sender, MouseButtonEventArgs e)
    {
        _pressedIndex = IndexOf(sender);
        _pressedAt = e.GetPosition(Headers);
        _dragging = false;

        if (_pressedIndex >= 0)
        {
            Chosen?.Invoke(this, _headers[_pressedIndex].Session);

            // Captured so a drag that leaves the header still reports where it
            // goes; without this a reorder only works while the pointer stays
            // inside the header it started in, which is the one place it cannot
            // usefully be.
            ((UIElement)sender).CaptureMouse();
        }
    }

    private void OnHeaderMoved(object sender, MouseEventArgs e)
    {
        if (_pressedIndex < 0 || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var now = e.GetPosition(Headers);

        if (!_dragging && Math.Abs(now.X - _pressedAt.X) < DragThreshold)
        {
            return;
        }

        _dragging = true;

        var over = IndexAt(now);

        if (over >= 0 && over != _pressedIndex)
        {
            Reordered?.Invoke(this, (_pressedIndex, over));

            // The strip has just been rebuilt under the pointer, so the drag
            // ends here rather than carrying on against stale headers. One drag
            // moves a tab one place, which is what dragging a short distance
            // means anyway.
            Release(sender);
        }
    }

    private void OnHeaderReleased(object sender, MouseButtonEventArgs e) => Release(sender);

    private void Release(object sender)
    {
        if (sender is UIElement element && element.IsMouseCaptured)
        {
            element.ReleaseMouseCapture();
        }

        _pressedIndex = -1;
        _dragging = false;
    }

    private int IndexOf(object sender) =>
        _headers.FindIndex(h => ReferenceEquals(h.Root, sender));

    /// <summary>Which header is under a point in the strip's own coordinates.</summary>
    private int IndexAt(Point point)
    {
        for (var i = 0; i < _headers.Count; i++)
        {
            var root = _headers[i].Root;
            var origin = root.TranslatePoint(new Point(0, 0), Headers);

            if (point.X >= origin.X && point.X <= origin.X + root.ActualWidth)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// One header: the pieces that have to be reachable afterwards, kept
    /// together rather than fished back out of the visual tree.
    /// </summary>
    private sealed class Header
    {
        internal Header(DocumentSession session, bool active)
        {
            Session = session;

            var label = new TextBlock
            {
                // Section 6.12: a dot marks unsaved changes.
                Text = session.IsDirty ? $"{session.Title} •" : session.Title,

                // Names the one TextBlock that is a tab's name, so a test can
                // find it among the ones the button and scroll templates make.
                Tag = LabelTag,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 4, 0),
                MaxWidth = 220,
                TextTrimming = TextTrimming.CharacterEllipsis,
                FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal,
            };

            Close = new Button
            {
                Content = "",
                FontSize = 9,
                Width = 18,
                Height = 18,
                Padding = new Thickness(0),
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = "Close tab (Ctrl+W)",
                Focusable = false,
            };

            Close.SetResourceReference(Control.FontFamilyProperty, "IconFont");

            var content = new DockPanel();
            DockPanel.SetDock(Close, Dock.Right);
            content.Children.Add(Close);
            content.Children.Add(label);

            Root = new Border
            {
                Child = content,
                MinWidth = 90,
                Height = 28,
                Margin = new Thickness(2, 2, 0, 0),
                CornerRadius = new CornerRadius(4, 4, 0, 0),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(active ? 1 : 0, active ? 1 : 0, active ? 1 : 0, 0),

                // The full path, which is the only place a tab says which of two
                // files with the same name it is (Section 6.12).
                ToolTip = session.FilePath ?? session.Title,
            };

            // Resource references rather than resolved brushes, so the strip
            // follows a theme change without being rebuilt.
            Root.SetResourceReference(Border.BorderBrushProperty, "BorderBrushSoft");

            if (active)
            {
                Root.SetResourceReference(Border.BackgroundProperty, "PaperBackground");
            }
        }

        internal DocumentSession Session { get; }

        internal Border Root { get; }

        internal Button Close { get; }
    }
}
