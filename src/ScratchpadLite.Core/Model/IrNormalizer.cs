using System.Globalization;
using System.Text;

namespace ScratchpadLite.Core.Model;

/// <summary>
/// Puts a document into a canonical form, so two documents that mean the same
/// thing compare equal (Section 6.3).
/// </summary>
/// <remarks>
/// The round-trip test in Section 6.4 - <c>Walk(Build(d))</c> equals <c>d</c> -
/// cannot be an exact comparison, and it is worth being clear about why,
/// because the temptation is to call the differences bugs.
/// <list type="bullet">
///   <item>
///     Two adjacent runs with identical formatting are one run as far as a
///     reader is concerned, and WPF will have merged them or not depending on
///     how they were created.
///   </item>
///   <item>
///     An empty run is not visible. A document with one and a document without
///     are the same document.
///   </item>
///   <item>
///     <c>#ff0000</c> and <c>#FF0000</c> are the same colour, and a format
///     reader may produce either.
///   </item>
///   <item>
///     Sizes make the trip through DIPs, which is a multiply and a divide by
///     non-round numbers: 10.5 pt comes back as 10.499999999999998.
///   </item>
/// </list>
/// So the rule is that normalising is allowed to discard anything a reader
/// could not perceive, and nothing else. Every other difference is a bug, and
/// the test is there to find it.
/// </remarks>
public static class IrNormalizer
{
    /// <summary>
    /// Points are compared to this many decimal places - enough to tell 10 pt
    /// from 10.5 pt, loose enough to survive the conversion to DIPs and back.
    /// </summary>
    private const int PointDigits = 3;

    public static SpDocument Normalize(SpDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        return new SpDocument(
            NormalizeBlocks(document.Blocks),
            new SortedDictionary<string, string>(
                document.Meta.ToDictionary(p => p.Key, p => p.Value), StringComparer.Ordinal),
            Normalize(document.Page),
            new SortedSet<string>(document.FontsUsed, StringComparer.Ordinal));
    }

    public static PageSetup Normalize(PageSetup page)
    {
        ArgumentNullException.ThrowIfNull(page);

        return page with
        {
            MarginTopPt = Round(page.MarginTopPt),
            MarginBottomPt = Round(page.MarginBottomPt),
            MarginLeftPt = Round(page.MarginLeftPt),
            MarginRightPt = Round(page.MarginRightPt),
        };
    }

    public static IReadOnlyList<Block> NormalizeBlocks(IReadOnlyList<Block> blocks)
    {
        ArgumentNullException.ThrowIfNull(blocks);

        var result = new List<Block>(blocks.Count);

        foreach (var block in blocks)
        {
            result.Add(block switch
            {
                Paragraph paragraph => Normalize(paragraph),
                Table table => Normalize(table),
                _ => block,
            });
        }

        return result;
    }

    public static Paragraph Normalize(Paragraph paragraph)
    {
        ArgumentNullException.ThrowIfNull(paragraph);

        return paragraph with
        {
            StyleName = string.IsNullOrWhiteSpace(paragraph.StyleName) ? "Normal" : paragraph.StyleName,
            IndentLeftPt = Round(paragraph.IndentLeftPt),
            IndentRightPt = Round(paragraph.IndentRightPt),
            FirstLinePt = Round(paragraph.FirstLinePt),
            SpaceBeforePt = Round(paragraph.SpaceBeforePt),
            SpaceAfterPt = Round(paragraph.SpaceAfterPt),
            LineSpacing = Round(paragraph.LineSpacing),
            Inlines = NormalizeInlines(paragraph.Inlines),
        };
    }

    public static Table Normalize(Table table)
    {
        ArgumentNullException.ThrowIfNull(table);

        return table with
        {
            Rows = table.Rows
                .Select(row => (IReadOnlyList<Cell>)row
                    .Select(cell => cell with
                    {
                        Blocks = NormalizeBlocks(cell.Blocks),
                        Shading = NormalizeColor(cell.Shading),
                    })
                    .ToList())
                .ToList(),
            ColWidthsPt = table.ColWidthsPt?.Select(Round).ToList(),
        };
    }

