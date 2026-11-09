using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using ScratchpadLite.App.Bridge;
using ScratchpadLite.Core.Model;

using Block = System.Windows.Documents.Block;
using Inline = System.Windows.Documents.Inline;
using Paragraph = System.Windows.Documents.Paragraph;

namespace ScratchpadLite.App.Editor;

/// <summary>
/// Every formatting operation, as a static method taking the
/// <see cref="RichTextBox"/> (Section 6.8).
/// </summary>
/// <remarks>
/// Three rules hold throughout:
/// <list type="bullet">
///   <item>
///     Changes go through <c>Selection.ApplyPropertyValue</c>, so the other
///     properties on the run are left alone.
///   </item>
///   <item>
///     Anything that takes more than one step is wrapped in
///     <c>BeginChange</c>/<c>EndChange</c>, so one Ctrl+Z undoes one action
///     rather than part of one.
///   </item>
///   <item>
///     With no selection the change applies to whatever is typed next, which is
///     what WPF already does for <c>ApplyPropertyValue</c> on an empty
///     selection.
///   </item>
/// </list>
/// </remarks>
internal static class FormatCommands
{
    /// <summary>
    /// The size list Grow and Shrink step through, and the size picker shows
    /// (Section 6.7).
    /// </summary>
    internal static readonly double[] PresetSizesPt =
        [8, 9, 10, 10.5, 11, 12, 14, 16, 18, 20, 22, 24, 26, 28, 36, 48, 72];

    internal const double MinSizePt = 1;
    internal const double MaxSizePt = 1638;

    /// <summary>Super and subscript shrink the text to this share of its size (Section 6.8).</summary>
    private const double VerticalShrink = 0.65;

    // ------------------------------------------------------- character ------

    internal static void ToggleBold(RichTextBox rtb)
    {
        var isBold = Equals(rtb.Selection.GetPropertyValue(TextElement.FontWeightProperty), FontWeights.Bold);
        rtb.Selection.ApplyPropertyValue(
            TextElement.FontWeightProperty, isBold ? FontWeights.Normal : FontWeights.Bold);
    }

    internal static void ToggleItalic(RichTextBox rtb)
    {
        var isItalic = Equals(rtb.Selection.GetPropertyValue(TextElement.FontStyleProperty), FontStyles.Italic);
        rtb.Selection.ApplyPropertyValue(
            TextElement.FontStyleProperty, isItalic ? FontStyles.Normal : FontStyles.Italic);
    }

    internal static void ToggleStrikethrough(RichTextBox rtb)
    {
        var existing = rtb.Selection.GetPropertyValue(Inline.TextDecorationsProperty)
            as TextDecorationCollection;

        var hasStrike = existing?.Any(d => d.Location == TextDecorationLocation.Strikethrough) == true;
        var kept = existing?
                       .Where(d => d.Location != TextDecorationLocation.Strikethrough)
                       .ToList()
                   ?? [];

        var result = new TextDecorationCollection(kept);
        if (!hasStrike)
        {
            result.Add(TextDecorations.Strikethrough[0]);
        }

        result.Freeze();
        rtb.Selection.ApplyPropertyValue(Inline.TextDecorationsProperty, result);
    }

    /// <summary>
    /// Sets the underline kind (Section 6.8). The kind is also stored on the
    /// run, because WPF's decorations hold pens and reading a kind back out of
    /// a pen does not round-trip — so a wavy underline still saves as wavy even
    /// though it is drawn approximately.
    /// </summary>
    internal static void SetUnderline(RichTextBox rtb, UnderlineKind kind)
    {
        rtb.BeginChange();
        try
        {
            var existing = rtb.Selection.GetPropertyValue(Inline.TextDecorationsProperty)
                as TextDecorationCollection;

            // Keep strikethrough; replace whatever underlines were there.
            var result = new TextDecorationCollection(
                existing?.Where(d => d.Location != TextDecorationLocation.Underline) ?? []);

            foreach (var decoration in UnderlineDecorations(kind))
            {
                result.Add(decoration);
            }

            result.Freeze();
            rtb.Selection.ApplyPropertyValue(Inline.TextDecorationsProperty, result);
            SpProps.ApplyToSelection(rtb.Selection, SpProps.UnderlineProperty, kind);
        }
        finally
        {
            rtb.EndChange();
        }
    }

