using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using ScratchpadLite.App.Bridge;

using Inline = System.Windows.Documents.Inline;

namespace ScratchpadLite.App.Editor;

/// <summary>
/// Copies the formatting at the caret and applies it to the next selection
/// (Section 6.8). One click paints once; a double click stays on until Escape.
/// </summary>
/// <remarks>Phase 1.</remarks>
internal sealed class FormatPainter
{
    /// <summary>
    /// The properties the painter carries. Deliberately character formatting
    /// only: a painter that also moved paragraph styles would turn a click into
    /// a much bigger change than the user asked for, which is not what the tool
    /// is for.
    /// </summary>
    private static readonly DependencyProperty[] Painted =
    [
        TextElement.FontFamilyProperty,
        TextElement.FontSizeProperty,
        TextElement.FontWeightProperty,
        TextElement.FontStyleProperty,
        TextElement.ForegroundProperty,
        TextElement.BackgroundProperty,
        Inline.TextDecorationsProperty,
        Inline.BaselineAlignmentProperty,
        Typography.CapitalsProperty,
    ];

    /// <summary>
    /// The app's own attached properties (Section 6.4). These are kept separate
    /// because a TextRange cannot carry them: <c>GetPropertyValue</c> and
    /// <c>ApplyPropertyValue</c> accept only WPF's own text-formatting
    /// properties and throw for anything else, so these go through
    /// <see cref="SpProps.ReadFromSelection"/> and
    /// <see cref="SpProps.ApplyToSelection"/> instead.
    /// </summary>
    private static readonly DependencyProperty[] PaintedAttached =
    [
        SpProps.UnderlineProperty,
        SpProps.VAlignProperty,
        SpProps.BaseSizePtProperty,
    ];

    private readonly Dictionary<DependencyProperty, object> _picked = [];
    private readonly Dictionary<DependencyProperty, object?> _pickedAttached = [];

    /// <summary>True while the painter is holding a format.</summary>
    internal bool IsActive { get; private set; }

    /// <summary>True when a double click locked it on until Escape.</summary>
    internal bool IsSticky { get; private set; }

    /// <summary>Raised when the painter turns itself off, so the button can un-press.</summary>
    internal event EventHandler? Deactivated;

    /// <summary>Picks up the formatting at the caret.</summary>
    internal void Pick(RichTextBox rtb, bool sticky)
    {
        _picked.Clear();
        _pickedAttached.Clear();

        foreach (var property in Painted)
        {
            var value = rtb.Selection.GetPropertyValue(property);

            // UnsetValue means the selection spans several values, so there is
            // no single format to carry; skipping it is better than picking one.
            if (value != DependencyProperty.UnsetValue)
            {
                _picked[property] = value;
            }
        }

        foreach (var property in PaintedAttached)
        {
            var value = SpProps.ReadFromSelection(rtb.Selection, property);
            if (value != DependencyProperty.UnsetValue)
            {
                _pickedAttached[property] = value;
            }
        }

        IsActive = _picked.Count > 0 || _pickedAttached.Count > 0;
        IsSticky = sticky && IsActive;
    }

    /// <summary>
    /// Applies the picked formatting to the current selection. Turns itself off
    /// afterwards unless a double click made it sticky.
    /// </summary>
    internal void Paint(RichTextBox rtb)
    {
        if (!IsActive || rtb.Selection.IsEmpty)
        {
            return;
        }

        rtb.BeginChange();
        try
        {
            // The real WPF properties go on first: applying them is what makes
            // WPF split the runs at the selection edges, so the attached ones
            // then land on elements that match the selection exactly.
            foreach (var (property, value) in _picked)
            {
                rtb.Selection.ApplyPropertyValue(property, value);
            }

            foreach (var (property, value) in _pickedAttached)
            {
                SpProps.ApplyToSelection(rtb.Selection, property, value);
            }
        }
        finally
        {
            rtb.EndChange();
        }

        if (!IsSticky)
        {
            Cancel();
        }
    }

    /// <summary>Escape, or a single-click paint finishing.</summary>
    internal void Cancel()
    {
        if (!IsActive)
        {
            return;
        }

        IsActive = false;
        IsSticky = false;
        _picked.Clear();
        _pickedAttached.Clear();
        Deactivated?.Invoke(this, EventArgs.Empty);
    }
}
