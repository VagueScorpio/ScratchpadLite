using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ScratchpadLite.App.Fonts;
using ScratchpadLite.Core.Model;
using ScratchpadLite.Core.Platform;
using IrBlock = ScratchpadLite.Core.Model.Block;
using IrInline = ScratchpadLite.Core.Model.Inline;
using IrParagraph = ScratchpadLite.Core.Model.Paragraph;
using IrTable = ScratchpadLite.Core.Model.Table;
using WpfBlock = System.Windows.Documents.Block;
using WpfInline = System.Windows.Documents.Inline;
using WpfParagraph = System.Windows.Documents.Paragraph;
using WpfLineBreak = System.Windows.Documents.LineBreak;
using WpfTable = System.Windows.Documents.Table;

namespace ScratchpadLite.App.Bridge;

/// <summary>
/// The forklift that moves a pallet onto the warehouse floor: IR to
/// FlowDocument (Section 4.1 and 6.4). UI thread only, because a
/// FlowDocument belongs to the thread that owns the window. Everything
/// before this step can run in the background, which is how a large file
/// opens without freezing the window.
///
/// Font sizes convert points to device-independent pixels, consecutive
/// paragraphs that share list info nest into List and ListItem elements,
/// images keep their original bytes and MIME type in the Image tag so a
/// save writes them back untouched, and every brush and pen it creates is
/// frozen.
/// </summary>
/// <remarks>
/// Two rules worth stating plainly, because the walker depends on both.
/// <list type="number">
///   <item>
///     Anything the IR knows that WPF has no property for goes into an
///     attached property from <see cref="SpProps"/>. There is no other
///     channel: if it is not a WPF property and not an attached property, it
///     is lost.
///   </item>
///   <item>
///     Only direct formatting is set. A run whose style matches its paragraph
///     style has nothing set on it at all, so the walker can tell the
///     difference between "this run is 11 pt because the style says so" and
///     "this run is 11 pt because someone typed it".
///   </item>
/// </list>
/// </remarks>
internal static class FlowBuilder
{
    /// <summary>Builds a FlowDocument from a document.</summary>
    /// <param name="styles">
    /// The style sheet, so a run's formatting can be compared with its
    /// paragraph style and only the differences written.
    /// </param>
    internal static FlowDocument Build(SpDocument doc, StyleSheet styles)
    {
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentNullException.ThrowIfNull(styles);

        var flow = new FlowDocument
        {
            PagePadding = new Thickness(0),
        };

        foreach (var block in BuildBlocks(doc.Blocks, styles))
        {
            flow.Blocks.Add(block);
        }

        // A FlowDocument with no blocks cannot be typed in.
        if (flow.Blocks.Count == 0)
        {
            flow.Blocks.Add(new WpfParagraph());
        }

        return flow;
    }

    /// <summary>
    /// Turns IR blocks into WPF blocks, gathering runs of list paragraphs into
    /// List elements as it goes.
    /// </summary>
    internal static IReadOnlyList<WpfBlock> BuildBlocks(IReadOnlyList<IrBlock> blocks, StyleSheet styles)
    {
        var result = new List<WpfBlock>(blocks.Count);
        var at = 0;

        while (at < blocks.Count)
        {
            // Consecutive paragraphs that carry list info become one nested
            // List structure, which is how WPF represents a list; the IR keeps
            // them flat with a level on each, which is how every file format
            // does. Converting between the two is the only reason this loop is
            // not a foreach.
            if (blocks[at] is IrParagraph { List: not null })
            {
                var run = new List<IrParagraph>();

                while (at < blocks.Count && blocks[at] is IrParagraph { List: not null } listParagraph)
                {
                    run.Add(listParagraph);
                    at++;
                }

                result.AddRange(BuildLists(run, styles));
                continue;
            }

            result.Add(blocks[at] switch
            {
                IrParagraph paragraph => BuildParagraph(paragraph, styles),
                IrTable table => BuildTable(table, styles),
                HorizontalRule => BuildRule(),
                var other => throw new InvalidOperationException($"unknown block type {other.GetType().Name}"),
            });

            at++;
        }

        return result;
    }

