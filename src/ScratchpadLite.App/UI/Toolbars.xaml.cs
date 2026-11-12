using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using ScratchpadLite.App.Editor;
using ScratchpadLite.Core.Model;

namespace ScratchpadLite.App.UI;

/// <summary>
/// The two toolbar rows (Section 8.2), and the wiring from their buttons to
/// <see cref="FormatCommands"/>.
/// </summary>
/// <remarks>
/// The toolbar owns this wiring rather than the window, so adding a button
/// means touching one file. What it does not own is the shortcuts: those live
/// in <see cref="Commands"/>, and the tooltips here only name them.
/// </remarks>
public partial class Toolbars : UserControl
{
    private readonly FormatPainter _painter = new();
    private bool _updating;

    public Toolbars()
    {
        InitializeComponent();
        WireUp();

        _painter.Deactivated += (_, _) => FormatPainterButton.IsChecked = false;
    }

    /// <summary>The editor the buttons act on. Set by the window.</summary>
    internal EditorView? Target { get; set; }

    /// <summary>
    /// Whether the files-and-font row is shown (Sections 6.2 and 8.1).
    /// </summary>
    /// <remarks>
    /// Collapsed rather than hidden, because a hidden toolbar still takes its
    /// height and would leave a band of empty chrome where the row used to be.
    /// </remarks>
    internal bool ShowRow1
    {
        get => Row1.Visibility == Visibility.Visible;
        set => Row1.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Whether the formatting row is shown (Sections 6.2 and 8.1).</summary>
    internal bool ShowRow2
    {
        get => Row2.Visibility == Visibility.Visible;
        set => Row2.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>The style sheet, needed by the style picker and Clear Formatting.</summary>
    internal StyleSheet Styles { get; private set; } = StyleSheet.Default;

    internal void LoadStyles(StyleSheet styles)
    {
        Styles = styles;
        StyleBox.Load(styles);
    }

    internal bool PreviewFontNamesInOwnFont
    {
        get => FontBox.PreviewInOwnFont;
        set => FontBox.PreviewInOwnFont = value;
    }

    /// <summary>
    /// Rebuilds the font list after the folder check found new families
    /// (Section 6.7). Does nothing if the dropdown has never been opened,
    /// since there is no list to rebuild yet.
    /// </summary>
    internal void RefreshFonts() => FontBox.Refresh();

    /// <summary>
    /// Reflects the formatting at the caret. Guarded by
    /// <see cref="_updating"/>, because setting a ToggleButton raises its own
    /// events and would otherwise apply the formatting it is only reporting.
    /// </summary>
    internal void UpdateFrom(FormatState state)
    {
        _updating = true;
        try
        {
            BoldButton.IsChecked = state.Bold;
            ItalicButton.IsChecked = state.Italic;
            StrikeButton.IsChecked = state.Strike;
            SmallCapsButton.IsChecked = state.SmallCaps;

            UnderlineButton.IsChecked = state.Underline switch
            {
                null => null,
                UnderlineKind.None => false,
                _ => true,
            };

            SuperButton.IsChecked = state.Vertical switch
            {
                null => null,
                VAlign.Super => true,
                _ => false,
            };

            SubButton.IsChecked = state.Vertical switch
            {
                null => null,
                VAlign.Sub => true,
                _ => false,
            };

            AlignLeftButton.IsChecked = state.Align == Align.Left;
            AlignCenterButton.IsChecked = state.Align == Align.Center;
            AlignRightButton.IsChecked = state.Align == Align.Right;
            AlignJustifyButton.IsChecked = state.Align == Align.Justify;

            FontBox.Show(state.FontFamily);
            SizeBox.Show(state.SizePt);
            StyleBox.Show(state.StyleName);

            if (Target is { } editor)
            {
                UndoButton.IsEnabled = editor.Editor.CanUndo;
                RedoButton.IsEnabled = editor.Editor.CanRedo;
            }
        }
        finally
        {
            _updating = false;
        }
    }

    private void WireUp()
    {
        // ---- Row 1 ----
        UndoButton.Click += (_, _) => Run(rtb => rtb.Undo());
        RedoButton.Click += (_, _) => Run(rtb => rtb.Redo());
        GrowButton.Click += (_, _) => Run(FormatCommands.GrowFont);
        ShrinkButton.Click += (_, _) => Run(FormatCommands.ShrinkFont);

        FontBox.FontChosen += (_, family) => Run(rtb => FormatCommands.SetFontFamily(rtb, family));
        SizeBox.SizeChosen += (_, size) => Run(rtb => FormatCommands.SetFontSize(rtb, size));
        StyleBox.StyleChosen += (_, name) => Run(rtb => FormatCommands.SetStyle(rtb, name, Styles));

        // ---- Row 2: characters ----
        BoldButton.Click += (_, _) => Run(FormatCommands.ToggleBold);
        ItalicButton.Click += (_, _) => Run(FormatCommands.ToggleItalic);
        StrikeButton.Click += (_, _) => Run(FormatCommands.ToggleStrikethrough);
        SmallCapsButton.Click += (_, _) => Run(FormatCommands.ToggleSmallCaps);

        UnderlineButton.Click += (_, _) => Run(rtb =>
        {
            // The button toggles plain single underline; the arrow beside it is
            // where the five kinds live.
            var kind = Bridge.SpProps.ReadUnderline(rtb.Selection) ?? UnderlineKind.None;
            FormatCommands.SetUnderline(
                rtb, kind == UnderlineKind.None ? UnderlineKind.Single : UnderlineKind.None);
        });

        UnderlineMenuButton.Click += (_, _) => OpenMenu(UnderlineMenuButton, UnderlineMenu);
        foreach (var item in UnderlineMenu.Items.OfType<MenuItem>())
        {
            item.Click += OnUnderlineKindClick;
        }

        SuperButton.Click += (_, _) => Run(rtb => FormatCommands.SetVerticalAlignment(rtb, VAlign.Super));
        SubButton.Click += (_, _) => Run(rtb => FormatCommands.SetVerticalAlignment(rtb, VAlign.Sub));

        TextColorBox.Glyph = "";
        TextColorBox.ClearLabel = "Automatic";
        TextColorBox.ColorChosen += (_, color) => Run(rtb =>
        {
            if (color is { } value)
            {
                FormatCommands.SetTextColor(rtb, value);
            }
            else
            {
                rtb.Selection.ApplyPropertyValue(
                    System.Windows.Documents.TextElement.ForegroundProperty,
                    rtb.Foreground);
            }
        });

        HighlightBox.Glyph = "";
        HighlightBox.ClearLabel = "No highlight";
        HighlightBox.ColorChosen += (_, color) => Run(rtb => FormatCommands.SetHighlight(rtb, color));

        // ---- Row 2: paragraphs ----
        AlignLeftButton.Click += (_, _) => Run(rtb => FormatCommands.SetAlignment(rtb, Align.Left));
        AlignCenterButton.Click += (_, _) => Run(rtb => FormatCommands.SetAlignment(rtb, Align.Center));
        AlignRightButton.Click += (_, _) => Run(rtb => FormatCommands.SetAlignment(rtb, Align.Right));
        AlignJustifyButton.Click += (_, _) => Run(rtb => FormatCommands.SetAlignment(rtb, Align.Justify));

        BulletsButton.Click += (_, _) => Run(ListCommands.ToggleBullets);
        NumberingButton.Click += (_, _) => Run(ListCommands.ToggleNumbering);

        IndentButton.Click += (_, _) => Run(rtb => EditingCommands.IncreaseIndentation.Execute(null, rtb));
        OutdentButton.Click += (_, _) => Run(rtb => EditingCommands.DecreaseIndentation.Execute(null, rtb));

        LineSpacingButton.Click += (_, _) => OpenMenu(LineSpacingButton, LineSpacingMenu);
        foreach (var item in LineSpacingMenu.Items.OfType<MenuItem>())
        {
            item.Click += OnLineSpacingClick;
        }

        // ---- The insert buttons ----
        // These three go through the window's commands rather than straight to
        // the editor, because each one opens a dialog that needs an owner -
        // and the toolbar is not a window.
        InsertImageButton.Click += (_, _) => Fire(Commands.InsertImage);
        InsertTableButton.Click += (_, _) => Fire(Commands.InsertTable);
        InsertLinkButton.Click += (_, _) => Fire(Commands.InsertLink);

        ClearFormattingButton.Click += (_, _) => Run(rtb => FormatCommands.ClearFormatting(rtb, Styles));

        // ---- The format painter ----
        // A single click paints the next selection once; a double click keeps it
        // on until Escape (Section 6.8).
        FormatPainterButton.Click += (_, _) =>
        {
            if (_updating)
            {
                return;
            }

            if (FormatPainterButton.IsChecked == true)
            {
                Run(rtb => _painter.Pick(rtb, sticky: false));
            }
            else
            {
                _painter.Cancel();
            }
        };

        FormatPainterButton.MouseDoubleClick += (_, e) =>
        {
            Run(rtb => _painter.Pick(rtb, sticky: true));
            FormatPainterButton.IsChecked = true;
            e.Handled = true;
        };
    }

    /// <summary>
    /// Loads the format painter from the caret, for the Format menu item. The
    /// toolbar button does the same thing plus the double-click-to-stick
    /// behaviour.
    /// </summary>
    internal void PickFormatPainter()
    {
        Run(rtb => _painter.Pick(rtb, sticky: false));
        FormatPainterButton.IsChecked = _painter.IsActive;
    }

    /// <summary>
    /// Paints the current selection if the painter is loaded. Called by the
    /// window when the selection changes.
    /// </summary>
    internal void PaintIfArmed()
    {
        if (_painter.IsActive && Target is { } editor && !editor.Editor.Selection.IsEmpty)
        {
            _painter.Paint(editor.Editor);
        }
    }

    internal void CancelFormatPainter() => _painter.Cancel();

    private void OnUnderlineKindClick(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string tag } && Enum.TryParse<UnderlineKind>(tag, out var kind))
        {
            Run(rtb => FormatCommands.SetUnderline(rtb, kind));
        }
    }

