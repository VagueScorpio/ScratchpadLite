using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;
using ScratchpadLite.Core.Model;

namespace ScratchpadLite.Core.Formats.Odt;

/// <summary>
/// Flattens an ODT's styles, resolving <c>style:parent-style-name</c>
/// inheritance (Section 7.2).
/// </summary>
/// <remarks>
/// Both parts' styles go in one table: the named styles from
/// <c>styles.xml</c>, which carry the document's design, and the automatic
/// styles from <c>content.xml</c>, which carry every piece of direct
/// formatting. A document refers to them by the same kind of name and does not
/// say which part a name came from, so keeping them apart would mean looking
/// in two places for every lookup and getting it wrong when a file reuses a
/// name.
/// </remarks>
internal sealed class StyleTable
{
    private readonly Dictionary<string, XElement> _definitions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Resolved> _resolved = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ListKind> _listKinds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _listFormats = new(StringComparer.Ordinal);
    private readonly StyleSheet _sheet;

    private StyleTable(StyleSheet sheet) => _sheet = sheet;

    /// <summary>What a style resolves to, after inheritance.</summary>
    internal sealed record Resolved
    {
        internal string AppStyleName { get; init; } = StyleSheet.NormalStyleName;

        internal string? Family { get; init; }

        internal double? SizePt { get; init; }

        internal bool? Bold { get; init; }

        internal bool? Italic { get; init; }

        internal UnderlineKind? Underline { get; init; }

        internal bool? Strike { get; init; }

        internal VAlign? Vertical { get; init; }

        internal bool? SmallCaps { get; init; }

        internal bool? AllCaps { get; init; }

        internal double? LetterSpacingPt { get; init; }

        internal string? Color { get; init; }

        internal string? Highlight { get; init; }

        internal Align Align { get; init; }

        internal double? IndentLeftPt { get; init; }

        internal double? IndentRightPt { get; init; }

        internal double? FirstLinePt { get; init; }

        internal double? SpaceBeforePt { get; init; }

        internal double? SpaceAfterPt { get; init; }

        internal double? LineSpacing { get; init; }

        internal bool PageBreakBefore { get; init; }

        /// <summary>True when the only border is at the bottom - a rule.</summary>
        internal bool BorderBottomOnly { get; init; }

        internal string? CellShading { get; init; }

        /// <summary>Whether the cell draws a border on any side.</summary>
        internal bool CellBordered { get; init; }

        internal double? ColumnWidthPt { get; init; }

        /// <summary>
        /// This style's character formatting as IR, with anything matching the
        /// app's own paragraph style left out.
        /// </summary>
        internal CharStyle Character(ParagraphStyle sheetStyle, StyleSheet sheet) =>
            ApplyTo(new CharStyle(), sheetStyle, sheet);

        /// <summary>
        /// This style's character formatting applied over an inherited set.
        /// </summary>
        internal CharStyle ApplyTo(CharStyle inherited, ParagraphStyle sheetStyle, StyleSheet sheet)
        {
            var family = Family ?? inherited.Family;
            var size = SizePt ?? inherited.SizePt;

            return inherited with
            {
                // Only what differs from the app's own style becomes direct
                // formatting - the same rule the bridge and the .docx reader
                // follow.
                Family = Same(family, sheet.FamilyFor(sheetStyle)) ? null : family,
                SizePt = size is { } actual && Math.Abs(actual - sheetStyle.SizePt) < 0.01 ? null : size,

                // Bold and italic have to be subtracted the same way, and this
                // is easy to miss because they are not nullable in the IR. An
                // ODT style chain is flattened before it gets here, so a
                // paragraph of Heading 3 arrives already bold: keeping that
                // would turn the style's own weight into direct formatting on
                // every run, and the next save would write it out as both.
                //
                // The cost is that a run deliberately set to normal weight
                // inside a bold style cannot be told from one that simply
                // inherits, so it comes back bold. The IR has no third value
                // to say otherwise, and .docx has the same limitation.
                Bold = (Bold ?? inherited.Bold) && !sheetStyle.Bold,
                Italic = (Italic ?? inherited.Italic) && !sheetStyle.Italic,
                Underline = Underline ?? inherited.Underline,
                Strike = Strike ?? inherited.Strike,
                Vertical = Vertical ?? inherited.Vertical,
                SmallCaps = SmallCaps ?? inherited.SmallCaps,
                AllCapsPreserved = AllCaps ?? inherited.AllCapsPreserved,
                LetterSpacingPtPreserved = LetterSpacingPt ?? inherited.LetterSpacingPtPreserved,
                Color = SameColor(Color ?? inherited.Color, sheetStyle.Color)
                    ? null
                    : Color ?? inherited.Color,
                Highlight = SameColor(Highlight ?? inherited.Highlight, sheetStyle.Shading)
                    ? null
                    : Highlight ?? inherited.Highlight,
            };
        }