    /// <summary>
    /// Turns an underline kind into WPF decorations (Section 6.8).
    /// </summary>
    /// <remarks>
    /// Single and dotted and dashed are straightforward. Double has no built-in
    /// style, so it is two underlines at different pen offsets. Wavy is not
    /// supported by WPF at all: it is drawn with a tiled wave brush, which is an
    /// approximation, and the user guide says so.
    /// </remarks>
    private static IEnumerable<TextDecoration> UnderlineDecorations(UnderlineKind kind)
    {
        switch (kind)
        {
            case UnderlineKind.None:
                yield break;

            case UnderlineKind.Single:
                yield return TextDecorations.Underline[0];
                yield break;

            case UnderlineKind.Double:
                yield return Underline(null, 0);
                yield return Underline(null, 3);
                yield break;

            case UnderlineKind.Dotted:
                yield return Underline(new DashStyle([1, 2], 0), 0);
                yield break;

            case UnderlineKind.Dashed:
                yield return Underline(new DashStyle([3, 3], 0), 0);
                yield break;

            case UnderlineKind.Wavy:
                yield return WavyUnderline();
                yield break;

            default:
                yield return TextDecorations.Underline[0];
                yield break;
        }
    }

    private static TextDecoration Underline(DashStyle? dash, double penOffset)
    {
        var pen = new Pen(Brushes.Black, 1);
        if (dash is not null)
        {
            dash.Freeze();
            pen.DashStyle = dash;
        }

        pen.Freeze();

        var decoration = new TextDecoration
        {
            Location = TextDecorationLocation.Underline,
            Pen = pen,
            PenOffset = penOffset,
            PenOffsetUnit = TextDecorationUnit.Pixel,
            PenThicknessUnit = TextDecorationUnit.Pixel,
        };

        decoration.Freeze();
        return decoration;
    }

