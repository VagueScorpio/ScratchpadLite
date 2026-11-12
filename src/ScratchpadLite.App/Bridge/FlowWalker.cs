using ScratchpadLite.Core.Platform;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.IO;
using ScratchpadLite.App.Fonts;
using ScratchpadLite.Core.Model;
using IrBlock = ScratchpadLite.Core.Model.Block;
using IrInline = ScratchpadLite.Core.Model.Inline;
using IrParagraph = ScratchpadLite.Core.Model.Paragraph;
using IrTable = ScratchpadLite.Core.Model.Table;
using WpfBlock = System.Windows.Documents.Block;
using WpfInline = System.Windows.Documents.Inline;
using WpfLineBreak = System.Windows.Documents.LineBreak;
using WpfParagraph = System.Windows.Documents.Paragraph;
using WpfTable = System.Windows.Documents.Table;

namespace ScratchpadLite.App.Bridge;

/// <summary>
/// The forklift in the other direction: FlowDocument to IR (Section 6.4).
/// UI thread only. Walks the blocks in order, turns List and ListItem
/// nesting back into paragraphs carrying list info, reads effective
/// inherited values for each run and compares them with the paragraph style
/// to decide what counts as direct formatting, and collects the fonts used.
///
/// Fonts from the font folder are recorded under their plain family name,
/// never the file URI with the fragment, or saved documents would carry a
/// path from this PC.
///
/// The most important test in the project (Section 6.4): for any IR
/// document d, walking what the builder built equals d after
/// normalization.
/// </summary>
/// <remarks>
/// The hard part is not the tree walk, it is deciding what a run's formatting
/// <em>is</em>. WPF only reports effective values: ask a run for its FontSize
/// and you get a number whether it was set on the run, inherited from the
/// paragraph, or inherited from the document. The IR has to distinguish "11 pt
/// because the style says so" from "11 pt because someone chose it", because
/// the first must not be written into the file as direct formatting - a
/// heading saved that way stops being a heading the moment its style changes.
/// <para>
/// So every run is read as an effective value and then compared against its
/// paragraph style, and only the differences become IR. That is the mirror of
/// what the builder does, and the two have to agree exactly or the round trip
/// does not close.
/// </para>
/// </remarks>
internal static class FlowWalker
{
    /// <summary>Walks a document, comparing against the style sheet.</summary>
    internal static SpDocument Walk(FlowDocument document, StyleSheet styles)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(styles);

        var fonts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var blocks = WalkBlocks(document.Blocks, styles, fonts);