    private static WpfParagraph BuildParagraph(IrParagraph paragraph, StyleSheet styles)
    {
        var style = styles[paragraph.StyleName];
        var wpf = new WpfParagraph();

        SpProps.SetStyleName(wpf, paragraph.StyleName);

        // The paragraph style's own formatting goes on the paragraph, so a run
        // inside it inherits and needs nothing set.
        ApplyStyle(wpf, style, styles);

        wpf.TextAlignment = paragraph.Align switch
        {
            Align.Center => TextAlignment.Center,
            Align.Right => TextAlignment.Right,
            Align.Justify => TextAlignment.Justify,
            _ => TextAlignment.Left,
        };

        var left = paragraph.IndentLeftPt != 0 ? Units.PointsToDips(paragraph.IndentLeftPt) : wpf.Margin.Left;
        var right = paragraph.IndentRightPt != 0 ? Units.PointsToDips(paragraph.IndentRightPt) : wpf.Margin.Right;
        var top = paragraph.SpaceBeforePt is { } before ? Units.PointsToDips(before) : wpf.Margin.Top;
        var bottom = paragraph.SpaceAfterPt is { } after ? Units.PointsToDips(after) : wpf.Margin.Bottom;

        wpf.Margin = new Thickness(left, top, right, bottom);
        wpf.TextIndent = Units.PointsToDips(paragraph.FirstLinePt);
        wpf.BreakPageBefore = paragraph.PageBreakBefore;

        if (paragraph.LineSpacing is { } spacing && spacing > 0)
        {
            // LineHeight alone is not enough: without BlockLineHeight, WPF
            // treats it as a minimum and a tall glyph silently overrides it.
            wpf.LineHeight = spacing * wpf.FontSize;
            wpf.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
        }

        foreach (var inline in BuildInlines(paragraph.Inlines, style, styles))
        {
            wpf.Inlines.Add(inline);
        }

        return wpf;
    }

    /// <summary>
    /// Applies a paragraph style's formatting to a WPF paragraph.
    /// </summary>
    internal static void ApplyStyle(WpfParagraph wpf, ParagraphStyle style, StyleSheet styles)
    {
        ArgumentNullException.ThrowIfNull(wpf);
        ArgumentNullException.ThrowIfNull(style);
        ArgumentNullException.ThrowIfNull(styles);

        // Resolved through the sheet rather than taken as-is, so a style with
        // no font of its own gets Normal's - see StyleSheet.FamilyFor.
        if (styles.FamilyFor(style) is { } family)
        {
            wpf.FontFamily = FontRegistry.Resolve(family);
        }

        if (style.SizePt > 0)
        {
            wpf.FontSize = Units.PointsToDips(style.SizePt);
        }

        wpf.FontWeight = style.Bold ? FontWeights.Bold : FontWeights.Normal;
        wpf.FontStyle = style.Italic ? FontStyles.Italic : FontStyles.Normal;

        if (ParseColor(style.Color) is { } color)
        {
            wpf.Foreground = color;
        }

        if (ParseColor(style.Shading) is { } shading)
        {
            wpf.Background = shading;
        }

        wpf.Margin = new Thickness(
            Units.PointsToDips(style.IndentLeftPt),
            Units.PointsToDips(style.SpaceBeforePt),
            0,
            Units.PointsToDips(style.SpaceAfterPt));

        if (style.LineSpacing > 0)
        {
            wpf.LineHeight = style.LineSpacing * wpf.FontSize;
            wpf.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
        }
    }

