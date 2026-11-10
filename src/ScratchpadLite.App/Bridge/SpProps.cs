using System.Windows;
using System.Windows.Documents;
using ScratchpadLite.Core.Model;

using Inline = System.Windows.Documents.Inline;

namespace ScratchpadLite.App.Bridge;

/// <summary>
/// Attached properties holding the facts WPF text elements have nowhere to put
/// (Section 6.4).
/// </summary>
/// <remarks>
/// Each one exists because a WPF property either cannot represent the thing or
/// cannot be read back reliably:
/// <list type="bullet">
///   <item>
///     <see cref="StyleNameProperty"/> — WPF paragraphs have no style name at
///     all, and RTF's converter drops them (Section 7.2).
///   </item>
///   <item>
///     <see cref="RequestedFontProperty"/> — when a document asks for a font
///     this PC lacks, the run shows a substitute but must still save the name
///     it asked for, or the document quietly loses it.
///   </item>
///   <item>
///     <see cref="VAlignProperty"/> and <see cref="BaseSizePtProperty"/> —
///     super and subscript are done by shrinking the font to 65% and shifting
///     the baseline, so the original size has to be kept to get it back.
///   </item>
///   <item>
///     <see cref="UnderlineProperty"/> — WPF's TextDecorations hold pens, not a
///     kind, and a wavy underline is drawn approximately. The kind is what gets
///     saved.
///   </item>
///   <item>
///     <see cref="PreservedProperty"/> — all caps and letter spacing, which WPF
///     cannot show, kept so a round trip does not drop them (Section 6.8).
///   </item>
/// </list>
/// </remarks>
internal static class SpProps
{
    // ---- Paragraph style name ----

    public static readonly DependencyProperty StyleNameProperty =
        DependencyProperty.RegisterAttached(
            "StyleName", typeof(string), typeof(SpProps), new PropertyMetadata(default(string)));

    public static void SetStyleName(DependencyObject element, string? value) =>
        element.SetValue(StyleNameProperty, value);

    public static string? GetStyleName(DependencyObject element) =>
        (string?)element.GetValue(StyleNameProperty);

    // ---- The font the file asked for, when a substitute is shown ----

    public static readonly DependencyProperty RequestedFontProperty =
        DependencyProperty.RegisterAttached(
            "RequestedFont", typeof(string), typeof(SpProps),
            new FrameworkPropertyMetadata(default(string), FrameworkPropertyMetadataOptions.Inherits));

    public static void SetRequestedFont(DependencyObject element, string? value) =>
        element.SetValue(RequestedFontProperty, value);

    public static string? GetRequestedFont(DependencyObject element) =>
        (string?)element.GetValue(RequestedFontProperty);

    // ---- Super and subscript ----

    public static readonly DependencyProperty VAlignProperty =
        DependencyProperty.RegisterAttached(
            "VAlign", typeof(VAlign), typeof(SpProps),
            new FrameworkPropertyMetadata(VAlign.Normal, FrameworkPropertyMetadataOptions.Inherits));

    public static void SetVAlign(DependencyObject element, VAlign value) =>
        element.SetValue(VAlignProperty, value);

    public static VAlign GetVAlign(DependencyObject element) =>
        (VAlign)element.GetValue(VAlignProperty);

    /// <summary>The font size in points before super or subscript shrank it.</summary>
    public static readonly DependencyProperty BaseSizePtProperty =
        DependencyProperty.RegisterAttached(
            "BaseSizePt", typeof(double?), typeof(SpProps),
            new FrameworkPropertyMetadata(default(double?), FrameworkPropertyMetadataOptions.Inherits));

    public static void SetBaseSizePt(DependencyObject element, double? value) =>
        element.SetValue(BaseSizePtProperty, value);

    public static double? GetBaseSizePt(DependencyObject element) =>
        (double?)element.GetValue(BaseSizePtProperty);

    // ---- Underline kind ----

    public static readonly DependencyProperty UnderlineProperty =
        DependencyProperty.RegisterAttached(
            "Underline", typeof(UnderlineKind), typeof(SpProps),
            new FrameworkPropertyMetadata(UnderlineKind.None, FrameworkPropertyMetadataOptions.Inherits));

    public static void SetUnderline(DependencyObject element, UnderlineKind value) =>
        element.SetValue(UnderlineProperty, value);

    public static UnderlineKind GetUnderline(DependencyObject element) =>
        (UnderlineKind)element.GetValue(UnderlineProperty);

    // ---- Formatting kept only so a round trip does not lose it ----

    /// <summary>
    /// All caps and letter spacing from an opened file. Not displayed; the
    /// load warnings say so, and a save writes them back unchanged.
    /// </summary>
    public static readonly DependencyProperty PreservedProperty =
        DependencyProperty.RegisterAttached(
            "Preserved", typeof(PreservedFormatting), typeof(SpProps),
            new FrameworkPropertyMetadata(default(PreservedFormatting), FrameworkPropertyMetadataOptions.Inherits));

    public static void SetPreserved(DependencyObject element, PreservedFormatting? value) =>
        element.SetValue(PreservedProperty, value);

    public static PreservedFormatting? GetPreserved(DependencyObject element) =>
        (PreservedFormatting?)element.GetValue(PreservedProperty);

    // ---- The bridge's own markers (Section 6.4) ----