    /// <summary>
    /// A wavy underline drawn with a small tiled wave (Section 6.8). WPF has no
    /// wavy text decoration, so this is the honest approximation; the kind is
    /// stored separately so the file keeps the real answer.
    /// </summary>
    private static TextDecoration WavyUnderline()
    {
        var wave = new PathGeometry();
        var figure = new PathFigure { StartPoint = new Point(0, 2) };
        figure.Segments.Add(new QuadraticBezierSegment(new Point(1, 0), new Point(2, 2), true));
        figure.Segments.Add(new QuadraticBezierSegment(new Point(3, 4), new Point(4, 2), true));
        wave.Figures.Add(figure);
        wave.Freeze();

        var brush = new DrawingBrush(new GeometryDrawing(null, new Pen(Brushes.Black, 0.7), wave))
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, 4, 4),
            ViewportUnits = BrushMappingMode.Absolute,
            Viewbox = new Rect(0, 0, 4, 4),
            ViewboxUnits = BrushMappingMode.Absolute,
        };
        brush.Freeze();

        var pen = new Pen(brush, 4);
        pen.Freeze();

        var decoration = new TextDecoration
        {
            Location = TextDecorationLocation.Underline,
            Pen = pen,
            PenOffset = 1,
            PenOffsetUnit = TextDecorationUnit.Pixel,
            PenThicknessUnit = TextDecorationUnit.Pixel,
        };

        decoration.Freeze();
        return decoration;
    }

    /// <summary>
    /// Super and subscript (Section 6.8).
    /// </summary>
    /// <remarks>
    /// WPF's own toggle uses <c>Typography.Variants</c>, which does nothing
    /// unless the font happens to carry superscript glyphs — on Calibri it
    /// silently has no effect, which is worse than not offering it. So this
    /// shifts the baseline and shrinks the size to 65% instead, which works in
    /// every font, and records the original size so turning it off restores it.
    /// </remarks>
    internal static void SetVerticalAlignment(RichTextBox rtb, VAlign align)
    {
        rtb.BeginChange();
        try
        {
            // A mixed selection counts as Normal, so pressing superscript on a
            // part-superscript selection makes all of it superscript rather
            // than toggling half of it off.
            var currentAlign = SpProps.ReadVAlign(rtb.Selection) ?? VAlign.Normal;

            // Pressing superscript on text that is already superscript turns it
            // off, the way the toolbar button looks like it should.
            if (currentAlign == align)
            {
                align = VAlign.Normal;
            }

            var baseSize = BaseSizeDip(rtb);

            if (align == VAlign.Normal)
            {
                rtb.Selection.ApplyPropertyValue(Inline.BaselineAlignmentProperty, BaselineAlignment.Baseline);
                if (baseSize is { } restore)
                {
                    rtb.Selection.ApplyPropertyValue(TextElement.FontSizeProperty, restore);
                }

                SpProps.ApplyToSelection(rtb.Selection, SpProps.BaseSizePtProperty, null);
            }
            else
            {
                if (currentAlign == VAlign.Normal && baseSize is { } original)
                {
                    // FontSize first: applying a real WPF property is what
                    // splits the runs at the selection edges, so the attached
                    // property then lands on elements that match the selection.
                    rtb.Selection.ApplyPropertyValue(
                        TextElement.FontSizeProperty, original * VerticalShrink);
                    SpProps.ApplyToSelection(
                        rtb.Selection, SpProps.BaseSizePtProperty, (double?)Units.DipsToPoints(original));
                }

                rtb.Selection.ApplyPropertyValue(
                    Inline.BaselineAlignmentProperty,
                    align == VAlign.Super ? BaselineAlignment.Superscript : BaselineAlignment.Subscript);
            }

            SpProps.ApplyToSelection(rtb.Selection, SpProps.VAlignProperty, align);
        }
        finally
        {
            rtb.EndChange();
        }
    }

    /// <summary>
    /// The font size in DIPs before super or subscript shrank it, or the
    /// current size when it was never shrunk.
    /// </summary>
    private static double? BaseSizeDip(RichTextBox rtb)
    {
        if (SpProps.ReadBaseSizePt(rtb.Selection) is { } storedPt)
        {
            return Units.PointsToDips(storedPt);
        }

        var size = rtb.Selection.GetPropertyValue(TextElement.FontSizeProperty);
        return size == DependencyProperty.UnsetValue ? null : (double)size;
    }

    /// <summary>
    /// Small caps (Section 6.8). This only shows on OpenType fonts that carry a
    /// small-caps feature; on other fonts the letters stay as they are. That is
    /// WPF's behaviour and the user guide states it, rather than the app
    /// pretending to have done something.
    /// </summary>
    internal static void ToggleSmallCaps(RichTextBox rtb)
    {
        var current = rtb.Selection.GetPropertyValue(Typography.CapitalsProperty);
        var isSmallCaps = current != DependencyProperty.UnsetValue && Equals(current, FontCapitals.SmallCaps);

        rtb.Selection.ApplyPropertyValue(
            Typography.CapitalsProperty, isSmallCaps ? FontCapitals.Normal : FontCapitals.SmallCaps);
    }

    /// <summary>
    /// Applies a font family by name (Section 6.6).
    /// </summary>
    /// <remarks>
    /// The name goes through <see cref="Fonts.FontRegistry.Resolve"/> rather
    /// than into <c>new FontFamily(name)</c>. A font from the <c>fonts/</c>
    /// folder is not installed on the PC, so WPF cannot find it by name alone -
    /// it needs the cache file URI the registry holds. Constructing the family
    /// from the bare name does not throw; it quietly falls back to the default
    /// font, which looks exactly like the font being broken.
    /// <para>
    /// The name the user asked for is also recorded in
    /// <see cref="Bridge.SpProps.RequestedFont"/>, so saving writes back the
    /// name rather than whatever was shown (Section 6.6).
    /// </para>
    /// </remarks>
    internal static void SetFontFamily(RichTextBox rtb, string family)
    {
        if (string.IsNullOrWhiteSpace(family))
        {
            return;
        }

        var name = family.Trim();

        rtb.BeginChange();
        try
        {
            rtb.Selection.ApplyPropertyValue(
                TextElement.FontFamilyProperty,
                Fonts.FontRegistry.Resolve(name));

            SpProps.ApplyToSelection(rtb.Selection, SpProps.RequestedFontProperty, name);
        }
        finally
        {
            rtb.EndChange();
        }
    }

    internal static void SetFontSize(RichTextBox rtb, double sizePt)
    {
        if (sizePt is < MinSizePt or > MaxSizePt)
        {
            return;
        }

        rtb.Selection.ApplyPropertyValue(TextElement.FontSizeProperty, Units.PointsToDips(sizePt));
    }

    internal static void GrowFont(RichTextBox rtb) => StepFontSize(rtb, up: true);

    internal static void ShrinkFont(RichTextBox rtb) => StepFontSize(rtb, up: false);

    /// <summary>
    /// Steps through <see cref="PresetSizesPt"/> rather than adding a fixed
    /// amount, so the sizes a user lands on are the ones in the picker.
    /// </summary>
    private static void StepFontSize(RichTextBox rtb, bool up)
    {
        var value = rtb.Selection.GetPropertyValue(TextElement.FontSizeProperty);
        if (value == DependencyProperty.UnsetValue)
        {
            return;
        }

        var currentPt = Units.DipsToPoints((double)value);

        var next = up
            ? PresetSizesPt.FirstOrDefault(s => s > currentPt + 0.01, PresetSizesPt[^1])
            : PresetSizesPt.LastOrDefault(s => s < currentPt - 0.01, PresetSizesPt[0]);

        SetFontSize(rtb, next);
    }

    internal static void SetTextColor(RichTextBox rtb, Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        rtb.Selection.ApplyPropertyValue(TextElement.ForegroundProperty, brush);
    }

    /// <summary>Highlight. A null clears it back to no highlight.</summary>
    internal static void SetHighlight(RichTextBox rtb, Color? color)
    {
        if (color is null)
        {
            rtb.Selection.ApplyPropertyValue(TextElement.BackgroundProperty, null);
            return;
        }

        var brush = new SolidColorBrush(color.Value);
        brush.Freeze();
        rtb.Selection.ApplyPropertyValue(TextElement.BackgroundProperty, brush);
    }

    /// <summary>
    /// Change Case (Section 6.8). This rewrites the letters rather than applying
    /// a format, because WPF has no all-caps format and a format that only looks
    /// like capitals would be lost on save. One undo step reverses the whole
    /// thing.
    /// </summary>
    internal static void ChangeCase(RichTextBox rtb, CaseMode mode)
    {
        if (rtb.Selection.IsEmpty)
        {
            return;
        }

        rtb.BeginChange();
        try
        {
            // Walking the runs rather than replacing Selection.Text keeps each
            // run's own formatting: replacing the text wholesale would flatten
            // a part-bold selection to one format.
            foreach (var run in RunsIn(rtb.Selection).ToList())
            {
                var range = new TextRange(
                    MaxPointer(run.ContentStart, rtb.Selection.Start),
                    MinPointer(run.ContentEnd, rtb.Selection.End));

                if (!range.IsEmpty)
                {
                    range.Text = Recase(range.Text, mode);
                }
            }
        }
        finally
        {
            rtb.EndChange();
        }
    }

    private static string Recase(string text, CaseMode mode) => mode switch
    {
        CaseMode.Upper => text.ToUpper(CultureInfo.CurrentCulture),
        CaseMode.Lower => text.ToLower(CultureInfo.CurrentCulture),
        CaseMode.Title => CultureInfo.CurrentCulture.TextInfo.ToTitleCase(
            text.ToLower(CultureInfo.CurrentCulture)),
        CaseMode.Sentence => ToSentenceCase(text),
        _ => text,
    };

    private static string ToSentenceCase(string text)
    {
        var lower = text.ToLower(CultureInfo.CurrentCulture).ToCharArray();
        var startOfSentence = true;

        for (var i = 0; i < lower.Length; i++)
        {
            if (startOfSentence && char.IsLetter(lower[i]))
            {
                lower[i] = char.ToUpper(lower[i], CultureInfo.CurrentCulture);
                startOfSentence = false;
            }
            else if (lower[i] is '.' or '!' or '?')
            {
                startOfSentence = true;
            }
        }

        return new string(lower);
    }

    // ------------------------------------------------------- paragraph ------

    internal static void SetAlignment(RichTextBox rtb, Align align) =>
        rtb.Selection.ApplyPropertyValue(
            Block.TextAlignmentProperty,
            align switch
            {
                Align.Center => TextAlignment.Center,
                Align.Right => TextAlignment.Right,
                Align.Justify => TextAlignment.Justify,
                _ => TextAlignment.Left,
            });

    /// <summary>
    /// Line spacing as a multiple, for example 1.15. WPF has no "multiple"
    /// setting, so this is LineHeight in DIPs with
    /// <c>LineStackingStrategy.BlockLineHeight</c>, which is what makes WPF
    /// honour the height instead of using the font's own.
    /// </summary>
    internal static void SetLineSpacing(RichTextBox rtb, double multiple)
    {
        if (multiple <= 0)
        {
            return;
        }

        rtb.BeginChange();
        try
        {
            foreach (var paragraph in ParagraphsIn(rtb.Selection))
            {
                paragraph.LineHeight = paragraph.FontSize * multiple;
                paragraph.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
            }
        }
        finally
        {
            rtb.EndChange();
        }
    }

    internal static void SetSpacing(RichTextBox rtb, double? beforePt, double? afterPt)
    {
        rtb.BeginChange();
        try
        {
            foreach (var paragraph in ParagraphsIn(rtb.Selection))
            {
                var margin = paragraph.Margin;
                paragraph.Margin = new Thickness(
                    margin.Left,
                    beforePt is { } before ? Units.PointsToDips(before) : margin.Top,
                    margin.Right,
                    afterPt is { } after ? Units.PointsToDips(after) : margin.Bottom);
            }
        }
        finally
        {
            rtb.EndChange();
        }
    }

    internal static void SetIndents(RichTextBox rtb, double? leftPt, double? rightPt, double? firstLinePt)
    {
        rtb.BeginChange();
        try
        {
            foreach (var paragraph in ParagraphsIn(rtb.Selection))
            {
                var margin = paragraph.Margin;
                paragraph.Margin = new Thickness(
                    leftPt is { } left ? Units.PointsToDips(left) : margin.Left,
                    margin.Top,
                    rightPt is { } right ? Units.PointsToDips(right) : margin.Right,
                    margin.Bottom);

                if (firstLinePt is { } first)
                {
                    paragraph.TextIndent = Units.PointsToDips(first);
                }
            }
        }
        finally
        {
            rtb.EndChange();
        }
    }

    /// <summary>
    /// Applies a paragraph style (Section 6.5): the paragraph properties and the
    /// base run formatting, plus the style name so the walker and the style
    /// picker can read it back.
    /// </summary>
    internal static void SetStyle(RichTextBox rtb, string styleName, StyleSheet styles)
    {
        var style = styles[styleName];

        rtb.BeginChange();
        try
        {
            foreach (var paragraph in ParagraphsIn(rtb.Selection))
            {
                ApplyStyleToParagraph(paragraph, style, styles);
            }
        }
        finally
        {
            rtb.EndChange();
        }
    }

    /// <summary>
    /// Writes one style onto one paragraph. Shared by
    /// <see cref="SetStyle"/> and by <c>EditorView.ApplyDefaults</c>, so a new
    /// document's first paragraph is set up exactly the way an explicitly
    /// styled one is.
    /// </summary>
    internal static void ApplyStyleToParagraph(Paragraph paragraph, ParagraphStyle style, StyleSheet styles)
    {
        SpProps.SetStyleName(paragraph, style.Name);

        var normal = styles[StyleSheet.NormalStyleName];
        var family = style.Family ?? normal.Family;

        if (family is not null)
        {
            paragraph.FontFamily = new FontFamily(family);
        }

        paragraph.FontSize = Units.PointsToDips(style.SizePt);
        paragraph.FontWeight = style.Bold ? FontWeights.Bold : FontWeights.Normal;
        paragraph.FontStyle = style.Italic ? FontStyles.Italic : FontStyles.Normal;

        // ClearValue rather than assigning null: a null Brush means "paint
        // nothing", while clearing lets the value inherit from the document,
        // which is what a style without its own colour should do.
        if (style.Color is { } color && ParseColor(color) is { } brush)
        {
            paragraph.Foreground = brush;
        }
        else
        {
            paragraph.ClearValue(TextElement.ForegroundProperty);
        }

        if (style.Shading is { } shading && ParseColor(shading) is { } shade)
        {
            paragraph.Background = shade;
        }
        else
        {
            paragraph.ClearValue(TextElement.BackgroundProperty);
        }

        paragraph.Margin = new Thickness(
            Units.PointsToDips(style.IndentLeftPt),
            Units.PointsToDips(style.SpaceBeforePt),
            0,
            Units.PointsToDips(style.SpaceAfterPt));

        if (style.LineSpacing > 0)
        {
            paragraph.LineHeight = paragraph.FontSize * style.LineSpacing;
            paragraph.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
        }

        // A style sets the paragraph's own values. Runs inside it that carry
        // their own direct formatting keep it, which is how Word behaves and why
        // Clear Formatting exists as a separate action.
        foreach (var inline in paragraph.Inlines.ToList())
        {
            inline.ClearValue(TextElement.FontSizeProperty);
            inline.ClearValue(TextElement.FontWeightProperty);
            inline.ClearValue(TextElement.FontStyleProperty);
            inline.ClearValue(TextElement.FontFamilyProperty);
        }
    }

    private static SolidColorBrush? ParseColor(string hex)
    {
        try
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            return brush;
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or NullReferenceException)
        {
            // styles.json is user-editable, so a bad color is a typo, not a bug.
            return null;
        }
    }

    /// <summary>
    /// Clear Formatting (Ctrl+Space): drops the direct formatting and leaves the
    /// paragraph's style, which is the distinction that makes styles worth
    /// having.
    /// </summary>
    internal static void ClearFormatting(RichTextBox rtb, StyleSheet styles)
    {
        rtb.BeginChange();
        try
        {
            var selection = rtb.Selection;

            // TextRange.ClearAllProperties is the right tool here: it removes
            // every formatting property from the range and splits runs at the
            // selection edges on its own. Applying DependencyProperty.UnsetValue
            // through ApplyPropertyValue is not a documented way to clear a
            // property and does not reliably do it.
            selection.ClearAllProperties();

            SpProps.ApplyToSelection(selection, SpProps.UnderlineProperty, UnderlineKind.None);
            SpProps.ApplyToSelection(selection, SpProps.VAlignProperty, VAlign.Normal);
            SpProps.ApplyToSelection(selection, SpProps.BaseSizePtProperty, null);

            // Re-apply each paragraph's own style, so clearing returns the text
            // to its style rather than to nothing at all.
            foreach (var paragraph in ParagraphsIn(selection))
            {
                var name = SpProps.GetStyleName(paragraph) ?? StyleSheet.NormalStyleName;
                ApplyStyleToParagraph(paragraph, styles[name], styles);
            }
        }
        finally
        {
            rtb.EndChange();
        }
    }

    // ----------------------------------------------------------- walking ----

    /// <summary>Every paragraph the selection touches, including a caret with no selection.</summary>
    internal static IEnumerable<Paragraph> ParagraphsIn(TextSelection selection)
    {
        var current = selection.Start.Paragraph;
        var last = selection.End.Paragraph;

        while (current is not null)
        {
            yield return current;

            if (ReferenceEquals(current, last))
            {
                yield break;
            }

            current = current.NextBlock as Paragraph;
        }
    }

    private static IEnumerable<Run> RunsIn(TextSelection selection)
    {
        var pointer = selection.Start;

        while (pointer is not null && pointer.CompareTo(selection.End) < 0)
        {
            if (pointer.Parent is Run run)
            {
                yield return run;
                pointer = run.ContentEnd.GetNextContextPosition(LogicalDirection.Forward);
            }
            else
            {
                pointer = pointer.GetNextContextPosition(LogicalDirection.Forward);
            }
        }
    }

    private static TextPointer MaxPointer(TextPointer a, TextPointer b) => a.CompareTo(b) >= 0 ? a : b;

    private static TextPointer MinPointer(TextPointer a, TextPointer b) => a.CompareTo(b) <= 0 ? a : b;
}