    /// <summary>
    /// Turns a run of list paragraphs into WPF List elements.
    /// </summary>
    /// <remarks>
    /// Returns several roots rather than one, because a change of marker is a
    /// change of list. A bulleted list followed immediately by a numbered one
    /// is two lists, and folding them into a single List element silently turns
    /// the numbers into bullets - which is exactly what it did before this
    /// returned a collection.
    /// </remarks>
    private static IReadOnlyList<List> BuildLists(IReadOnlyList<IrParagraph> paragraphs, StyleSheet styles)
    {
        var roots = new List<List>();
        var open = new List<(List List, ListInfo Info)>();

        foreach (var paragraph in paragraphs)
        {
            var info = paragraph.List!;
            var level = Math.Clamp(info.Level, 0, 8);

            // Shallower, or the same level with a different marker: close this
            // level and everything under it.
            while (open.Count > level + 1
                   || (open.Count == level + 1 && !SameMarker(open[level].Info, info)))
            {
                open.RemoveAt(open.Count - 1);
            }

            // Deeper: a new List goes inside the last item of the level above,
            // or becomes a new root at level 0.
            while (open.Count <= level)
            {
                var child = NewList(info);

                if (open.Count == 0)
                {
                    roots.Add(child);
                }
                else
                {
                    LastItem(open[^1].List).Blocks.Add(child);
                }

                open.Add((child, info));
            }

            var item = new ListItem();
            item.Blocks.Add(BuildParagraph(paragraph with { List = null }, styles));
            open[level].List.ListItems.Add(item);
        }

        return roots;
    }

    /// <summary>
    /// Whether two list levels are the same list, which is decided by what
    /// their markers look like rather than by every field.
    /// </summary>
    /// <remarks>
    /// Start is deliberately not compared: a second item saying "start at 1"
    /// is not a new list, and the walker only reports a start on the first
    /// item of each one.
    /// </remarks>
    private static bool SameMarker(ListInfo a, ListInfo b) =>
        a.Kind == b.Kind
        && (a.Kind == ListKind.Bullet
            || string.Equals(a.NumberFormat, b.NumberFormat, StringComparison.Ordinal));

    private static List NewList(ListInfo info) => new()
    {
        MarkerStyle = MarkerFor(info),
        StartIndex = Math.Max(1, info.Start),
        Margin = new Thickness(0),
        Padding = new Thickness(0),
    };

    /// <summary>
    /// A list marker, from the kind and the level (Section 6.4).
    /// </summary>
    /// <remarks>
    /// Bullets cycle through three shapes by depth, the way Word does, so
    /// nested lists are readable. Numbers use whatever format the file asked
    /// for, falling back to the same depth cycle.
    /// </remarks>
    private static TextMarkerStyle MarkerFor(ListInfo info)
    {
        if (info.Kind == ListKind.Bullet)
        {
            return (info.Level % 3) switch
            {
                0 => TextMarkerStyle.Disc,
                1 => TextMarkerStyle.Circle,
                _ => TextMarkerStyle.Square,
            };
        }

        return info.NumberFormat switch
        {
            "lowerLetter" => TextMarkerStyle.LowerLatin,
            "upperLetter" => TextMarkerStyle.UpperLatin,
            "lowerRoman" => TextMarkerStyle.LowerRoman,
            "upperRoman" => TextMarkerStyle.UpperRoman,
            _ => (info.Level % 3) switch
            {
                0 => TextMarkerStyle.Decimal,
                1 => TextMarkerStyle.LowerLatin,
                _ => TextMarkerStyle.LowerRoman,
            },
        };
    }

    private static ListItem LastItem(List list)
    {
        if (list.ListItems.Count == 0)
        {
            // A list that jumps straight to a deeper level - which a .docx can
            // do - needs something to hang the sub-list from.
            list.ListItems.Add(new ListItem(new WpfParagraph()));
        }

        return list.ListItems.LastListItem!;
    }