    /// <summary>
    /// The style name a horizontal rule carries, so the walker recognises one.
    /// </summary>
    /// <remarks>
    /// A rule is a <c>BlockUIContainer</c> holding a Separator, and WPF has no
    /// way to say what a BlockUIContainer is <em>for</em>. The double
    /// underscore keeps it out of the style picker, which lists the sheet's own
    /// names.
    /// </remarks>
    public const string HorizontalRuleStyleName = "__hr";

    /// <summary>
    /// A hyperlink's target as the file gave it.
    /// </summary>
    /// <remarks>
    /// <see cref="Hyperlink.NavigateUri"/> would nearly do, but it is a
    /// <see cref="Uri"/>, and a file can carry a target that <c>Uri</c> will
    /// not parse. Keeping the original string means a save writes back what was
    /// read rather than dropping it.
    /// </remarks>
    public static readonly DependencyProperty LinkProperty =
        DependencyProperty.RegisterAttached(
            "Link", typeof(string), typeof(SpProps),
            new FrameworkPropertyMetadata(default(string), FrameworkPropertyMetadataOptions.Inherits));

    public static void SetLink(DependencyObject element, string? value) =>
        element.SetValue(LinkProperty, value);

    public static string? GetLink(DependencyObject element) =>
        (string?)element.GetValue(LinkProperty);

    /// <summary>
    /// An image's original bytes, carried on the <c>Image.Tag</c> so a save
    /// writes them back untouched (Section 6.4).
    /// </summary>
    /// <remarks>
    /// Re-encoding on every save would degrade a picture a little at a time. A
    /// PNG screenshot saved five times should be the same PNG.
    /// </remarks>
    internal sealed record ImagePayload(byte[] Data, string Mime, string Alt);

    // ------------------------------------------------- applying to a range --

    /// <summary>
    /// Sets one of these attached properties across a selection.
    /// </summary>
    /// <remarks>
    /// This exists because <c>TextRange.ApplyPropertyValue</c> cannot be used:
    /// it accepts only WPF's own text-formatting properties and throws
    /// <c>ArgumentException</c> — "'Underline' property is not valid for text
    /// formatting" — for anything else. So the value is written onto the inline
    /// elements themselves.
    /// <para>
    /// Callers apply the matching real WPF property first (TextDecorations for
    /// an underline, FontSize for a superscript). That call is what makes WPF
    /// split the runs at the selection edges, so by the time this runs the
    /// elements to tag already line up with the selection.
    /// </para>
    /// </remarks>
    internal static void ApplyToSelection(TextSelection selection, DependencyProperty property, object? value)
    {
        foreach (var element in InlinesIn(selection))
        {
            element.SetValue(property, value);
        }
    }

    /// <summary>
    /// Reads one of these attached properties from a selection, returning
    /// <see cref="DependencyProperty.UnsetValue"/> when the selection holds more
    /// than one value — the same "mixed" signal WPF's own
    /// <c>GetPropertyValue</c> gives, so callers can treat both alike.
    /// </summary>
    internal static object? ReadFromSelection(TextSelection selection, DependencyProperty property)
    {
        object? first = null;
        var seen = false;

        foreach (var element in InlinesIn(selection))
        {
            var value = element.GetValue(property);

            if (!seen)
            {
                first = value;
                seen = true;
            }
            else if (!Equals(first, value))
            {
                return DependencyProperty.UnsetValue;
            }
        }

        return seen ? first : DependencyProperty.UnsetValue;
    }

    /// <summary>
    /// The underline kind across a selection, or null when it is mixed
    /// (Section 6.8). The kind is read from here rather than from WPF's
    /// decorations, because a pen does not tell you which kind produced it.
    /// </summary>
    internal static UnderlineKind? ReadUnderline(TextSelection selection) =>
        ReadFromSelection(selection, UnderlineProperty) is UnderlineKind kind ? kind : null;

    /// <summary>Super or subscript across a selection, or null when mixed.</summary>
    internal static VAlign? ReadVAlign(TextSelection selection) =>
        ReadFromSelection(selection, VAlignProperty) is VAlign align ? align : null;

    /// <summary>
    /// The size in points before super or subscript shrank the text, or null
    /// when it was never shrunk or the selection is mixed.
    /// </summary>
    internal static double? ReadBaseSizePt(TextSelection selection) =>
        ReadFromSelection(selection, BaseSizePtProperty) is double points ? points : null;

    /// <summary>
    /// The inline elements a selection touches. With an empty selection this is
    /// the one element holding the caret, so a value set with nothing selected
    /// applies to what gets typed next — these properties inherit, so text
    /// inserted into that run picks the value up.
    /// </summary>
    private static IEnumerable<Inline> InlinesIn(TextSelection selection)
    {
        if (selection.IsEmpty)
        {
            if (selection.Start.Parent is Inline caretInline)
            {
                yield return caretInline;
            }

            yield break;
        }

        var pointer = selection.Start;

        while (pointer is not null && pointer.CompareTo(selection.End) < 0)
        {
            if (pointer.Parent is Inline inline)
            {
                yield return inline;
            }

            pointer = pointer.GetNextContextPosition(LogicalDirection.Forward);
        }
    }
}

/// <summary>
/// Character formatting an opened file used that WPF cannot show (Section 6.8).
/// Carried through untouched so saving back does not drop it.
/// </summary>
internal sealed record PreservedFormatting
{
    internal bool AllCaps { get; init; }

    internal double? LetterSpacingPt { get; init; }
}