        private static bool Same(string? a, string? b) =>
            string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);

        private static bool SameColor(string? value, string? styleColor)
        {
            if (value is null)
            {
                return true;
            }

            var left = IrNormalizer.NormalizeColor(value);
            var right = IrNormalizer.NormalizeColor(styleColor);

            // A style with no colour renders black, so black text in such a
            // paragraph is not direct formatting.
            return left == (right ?? (styleColor is null ? "#000000" : null));
        }
    }

    internal static StyleTable From(ZipArchive zip, StyleSheet sheet)
    {
        var table = new StyleTable(sheet);

        foreach (var part in new[] { "styles.xml", "content.xml" })
        {
            var document = Load(zip, part);

            if (document?.Root is null)
            {
                continue;
            }

            foreach (var container in document.Root.Elements())
            {
                if (container.Name != OdtXml.Office + "styles"
                    && container.Name != OdtXml.Office + "automatic-styles")
                {
                    continue;
                }

                foreach (var style in container.Elements(OdtXml.Style + "style"))
                {
                    if (style.Attribute(OdtXml.Style + "name")?.Value is { } name)
                    {
                        table._definitions[name] = style;
                    }
                }

                foreach (var list in container.Elements(OdtXml.Text + "list-style"))
                {
                    if (list.Attribute(OdtXml.Style + "name")?.Value is { } name)
                    {
                        var numbered = list.Elements()
                            .FirstOrDefault(e => e.Name == OdtXml.Text + "list-level-style-number");

                        table._listKinds[name] = numbered is null ? ListKind.Bullet : ListKind.Number;

                        if (numbered is not null)
                        {
                            // The format comes from the first numbered level.
                            // ODT allows a different one per level and the IR
                            // does not, so the shallowest wins.
                            table._listFormats[name] = OdtStyleMap.NumberFormatFrom(
                                numbered.Attribute(OdtXml.Style + "num-format")?.Value);
                        }
                    }
                }
            }
        }

        return table;
    }

    private static XDocument? Load(ZipArchive zip, string path)
    {
        var entry = zip.GetEntry(path);

        if (entry is null)
        {
            return null;
        }

        using var stream = entry.Open();
        using var reader = XmlReader.Create(stream, Limits.SafeXml());

        return XDocument.Load(reader);
    }

    /// <summary>What a style name resolves to, following its parents.</summary>
    internal Resolved Resolve(string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return new Resolved();
        }

        if (_resolved.TryGetValue(name, out var cached))
        {
            return cached;
        }

        var result = Flatten(name, depth: 0);
        _resolved[name] = result;

        return result;
    }

    private Resolved Flatten(string name, int depth)
    {
        // A parent chain that loops is malformed but does happen.
        if (depth > 16 || !_definitions.TryGetValue(name, out var style))
        {
            return new Resolved();
        }

        var parentName = style.Attribute(OdtXml.Style + "parent-style-name")?.Value;

        var inherited = parentName is { Length: > 0 }
                        && !string.Equals(parentName, name, StringComparison.Ordinal)
            ? Flatten(parentName, depth + 1)
            : new Resolved();

        var result = Apply(inherited, style);

        // The app's style comes from this style's own name where it is one of
        // ours, otherwise from whatever it inherits.
        var mapped = OdtStyleMap.AppStyleFor(name, _sheet)
                     ?? (parentName is null ? null : OdtStyleMap.AppStyleFor(parentName, _sheet));

        return result with { AppStyleName = mapped ?? inherited.AppStyleName };
    }

    private static Resolved Apply(Resolved inherited, XElement style)
    {
        var result = inherited;

        if (style.Element(OdtXml.Style + "text-properties") is { } text)
        {
            result = ApplyText(result, text);
        }

        if (style.Element(OdtXml.Style + "paragraph-properties") is { } paragraph)
        {
            result = ApplyParagraph(result, paragraph);
        }

        if (style.Element(OdtXml.Style + "table-cell-properties") is { } cell)
        {
            var border = cell.Attribute(OdtXml.Fo + "border")?.Value;

            result = result with
            {
                CellShading = OdtXml.FromColor(cell.Attribute(OdtXml.Fo + "background-color")?.Value)
                              ?? result.CellShading,

                // "none" and a zero width both mean no border; anything else
                // is one. A file from another program may say it four times,
                // once per side, and any one of them showing is enough.
                CellBordered = Bordered(border)
                               || Bordered(cell.Attribute(OdtXml.Fo + "border-left")?.Value)
                               || Bordered(cell.Attribute(OdtXml.Fo + "border-top")?.Value)
                               || result.CellBordered,
            };
        }

        if (style.Element(OdtXml.Style + "table-column-properties") is { } column)
        {
            result = result with
            {
                ColumnWidthPt = OdtXml.ToPoints(column.Attribute(OdtXml.Style + "column-width")?.Value)
                                ?? result.ColumnWidthPt,
            };
        }

        return result;
    }

    private static Resolved ApplyText(Resolved result, XElement text)
    {
        if (text.Attribute(OdtXml.Style + "font-name")?.Value is { Length: > 0 } fontName)
        {
            result = result with { Family = fontName };
        }
        else if (text.Attribute(OdtXml.Fo + "font-family")?.Value is { Length: > 0 } family)
        {
            result = result with { Family = family.Trim('\'', '"') };
        }

        if (OdtXml.ToPoints(text.Attribute(OdtXml.Fo + "font-size")?.Value) is { } size)
        {
            result = result with { SizePt = size };
        }

        if (text.Attribute(OdtXml.Fo + "font-weight")?.Value is { } weight)
        {
            result = result with { Bold = IsBold(weight) };
        }

        if (text.Attribute(OdtXml.Fo + "font-style")?.Value is { } fontStyle)
        {
            result = result with
            {
                Italic = fontStyle is "italic" or "oblique",
            };
        }

        if (text.Attribute(OdtXml.Style + "text-underline-style")?.Value is { } underline)
        {
            var kind = UnderlineFor(
                underline,
                text.Attribute(OdtXml.Style + "text-underline-type")?.Value);

            result = result with { Underline = kind };
        }

        if (text.Attribute(OdtXml.Style + "text-line-through-style")?.Value is { } strike)
        {
            result = result with { Strike = !strike.Equals("none", StringComparison.OrdinalIgnoreCase) };
        }

        if (text.Attribute(OdtXml.Style + "text-position")?.Value is { } position)
        {
            result = result with { Vertical = VerticalFor(position) };
        }

        if (text.Attribute(OdtXml.Fo + "font-variant")?.Value is { } variant)
        {
            result = result with
            {
                SmallCaps = variant.Equals("small-caps", StringComparison.OrdinalIgnoreCase),
            };
        }

        if (text.Attribute(OdtXml.Fo + "text-transform")?.Value is { } transform)
        {
            // Not displayed - WPF has no all-caps - but kept so a save does
            // not lose it (Section 6.8).
            result = result with
            {
                AllCaps = transform.Equals("uppercase", StringComparison.OrdinalIgnoreCase),
            };
        }

        if (text.Attribute(OdtXml.Fo + "letter-spacing")?.Value is { } spacing
            && OdtXml.ToPoints(spacing) is { } tracking)
        {
            result = result with { LetterSpacingPt = tracking };
        }

        if (OdtXml.FromColor(text.Attribute(OdtXml.Fo + "color")?.Value) is { } color)
        {
            result = result with { Color = color };
        }

        if (OdtXml.FromColor(text.Attribute(OdtXml.Fo + "background-color")?.Value) is { } highlight)
        {
            result = result with { Highlight = highlight };
        }

        return result;
    }

    private static Resolved ApplyParagraph(Resolved result, XElement paragraph)
    {
        if (paragraph.Attribute(OdtXml.Fo + "text-align")?.Value is { } align)
        {
            result = result with
            {
                Align = align switch
                {
                    "center" => Align.Center,

                    // "end" and "right" both appear; "end" is the correct one
                    // and "right" is what older tools wrote.
                    "end" or "right" => Align.Right,
                    "justify" => Align.Justify,
                    _ => Align.Left,
                },
            };
        }

        if (OdtXml.ToPoints(paragraph.Attribute(OdtXml.Fo + "margin-left")?.Value) is { } left)
        {
            result = result with { IndentLeftPt = left };
        }

        if (OdtXml.ToPoints(paragraph.Attribute(OdtXml.Fo + "margin-right")?.Value) is { } right)
        {
            result = result with { IndentRightPt = right };
        }

        if (OdtXml.ToPoints(paragraph.Attribute(OdtXml.Fo + "text-indent")?.Value) is { } indent)
        {
            result = result with { FirstLinePt = indent };
        }

        if (OdtXml.ToPoints(paragraph.Attribute(OdtXml.Fo + "margin-top")?.Value) is { } before)
        {
            result = result with { SpaceBeforePt = before };
        }

        if (OdtXml.ToPoints(paragraph.Attribute(OdtXml.Fo + "margin-bottom")?.Value) is { } after)
        {
            result = result with { SpaceAfterPt = after };
        }

        if (paragraph.Attribute(OdtXml.Fo + "line-height")?.Value is { } height)
        {
            result = result with { LineSpacing = LineSpacingFor(height) };
        }

        if (paragraph.Attribute(OdtXml.Fo + "break-before")?.Value is { } breakBefore)
        {
            result = result with
            {
                PageBreakBefore = breakBefore.Equals("page", StringComparison.OrdinalIgnoreCase),
            };
        }

        var bottom = paragraph.Attribute(OdtXml.Fo + "border-bottom")?.Value;
        var all = paragraph.Attribute(OdtXml.Fo + "border")?.Value;

        result = result with
        {
            BorderBottomOnly = bottom is { Length: > 0 }
                               && !bottom.Equals("none", StringComparison.OrdinalIgnoreCase)
                               && all is null
                               && paragraph.Attribute(OdtXml.Fo + "border-top") is null,
        };

        return result;
    }

    private static bool Bordered(string? border) =>
        border is { Length: > 0 }
        && !border.Equals("none", StringComparison.OrdinalIgnoreCase)
        && !border.StartsWith("0 ", StringComparison.Ordinal)
        && !border.StartsWith("0pt", StringComparison.OrdinalIgnoreCase)
        && !border.StartsWith("0in", StringComparison.OrdinalIgnoreCase);

    private static bool IsBold(string weight)
    {
        if (weight.Equals("bold", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // ODT also allows a numeric weight, the way CSS does.
        return int.TryParse(weight, out var value) && value >= 600;
    }

    private static UnderlineKind UnderlineFor(string style, string? type)
    {
        if (style.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            return UnderlineKind.None;
        }

        if (type is { } actual && actual.Equals("double", StringComparison.OrdinalIgnoreCase))
        {
            return UnderlineKind.Double;
        }

        return style.ToLowerInvariant() switch
        {
            "dotted" or "dotted-dash" or "dot-dash" or "dot-dot-dash" => UnderlineKind.Dotted,
            "dash" or "long-dash" => UnderlineKind.Dashed,
            "wave" => UnderlineKind.Wavy,
            _ => UnderlineKind.Single,
        };
    }

    private static VAlign VerticalFor(string position)
    {
        var text = position.Trim().ToLowerInvariant();

        if (text.StartsWith("super", StringComparison.Ordinal))
        {
            return VAlign.Super;
        }

        if (text.StartsWith("sub", StringComparison.Ordinal))
        {
            return VAlign.Sub;
        }

        // ODT also allows a signed percentage: positive is raised, negative
        // lowered.
        var number = text.Split(' ')[0].TrimEnd('%');

        if (double.TryParse(
                number,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out var value))
        {
            if (value > 5)
            {
                return VAlign.Super;
            }

            if (value < -5)
            {
                return VAlign.Sub;
            }
        }

        return VAlign.Normal;
    }

    private static double? LineSpacingFor(string height)
    {
        var text = height.Trim();

        if (text.EndsWith('%')
            && double.TryParse(
                text[..^1],
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out var percent))
        {
            return percent / 100;
        }

        // A fixed line height in a length unit has no IR equivalent, since the
        // IR stores a multiple.
        return null;
    }

    /// <summary>Whether a list style is numbered or bulleted.</summary>
    internal ListKind ListKindFor(string? styleName) =>
        styleName is { } name && _listKinds.TryGetValue(name, out var kind) ? kind : ListKind.Bullet;

    /// <summary>A numbered list's format, as the IR names it.</summary>
    internal string NumberFormatFor(string? styleName) =>
        styleName is { } name && _listFormats.TryGetValue(name, out var format) ? format : "decimal";

    /// <summary>A table cell's shading, from its style.</summary>
    internal string? CellShadingFor(string? styleName) => Resolve(styleName).CellShading;

    /// <summary>Whether a cell style draws a border.</summary>
    internal bool CellHasBorder(string? styleName) => Resolve(styleName).CellBordered;

    /// <summary>A table column's width in points, from its style.</summary>
    internal double? ColumnWidthFor(string? styleName) => Resolve(styleName).ColumnWidthPt;
}