    private void OnLineSpacingClick(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string tag })
        {
            return;
        }

        if (tag == "custom")
        {
            // The Paragraph dialog, which is where a typed multiple lives.
            // Raised as the command rather than opened here, so the dialog has
            // one caller: the routed command finds the window's binding by
            // bubbling up from this control.
            Commands.ParagraphSettings.Execute(null, this);

            return;
        }

        if (double.TryParse(tag, CultureInfo.InvariantCulture, out var multiple))
        {
            Run(rtb => FormatCommands.SetLineSpacing(rtb, multiple));
        }
    }

    /// <summary>
    /// Opens a button's menu on a left click. WPF only opens a ContextMenu on
    /// right click, and these two buttons are menus rather than context menus.
    /// </summary>
    private static void OpenMenu(ButtonBase owner, ContextMenu menu)
    {
        menu.PlacementTarget = owner;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    /// <summary>
    /// Runs a formatting action against the active editor and gives the focus
    /// back, so typing continues in the document rather than in the toolbar.
    /// </summary>
    private void Run(Action<RichTextBox> action)
    {
        if (_updating || Target is not { } editor)
        {
            return;
        }

        action(editor.Editor);
        editor.Editor.Focus();
    }

    /// <summary>
    /// Raises one of the window's commands, for a button whose work needs a
    /// dialog.
    /// </summary>
    /// <remarks>
    /// The editor is given as the command target so the routed command travels
    /// up from it and finds the window's binding - a toolbar button is not in
    /// the editor's tree, and without a target the command would be looked for
    /// from wherever the keyboard focus happens to be.
    /// </remarks>
    private void Fire(RoutedUICommand command)
    {
        if (_updating || Target is not { } editor)
        {
            return;
        }

        if (command.CanExecute(null, editor.Editor))
        {
            command.Execute(null, editor.Editor);
        }
    }
}