    private static WpfTable BuildTable(IrTable table, StyleSheet styles)
    {
        var wpf = new WpfTable
        {
            CellSpacing = 0,
            Margin = new Thickness(0, 0, 0, Units.PointsToDips(8)),
        };

        var columns = table.Rows.Count == 0 ? 0 : table.Rows.Max(r => r.Sum(c => c.ColSpan));

        for (var c = 0; c < columns; c++)
        {
            wpf.Columns.Add(new TableColumn
            {
                Width = table.ColWidthsPt is { } widths && c < widths.Count
                    ? new GridLength(Units.PointsToDips(widths[c]))
                    : GridLength.Auto,
            });
        }

        var group = new TableRowGroup();
        wpf.RowGroups.Add(group);

        var border = table.Borders ? Frozen(Colors.Gray) : null;
        var thickness = table.Borders ? new Thickness(0.5) : new Thickness(0);

        foreach (var row in table.Rows)
        {
            var wpfRow = new TableRow();

            foreach (var cell in row)
            {
                var wpfCell = new TableCell
                {
                    ColumnSpan = Math.Max(1, cell.ColSpan),
                    RowSpan = Math.Max(1, cell.RowSpan),
                    Padding = new Thickness(Units.PointsToDips(4)),
                    BorderThickness = thickness,
                };

                if (border is not null)
                {
                    wpfCell.BorderBrush = border;
                }

                if (ParseColor(cell.Shading) is { } shading)
                {
                    wpfCell.Background = shading;
                }

                foreach (var block in BuildBlocks(cell.Blocks, styles))
                {
                    wpfCell.Blocks.Add(block);
                }

                if (wpfCell.Blocks.Count == 0)
                {
                    wpfCell.Blocks.Add(new WpfParagraph());
                }

                wpfRow.Cells.Add(wpfCell);
            }

            group.Rows.Add(wpfRow);
        }

        return wpf;
    }

    /// <summary>
    /// A horizontal rule, tagged so the walker can recognise it
    /// (Section 6.4).
    /// </summary>
    private static BlockUIContainer BuildRule()
    {
        var container = new BlockUIContainer(new Separator
        {
            Margin = new Thickness(0, Units.PointsToDips(6), 0, Units.PointsToDips(6)),
        });

        SpProps.SetStyleName(container, SpProps.HorizontalRuleStyleName);

        return container;
    }

