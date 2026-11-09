using ScratchpadLite.Core.Model;

namespace ScratchpadLite.App.Editor;

/// <summary>
/// What the formatting is at the caret, read once per selection change and
/// handed to the toolbar and status bar (Section 6.8).
/// </summary>
/// <remarks>
/// A null on <see cref="FontFamily"/>, <see cref="SizePt"/>,
/// <see cref="StyleName"/> or <see cref="Align"/> means "mixed": the selection
/// spans more than one value, and WPF reported
/// <c>DependencyProperty.UnsetValue</c>. The pickers show a blank box for it,
/// the way Word does, rather than picking one of the values and lying about it.
/// <para>
/// The booleans are three-state for the same reason: <see cref="Bold"/> is null
/// across a selection that is partly bold.
/// </para>
/// </remarks>
internal sealed record FormatState
{
    internal bool? Bold { get; init; }
    internal bool? Italic { get; init; }
    internal bool? Strike { get; init; }
    internal bool? SmallCaps { get; init; }

    internal UnderlineKind? Underline { get; init; }
    internal VAlign? Vertical { get; init; }

    internal string? FontFamily { get; init; }
    internal double? SizePt { get; init; }
    internal string? StyleName { get; init; }
    internal Align? Align { get; init; }

    internal double? LineSpacing { get; init; }

    /// <summary>1-based caret position, for the status bar.</summary>
    internal int Line { get; init; } = 1;

    internal int Column { get; init; } = 1;

    internal bool HasSelection { get; init; }
}

/// <summary>How <see cref="FormatCommands.ChangeCase"/> should rewrite the text.</summary>
/// <remarks>
/// This changes the letters themselves, which is deliberate. WPF has no
/// all-caps character format (Section 6.8), and a format that only looks like
/// capitals would be lost on save; rewriting the text survives every format.
/// </remarks>
public enum CaseMode
{
    Upper,
    Lower,
    Title,
    Sentence,
}