    /// <summary>
    /// Merges adjacent runs that share a style, drops empty ones, and
    /// canonicalises what is left.
    /// </summary>
    public static IReadOnlyList<Inline> NormalizeInlines(IReadOnlyList<Inline> inlines)
    {
        ArgumentNullException.ThrowIfNull(inlines);

        var result = new List<Inline>(inlines.Count);

        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case TextRun run:
                {
                    if (run.Text.Length == 0)
                    {
                        continue;
                    }

                    var normalized = run with { Style = Normalize(run.Style) };

                    if (result.Count > 0
                        && result[^1] is TextRun previous
                        && previous.Style == normalized.Style)
                    {
                        result[^1] = previous with { Text = previous.Text + normalized.Text };
                        continue;
                    }

                    result.Add(normalized);
                    break;
                }

                case ImageRun image:
                    result.Add(image with
                    {
                        WidthPt = Round(image.WidthPt),
                        HeightPt = Round(image.HeightPt),
                    });
                    break;

                default:
                    result.Add(inline);
                    break;
            }
        }

        // A paragraph with nothing in it and a paragraph with one empty run are
        // the same paragraph, and both end up here as an empty list.
        return result;
    }

    public static CharStyle Normalize(CharStyle style)
    {
        ArgumentNullException.ThrowIfNull(style);

        return style with
        {
            Family = string.IsNullOrWhiteSpace(style.Family) ? null : style.Family.Trim(),
            SizePt = Round(style.SizePt),
            Color = NormalizeColor(style.Color),
            Highlight = NormalizeColor(style.Highlight),
            Link = NormalizeLink(style.Link),
            LetterSpacingPtPreserved = Round(style.LetterSpacingPtPreserved),
        };
    }

    /// <summary>
    /// A link in canonical form, or null.
    /// </summary>
    /// <remarks>
    /// An absolute URL is put through <see cref="Uri"/>, which is what decides
    /// URL equality: <c>https://example.com</c> and
    /// <c>https://example.com/</c> are the same address, and a file format
    /// that stores links as URIs will give back whichever form it prefers.
    /// Anything that is not an absolute URL - a relative path, a bookmark, or
    /// something a file simply claimed - is left exactly as it was.
    /// </remarks>
    public static string? NormalizeLink(string? link)
    {
        if (string.IsNullOrWhiteSpace(link))
        {
            return null;
        }

        var text = link.Trim();

        return Uri.TryCreate(text, UriKind.Absolute, out var uri) ? uri.ToString() : text;
    }

    /// <summary>
    /// A colour as <c>#RRGGBB</c> in upper case, or null.
    /// </summary>
    /// <remarks>
    /// Also drops the alpha channel a WPF brush always has. The IR has no
    /// transparency, so an opaque <c>#FFRRGGBB</c> from WPF and the
    /// <c>#RRGGBB</c> it came from are the same colour.
    /// </remarks>
    public static string? NormalizeColor(string? color)
    {
        if (string.IsNullOrWhiteSpace(color))
        {
            return null;
        }

        var text = color.Trim();

        if (text[0] != '#')
        {
            return text;        // a named colour; left as the reader gave it
        }

        var digits = text[1..];

        if (digits.Length == 8)
        {
            digits = digits[2..];
        }
        else if (digits.Length == 3)
        {
            // #abc means #aabbcc.
            var expanded = new StringBuilder(6);

            foreach (var c in digits)
            {
                expanded.Append(c).Append(c);
            }

            digits = expanded.ToString();
        }

        return digits.Length == 6
            ? "#" + digits.ToUpperInvariant()
            : text;
    }

    private static double Round(double value) => Math.Round(value, PointDigits);

    private static double? Round(double? value) => value is null ? null : Math.Round(value.Value, PointDigits);

    /// <summary>
    /// A document as text, for a test failure message that says what differs.
    /// </summary>
    /// <remarks>
    /// Without this, a round-trip failure reads "SpDocument != SpDocument" and
    /// tells you nothing. With it, the two dumps can be compared line by line.
    /// </remarks>
    public static string Describe(SpDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var text = new StringBuilder();

        text.Append("page: ").Append(document.Page).Append('\n');

        if (document.Meta.Count > 0)
        {
            text.Append("meta: ")
                .Append(string.Join(", ", document.Meta.OrderBy(p => p.Key, StringComparer.Ordinal)
                    .Select(p => $"{p.Key}={p.Value}")))
                .Append('\n');
        }

        if (document.FontsUsed.Count > 0)
        {
            text.Append("fonts: ")
                .Append(string.Join(", ", document.FontsUsed.Order(StringComparer.Ordinal)))
                .Append('\n');
        }

        Describe(text, document.Blocks, 0);

        return text.ToString();
    }

    private static void Describe(StringBuilder text, IReadOnlyList<Block> blocks, int depth)
    {
        var indent = new string(' ', depth * 2);

        foreach (var block in blocks)
        {
            switch (block)
            {
                case Paragraph paragraph:
                    text.Append(indent).Append("p [").Append(paragraph.StyleName).Append(']');

                    if (paragraph.Align != Align.Left)
                    {
                        text.Append(" align=").Append(paragraph.Align);
                    }

                    if (paragraph.List is { } list)
                    {
                        text.Append(" list=").Append(list.Kind).Append(':').Append(list.Level);

                        if (list.NumberFormat != "decimal")
                        {
                            text.Append('/').Append(list.NumberFormat);
                        }

                        if (list.Start != 1)
                        {
                            text.Append(" start=").Append(list.Start);
                        }
                    }

                    Append(text, " indentL=", paragraph.IndentLeftPt);
                    Append(text, " indentR=", paragraph.IndentRightPt);
                    Append(text, " first=", paragraph.FirstLinePt);
                    Append(text, " before=", paragraph.SpaceBeforePt);
                    Append(text, " after=", paragraph.SpaceAfterPt);
                    Append(text, " spacing=", paragraph.LineSpacing);

                    if (paragraph.PageBreakBefore)
                    {
                        text.Append(" pagebreak");
                    }

                    text.Append('\n');

                    foreach (var inline in paragraph.Inlines)
                    {
                        text.Append(indent).Append("  ").Append(Describe(inline)).Append('\n');
                    }

                    break;

                case Table table:
                    text.Append(indent).Append("table ").Append(table.Rows.Count).Append(" rows");

                    if (table.ColWidthsPt is { } widths)
                    {
                        text.Append(" widths=[")
                            .Append(string.Join(",", widths.Select(w => w.ToString("0.###", CultureInfo.InvariantCulture))))
                            .Append(']');
                    }

                    if (!table.Borders)
                    {
                        text.Append(" noborders");
                    }

                    text.Append('\n');

                    for (var r = 0; r < table.Rows.Count; r++)
                    {
                        for (var c = 0; c < table.Rows[r].Count; c++)
                        {
                            var cell = table.Rows[r][c];
                            text.Append(indent).Append("  cell ").Append(r).Append(',').Append(c);

                            if (cell.ColSpan != 1)
                            {
                                text.Append(" colspan=").Append(cell.ColSpan);
                            }

                            if (cell.RowSpan != 1)
                            {
                                text.Append(" rowspan=").Append(cell.RowSpan);
                            }

                            if (cell.Shading is { } shading)
                            {
                                text.Append(" shading=").Append(shading);
                            }

                            text.Append('\n');
                            Describe(text, cell.Blocks, depth + 2);
                        }
                    }

                    break;

                case HorizontalRule:
                    text.Append(indent).Append("hr\n");
                    break;
            }
        }
    }

    private static string Describe(Inline inline) => inline switch
    {
        TextRun run => $"run {Describe(run.Style)} {Quote(run.Text)}",
        ImageRun image =>
            $"image {image.Mime} {image.WidthPt:0.###}x{image.HeightPt:0.###}pt "
            + $"{image.Data.Length} bytes alt={Quote(image.Alt)}",
        LineBreak => "break",
        _ => inline.ToString() ?? "?",
    };

    private static string Describe(CharStyle style)
    {
        var parts = new List<string>(8);

        if (style.Family is { } family)
        {
            parts.Add(family);
        }

        if (style.SizePt is { } size)
        {
            parts.Add(size.ToString("0.###", CultureInfo.InvariantCulture) + "pt");
        }

        if (style.Bold)
        {
            parts.Add("bold");
        }

        if (style.Italic)
        {
            parts.Add("italic");
        }

        if (style.Underline != UnderlineKind.None)
        {
            parts.Add("u:" + style.Underline);
        }

        if (style.Strike)
        {
            parts.Add("strike");
        }

        if (style.Vertical != VAlign.Normal)
        {
            parts.Add(style.Vertical.ToString().ToLowerInvariant());
        }

        if (style.SmallCaps)
        {
            parts.Add("smallcaps");
        }

        if (style.AllCapsPreserved)
        {
            parts.Add("allcaps!");
        }

        if (style.LetterSpacingPtPreserved is { } spacing)
        {
            parts.Add($"tracking!{spacing.ToString("0.###", CultureInfo.InvariantCulture)}");
        }

        if (style.Color is { } color)
        {
            parts.Add(color);
        }

        if (style.Highlight is { } highlight)
        {
            parts.Add("bg" + highlight);
        }

        if (style.Link is { } link)
        {
            parts.Add("link=" + link);
        }

        return "{" + string.Join(" ", parts) + "}";
    }

    private static string Quote(string text) =>
        "\"" + text.Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace("\t", "\\t", StringComparison.Ordinal) + "\"";

    private static void Append(StringBuilder text, string label, double value)
    {
        if (value != 0)
        {
            text.Append(label).Append(value.ToString("0.###", CultureInfo.InvariantCulture));
        }
    }

    private static void Append(StringBuilder text, string label, double? value)
    {
        if (value is { } actual)
        {
            text.Append(label).Append(actual.ToString("0.###", CultureInfo.InvariantCulture));
        }
    }
}