    internal static IReadOnlyList<WpfInline> BuildInlines(
        IReadOnlyList<IrInline> inlines,
        ParagraphStyle style,
        StyleSheet styles)
    {
        var result = new List<WpfInline>(inlines.Count);

        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case TextRun text:
                {
                    var run = BuildRun(text, style, styles);

                    result.Add(text.Style.Link is { } link
                        ? BuildHyperlink(run, link)
                        : run);

                    break;
                }

                case ImageRun image:
                    if (BuildImage(image) is { } container)
                    {
                        result.Add(container);
                    }

                    break;

                case ScratchpadLite.Core.Model.LineBreak:
                    result.Add(new WpfLineBreak());
                    break;
            }
        }

        return result;
    }

    /// <summary>
    /// One run. Only what differs from the paragraph style is set, which is
    /// what lets the walker tell direct formatting from inherited formatting.
    /// </summary>
    private static Run BuildRun(TextRun text, ParagraphStyle style, StyleSheet styles)
    {
        var run = new Run(text.Text);
        var character = text.Style;
        var styleFamily = styles.FamilyFor(style);

        if (character.Family is { } family && !Same(family, styleFamily))
        {
            run.FontFamily = FontRegistry.Resolve(family);

            // What the file asked for, kept so a save writes the name back
            // even when a substitute is being shown (Section 6.6).
            SpProps.SetRequestedFont(run, family);
        }

        var baseSize = character.SizePt ?? style.SizePt;

        if (character.SizePt is { } size && Math.Abs(size - style.SizePt) > 0.001)
        {
            run.FontSize = Units.PointsToDips(size);
        }

        if (character.Bold != style.Bold)
        {
            run.FontWeight = character.Bold ? FontWeights.Bold : FontWeights.Normal;
        }

        if (character.Italic != style.Italic)
        {
            run.FontStyle = character.Italic ? FontStyles.Italic : FontStyles.Normal;
        }

        if (character.Underline != UnderlineKind.None || character.Strike)
        {
            var decorations = new TextDecorationCollection();

            if (character.Underline != UnderlineKind.None)
            {
                foreach (var decoration in Editor.FormatCommands.UnderlineDecorations(character.Underline))
                {
                    decorations.Add(decoration);
                }
            }

            if (character.Strike)
            {
                decorations.Add(TextDecorations.Strikethrough[0]);
            }

            decorations.Freeze();
            run.TextDecorations = decorations;
        }

        // WPF's decorations cannot say which of the five kinds this is, so the
        // kind is recorded separately (Section 6.4).
        if (character.Underline != UnderlineKind.None)
        {
            SpProps.SetUnderline(run, character.Underline);
        }

        if (character.Vertical != VAlign.Normal)
        {
            run.BaselineAlignment = character.Vertical == VAlign.Super
                ? BaselineAlignment.Superscript
                : BaselineAlignment.Subscript;

            // The size before shrinking, so the walker can report the true
            // size rather than the shrunken one (Section 6.8).
            SpProps.SetBaseSizePt(run, baseSize);
            SpProps.SetVAlign(run, character.Vertical);

            run.FontSize = Units.PointsToDips(baseSize) * Editor.FormatCommands.VerticalShrink;
        }

        if (character.SmallCaps)
        {
            Typography.SetCapitals(run, FontCapitals.SmallCaps);
        }

        if (ParseColor(character.Color) is { } foreground)
        {
            run.Foreground = foreground;
        }

        if (ParseColor(character.Highlight) is { } background)
        {
            run.Background = background;
        }

        // Facts no WPF property can hold, kept only so a save does not lose
        // them (Section 6.8).
        if (character.AllCapsPreserved || character.LetterSpacingPtPreserved is not null)
        {
            SpProps.SetPreserved(
                run,
                new PreservedFormatting
                {
                    AllCaps = character.AllCapsPreserved,
                    LetterSpacingPt = character.LetterSpacingPtPreserved,
                });
        }

        return run;
    }

    private static Hyperlink BuildHyperlink(Run run, string link)
    {
        var hyperlink = new Hyperlink(run);

        try
        {
            hyperlink.NavigateUri = new Uri(link, UriKind.RelativeOrAbsolute);
        }
        catch (UriFormatException)
        {
            // A link a file claimed but Uri will not parse. The text keeps the
            // link in SpProps so saving does not lose it; it just is not
            // clickable.
            Log.Debug($"a hyperlink target could not be parsed: {link}");
        }

        SpProps.SetLink(hyperlink, link);

        return hyperlink;
    }

    /// <summary>
    /// An image, with its original bytes kept so a save writes them back
    /// untouched (Section 6.4).
    /// </summary>
    /// <remarks>
    /// Re-encoding an image on every save would lose quality a little at a
    /// time, and a PNG screenshot saved five times should be the same PNG. So
    /// the bytes and the MIME type travel on the Image's Tag and the walker
    /// reads them straight back.
    /// </remarks>
    private static InlineUIContainer? BuildImage(ImageRun image)
    {
        try
        {
            var bitmap = new BitmapImage();

            bitmap.BeginInit();
            bitmap.StreamSource = new MemoryStream(image.Data, writable: false);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();

            var element = new Image
            {
                Source = bitmap,
                Width = Units.PointsToDips(image.WidthPt),
                Height = Units.PointsToDips(image.HeightPt),
                Stretch = Stretch.Fill,
                Tag = new SpProps.ImagePayload(image.Data, image.Mime, image.Alt),
            };

            if (!string.IsNullOrEmpty(image.Alt))
            {
                element.ToolTip = image.Alt;
            }

            return new InlineUIContainer(element);
        }
        catch (Exception ex) when (ex is NotSupportedException or ArgumentException or IOException)
        {
            // An image format WPF cannot decode. Dropping it loses the picture
            // but keeps the document openable, and the warning list says so.
            Log.Warn($"an image could not be decoded ({image.Mime}, {image.Data.Length} bytes): {ex.Message}");
            return null;
        }
    }

    private static bool Same(string? a, string? b) =>
        string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>A frozen brush from an IR colour string, or null.</summary>
    internal static SolidColorBrush? ParseColor(string? color)
    {
        if (string.IsNullOrWhiteSpace(color))
        {
            return null;
        }

        try
        {
            var converted = ColorConverter.ConvertFromString(color);

            return converted is Color value ? Frozen(value) : null;
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or NotSupportedException)
        {
            Log.Debug($"a colour could not be parsed: {color}");
            return null;
        }
    }

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();

        return brush;
    }
}