        return new SpDocument(
            blocks,
            new Dictionary<string, string>(),
            new PageSetup(),
            fonts);
    }

    /// <summary>
    /// Walks a document and keeps the metadata and page setup it was opened
    /// with, which the FlowDocument has nowhere to store.
    /// </summary>
    internal static SpDocument Walk(
        FlowDocument document,
        StyleSheet styles,
        IReadOnlyDictionary<string, string> meta,
        PageSetup page)
    {
        var walked = Walk(document, styles);

        return walked with { Meta = meta, Page = page };
    }

    internal static IReadOnlyList<IrBlock> WalkBlocks(
        IEnumerable<WpfBlock> blocks,
        StyleSheet styles,
        HashSet<string> fonts)
    {
        var result = new List<IrBlock>();

        foreach (var block in blocks)
        {
            switch (block)
            {
                case WpfParagraph paragraph:
                    result.Add(WalkParagraph(paragraph, styles, fonts, list: null));
                    break;

                case List list:
                    WalkList(list, styles, fonts, level: 0, result);
                    break;

                case WpfTable table:
                    result.Add(WalkTable(table, styles, fonts));
                    break;

                case BlockUIContainer container
                    when SpProps.GetStyleName(container) == SpProps.HorizontalRuleStyleName:
                    result.Add(new HorizontalRule());
                    break;

                case Section section:
                    // WPF produces these when rich text is pasted. They carry
                    // no meaning the IR has, so their contents are lifted out.
                    result.AddRange(WalkBlocks(section.Blocks, styles, fonts));
                    break;

                case BlockUIContainer other:
                    // Something embedded that the IR cannot describe. Dropping
                    // it is better than inventing a representation for it.
                    Log.Debug($"a {other.Child?.GetType().Name ?? "null"} block was skipped by the walker");
                    break;
            }
        }

        return result;
    }

    /// <summary>
    /// Flattens WPF's nested lists back into paragraphs carrying a level
    /// (Section 6.4).
    /// </summary>
    private static void WalkList(
        List list,
        StyleSheet styles,
        HashSet<string> fonts,
        int level,
        List<IrBlock> result)
    {
        var info = new ListInfo(
            KindOf(list.MarkerStyle),
            level,
            FormatOf(list.MarkerStyle),
            Math.Max(1, list.StartIndex));

        foreach (var item in list.ListItems)
        {
            var first = true;

            foreach (var block in item.Blocks)
            {
                switch (block)
                {
                    case WpfParagraph paragraph:
                        // Only the item's first paragraph carries the marker;
                        // a second paragraph inside one item is a continuation.
                        result.Add(WalkParagraph(
                            paragraph, styles, fonts, first ? info : info with { Start = 1 }));

                        first = false;
                        break;

                    case List nested:
                        WalkList(nested, styles, fonts, level + 1, result);
                        break;

                    default:
                        result.AddRange(WalkBlocks([block], styles, fonts));
                        break;
                }
            }
        }
    }

    private static ListKind KindOf(TextMarkerStyle marker) => marker switch
    {
        TextMarkerStyle.Disc or TextMarkerStyle.Circle or TextMarkerStyle.Square or TextMarkerStyle.Box =>
            ListKind.Bullet,
        TextMarkerStyle.None => ListKind.Bullet,
        _ => ListKind.Number,
    };

    private static string FormatOf(TextMarkerStyle marker) => marker switch
    {
        TextMarkerStyle.LowerLatin => "lowerLetter",
        TextMarkerStyle.UpperLatin => "upperLetter",
        TextMarkerStyle.LowerRoman => "lowerRoman",
        TextMarkerStyle.UpperRoman => "upperRoman",
        _ => "decimal",
    };

    private static IrParagraph WalkParagraph(
        WpfParagraph paragraph,
        StyleSheet styles,
        HashSet<string> fonts,
        ListInfo? list)
    {
        var styleName = SpProps.GetStyleName(paragraph);
        var style = styles[styleName];

        // A list paragraph's indents are WPF's own doing - the List element
        // supplies them - so they are not the paragraph's formatting and must
        // not be written back as such.
        var indentLeft = list is null
            ? Units.DipsToPoints(paragraph.Margin.Left)
            : style.IndentLeftPt;

        var spaceBefore = Units.DipsToPoints(paragraph.Margin.Top);
        var spaceAfter = Units.DipsToPoints(paragraph.Margin.Bottom);

        return new IrParagraph
        {
            StyleName = string.IsNullOrEmpty(styleName) ? StyleSheet.NormalStyleName : styleName,
            Align = paragraph.TextAlignment switch
            {
                TextAlignment.Center => Align.Center,
                TextAlignment.Right => Align.Right,
                TextAlignment.Justify => Align.Justify,
                _ => Align.Left,
            },
            IndentLeftPt = Near(indentLeft, style.IndentLeftPt) ? 0 : indentLeft,
            IndentRightPt = Units.DipsToPoints(paragraph.Margin.Right),
            FirstLinePt = Units.DipsToPoints(paragraph.TextIndent),
            SpaceBeforePt = Near(spaceBefore, style.SpaceBeforePt) ? null : spaceBefore,
            SpaceAfterPt = Near(spaceAfter, style.SpaceAfterPt) ? null : spaceAfter,
            LineSpacing = LineSpacingOf(paragraph, style),
            List = list,
            PageBreakBefore = paragraph.BreakPageBefore,
            Inlines = WalkInlines(paragraph.Inlines, style, styles, fonts),
        };
    }

    /// <summary>
    /// The line spacing as a multiple, or null when it matches the style.
    /// </summary>
    private static double? LineSpacingOf(WpfParagraph paragraph, ParagraphStyle style)
    {
        var height = paragraph.LineHeight;

        if (double.IsNaN(height) || height <= 0 || paragraph.FontSize <= 0)
        {
            return null;
        }

        var multiple = height / paragraph.FontSize;

        return Near(multiple, style.LineSpacing, 0.01) ? null : multiple;
    }

    private static IrTable WalkTable(WpfTable table, StyleSheet styles, HashSet<string> fonts)
    {
        var rows = new List<IReadOnlyList<Cell>>();

        foreach (var group in table.RowGroups)
        {
            foreach (var row in group.Rows)
            {
                var cells = new List<Cell>(row.Cells.Count);

                foreach (var cell in row.Cells)
                {
                    cells.Add(new Cell(
                        WalkBlocks(cell.Blocks, styles, fonts),
                        cell.ColumnSpan,
                        cell.RowSpan,
                        ColorOf(cell.Background)));
                }

                rows.Add(cells);
            }
        }

        var widths = table.Columns
            .Select(c => c.Width.IsAbsolute ? Units.DipsToPoints(c.Width.Value) : 0)
            .ToList();

        return new IrTable(
            rows,
            widths.Any(w => w > 0) ? widths : null,
            table.RowGroups.Count > 0 && HasBorders(table));
    }

    private static bool HasBorders(WpfTable table)
    {
        foreach (var group in table.RowGroups)
        {
            foreach (var row in group.Rows)
            {
                foreach (var cell in row.Cells)
                {
                    if (cell.BorderThickness.Left > 0 || cell.BorderThickness.Top > 0)
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Walks a run of inlines into IR.
    /// </summary>
    /// <remarks>
    /// The <paramref name="link"/> and <paramref name="inherited"/> arguments
    /// are what a surrounding hyperlink contributes, passed down because a
    /// hyperlink's content is not always a flat list of runs. WPF's RTF reader
    /// puts a Span inside the Hyperlink, and the first version of this method
    /// looked only at direct Run children - so every link in an opened .rtf
    /// lost its text entirely, not just its target.
    /// </remarks>
    internal static IReadOnlyList<IrInline> WalkInlines(
        IEnumerable<WpfInline> inlines,
        ParagraphStyle style,
        StyleSheet styles,
        HashSet<string> fonts,
        string? link = null,
        Brush? inherited = null)
    {
        var result = new List<IrInline>();

        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case Run run:
                    if (run.Text.Length > 0)
                    {
                        result.Add(new TextRun(
                            run.Text, StyleOf(run, style, styles, fonts, link, inherited)));
                    }

                    break;

                case Hyperlink hyperlink:
                {
                    var target = SpProps.GetLink(hyperlink)
                                 ?? hyperlink.NavigateUri?.ToString();

                    result.AddRange(WalkInlines(
                        hyperlink.Inlines,
                        style,
                        styles,
                        fonts,
                        target ?? link,
                        hyperlink.Foreground));

                    break;
                }

                case WpfLineBreak:
                    result.Add(new ScratchpadLite.Core.Model.LineBreak());
                    break;

                case InlineUIContainer container when container.Child is Image image:
                    if (WalkImage(image) is { } picture)
                    {
                        result.Add(picture);
                    }

                    break;

                case Span span:
                    // A Span is formatting applied to several runs. Its runs
                    // report the span's inherited values - weight, size,
                    // foreground - as their own effective values, so walking
                    // the children is enough for those. The two that do not
                    // inherit, TextDecorations and Background, are collected
                    // by looking up the tree from each run instead.
                    result.AddRange(WalkInlines(span.Inlines, style, styles, fonts, link, inherited));
                    break;
            }
        }

        return result;
    }

    /// <summary>
    /// An image, with the bytes it was built from rather than a re-encode
    /// (Section 6.4).
    /// </summary>
    private static ImageRun? WalkImage(Image image)
    {
        if (image.Tag is SpProps.ImagePayload payload)
        {
            return new ImageRun(
                payload.Data,
                payload.Mime,
                Units.DipsToPoints(double.IsNaN(image.Width) ? image.ActualWidth : image.Width),
                Units.DipsToPoints(double.IsNaN(image.Height) ? image.ActualHeight : image.Height),
                payload.Alt);
        }

        // An image that arrived some other way: from WPF's RTF reader, or
        // pasted. There are no original bytes, so they are made - the pixels
        // are right there in the BitmapSource, and encoding them as PNG is
        // lossless. Dropping the picture instead meant every image in an
        // opened .rtf disappeared, which is a worse answer than a re-encode.
        if (image.Source is BitmapSource bitmap)
        {
            return Encoded(image, bitmap);
        }

        Log.Debug("an image with no bytes and no bitmap was skipped by the walker");

        return null;
    }

    /// <summary>
    /// Encodes a bitmap that came with no file bytes of its own as PNG.
    /// </summary>
    private static ImageRun? Encoded(Image image, BitmapSource bitmap)
    {
        try
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));

            using var stream = new MemoryStream();
            encoder.Save(stream);

            // The displayed size where there is one, otherwise the bitmap's
            // own, which for a pasted picture is all there is.
            var width = double.IsNaN(image.Width) ? image.ActualWidth : image.Width;
            var height = double.IsNaN(image.Height) ? image.ActualHeight : image.Height;

            if (width <= 0 || height <= 0)
            {
                width = bitmap.Width;
                height = bitmap.Height;
            }

            return new ImageRun(
                stream.ToArray(),
                "image/png",
                Units.DipsToPoints(width),
                Units.DipsToPoints(height),
                image.ToolTip as string ?? "");
        }
        catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException
                                       or ArgumentException or IOException)
        {
            // A bitmap that cannot be encoded is not worth failing a save for.
            Log.Warn($"an image could not be encoded and was left out: {ex.Message}");

            return null;
        }
    }

    /// <summary>
    /// A run's formatting, as the difference between its effective values and
    /// its paragraph style.
    /// </summary>
    private static CharStyle StyleOf(
        Run run,
        ParagraphStyle style,
        StyleSheet styles,
        HashSet<string> fonts,
        string? link,
        Brush? inherited)
    {
        var family = FamilyOf(run);

        if (family is { } named)
        {
            fonts.Add(named);
        }

        // The size has to come from SpProps when the run is super- or
        // subscript, because the FontSize on it is the shrunken one
        // (Section 6.8).
        var vertical = SpProps.GetVAlign(run);
        var sizePt = vertical != VAlign.Normal && SpProps.GetBaseSizePt(run) is { } baseSize
            ? baseSize
            : Units.DipsToPoints(run.FontSize);

        var preserved = SpProps.GetPreserved(run);

        return new CharStyle
        {
            Family = Same(family, styles.FamilyFor(style)) ? null : family,
            SizePt = Near(sizePt, style.SizePt) ? null : sizePt,
            Bold = run.FontWeight.ToOpenTypeWeight() >= FontWeights.Bold.ToOpenTypeWeight(),
            Italic = run.FontStyle != FontStyles.Normal,
            Underline = UnderlineOf(run),
            Strike = HasStrikethrough(run),
            Vertical = vertical,
            SmallCaps = Typography.GetCapitals(run) == FontCapitals.SmallCaps,
            AllCapsPreserved = preserved?.AllCaps ?? false,
            LetterSpacingPtPreserved = preserved?.LetterSpacingPt,
            Color = ForegroundOf(run, style, inherited),
            Highlight = ColorOf(BackgroundOf(run)),
            Link = link,
        };
    }

    /// <summary>
    /// The family name to record: the one the file asked for where there is
    /// one, otherwise the family's own name - never the cache file's URI
    /// (Section 6.4).
    /// </summary>
    /// <remarks>
    /// A font from the <c>fonts/</c> folder has a Source like
    /// <c>./a1b2c3.otf#Nimbus Roman</c> relative to this PC's cache folder.
    /// Writing that into a saved document would put a path from this machine
    /// into the file and would mean nothing anywhere else.
    /// </remarks>
    private static string? FamilyOf(Run run)
    {
        if (SpProps.GetRequestedFont(run) is { Length: > 0 } requested)
        {
            return requested;
        }

        var family = run.FontFamily;

        if (family is null)
        {
            return null;
        }

        var name = FontRegistry.DisplayName(family);

        if (name.Contains('#', StringComparison.Ordinal))
        {
            // "./file.otf#Family Name" - keep the part after the fragment.
            name = name[(name.IndexOf('#', StringComparison.Ordinal) + 1)..];
        }

        return name.Length == 0 ? null : name;
    }

    private static UnderlineKind UnderlineOf(Run run)
    {
        var kind = SpProps.GetUnderline(run);

        if (kind != UnderlineKind.None)
        {
            return kind;
        }

        // No attached property, so this came from somewhere other than the
        // builder - a paste, or WPF's own RTF reader. A plain underline is the
        // only thing that can be told from the decorations alone.
        return HasUnderline(run) ? UnderlineKind.Single : UnderlineKind.None;
    }

    private static bool HasUnderline(Run run) =>
        HasDecoration(run, TextDecorationLocation.Underline);

    private static bool HasStrikethrough(Run run) =>
        HasDecoration(run, TextDecorationLocation.Strikethrough);

    /// <summary>
    /// Whether this run is decorated, by itself or by something it sits
    /// inside.
    /// </summary>
    /// <remarks>
    /// TextDecorations is not an inheriting property, but it renders as though
    /// it were: a Span marked underlined draws every run inside it underlined,
    /// while each of those runs reports an empty TextDecorations of its own.
    /// So the effective value can only be found by walking up.
    /// <para>
    /// It matters because WPF's RTF reader puts the decoration on a Span, so
    /// reading only the run lost every underline and every strikethrough in
    /// an opened .rtf - and the same would happen to pasted text.
    /// </para>
    /// <para>
    /// The walk stops at a Hyperlink, whose underline belongs to the link
    /// style rather than to the text. Writing it down as direct formatting
    /// would mean a link saved and reopened came back as underlined text that
    /// happens to be a link.
    /// </para>
    /// </remarks>
    private static bool HasDecoration(Run run, TextDecorationLocation where)
    {
        for (WpfInline? inline = run; inline is not null and not Hyperlink; inline = inline.Parent as WpfInline)
        {
            if (inline.TextDecorations.Any(d => d.Location == where))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The highlight behind this run, from itself or from something it sits
    /// inside.
    /// </summary>
    /// <remarks>
    /// Background does not inherit either, for the same reason: a Span's
    /// background is painted behind its content, not inherited by it. The walk
    /// stops at the first Inline that has one, and never leaves the inlines -
    /// a Paragraph's background is paragraph shading, which is not a run's
    /// highlight.
    /// </remarks>
    private static Brush? BackgroundOf(Run run)
    {
        for (WpfInline? inline = run; inline is not null; inline = inline.Parent as WpfInline)
        {
            if (inline.Background is { } brush)
            {
                return brush;
            }
        }

        return null;
    }

    /// <summary>
    /// The foreground colour, or null when it matches the paragraph style.
    /// </summary>
    private static string? ForegroundOf(Run run, ParagraphStyle style, Brush? inherited)
    {
        var color = ColorOf(run.Foreground);

        // A run inside a Hyperlink reports the link style's blue as its
        // effective foreground. That is the link being a link, not a colour
        // anyone chose, so it is not direct formatting.
        if (inherited is not null && color == ColorOf(inherited))
        {
            return null;
        }

        if (color is null)
        {
            return null;
        }

        var styleColor = IrNormalizer.NormalizeColor(style.Color);

        // A style with no colour of its own renders black, so a black run in
        // such a paragraph is not direct formatting.
        styleColor ??= "#000000";

        return color == styleColor ? null : color;
    }

    /// <summary>A brush as an IR colour string, or null.</summary>
    private static string? ColorOf(Brush? brush)
    {
        if (brush is not SolidColorBrush solid)
        {
            return null;
        }

        if (solid.Color.A == 0)
        {
            return null;        // transparent is "no colour"
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"#{solid.Color.R:X2}{solid.Color.G:X2}{solid.Color.B:X2}");
    }

    private static bool Same(string? a, string? b) =>
        string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Whether two point measurements are the same.
    /// </summary>
    /// <remarks>
    /// Needed because every size makes the trip through DIPs - a multiply and
    /// a divide by 96/72 - so a value that went in as 10.5 comes back as
    /// 10.499999999999998. Comparing those exactly would make every run look
    /// like direct formatting.
    /// </remarks>
    private static bool Near(double a, double b, double tolerance = 0.001) =>
        Math.Abs(a - b) <= tolerance;
}
