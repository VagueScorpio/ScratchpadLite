using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using ScratchpadLite.Core.Model;
using IrParagraph = ScratchpadLite.Core.Model.Paragraph;
using IrTable = ScratchpadLite.Core.Model.Table;

namespace ScratchpadLite.Core.Formats.Odt;

/// <summary>
/// Writes the IR as an OpenDocument text file, by hand (Section 7.2).
/// </summary>
/// <remarks>
/// An ODT is a zip of XML parts. Two things about it are easy to get wrong and
/// fatal when you do.
/// <list type="number">
///   <item>
///     <c>mimetype</c> must be the <b>first</b> entry in the zip and must be
///     <b>stored uncompressed</b>. That is how the format is sniffed without
///     unpacking it, and a file that gets it wrong opens with a repair prompt
///     even though every byte of its XML is correct.
///   </item>
///   <item>
///     Direct formatting goes in <em>automatic styles</em>, not on the
///     elements. ODT has no equivalent of a style attribute: a bold word is a
///     span referring to an automatic style that says bold. So the writer
///     collects the distinct property sets as it goes and emits one style per
///     set, which is also why they are deduplicated - a document with a
///     thousand bold words gets one style, not a thousand.
///   </item>
/// </list>
/// </remarks>
internal static class OdtWriter
{
    internal static void Write(SpDocument doc, Stream output, StyleSheet styles, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(styles);

        var automatic = new AutomaticStyles();
        var images = new List<(string Path, ImageRun Image)>();
        var body = BuildBody(doc, automatic, images, styles, ct);

        using var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);

        // First, and stored: this one is not a style choice (see the remarks).
        var mimetype = zip.CreateEntry("mimetype", CompressionLevel.NoCompression);
        using (var stream = mimetype.Open())
        {
            stream.Write(Encoding.ASCII.GetBytes(OdtXml.MimeType));
        }

        WriteXml(zip, "META-INF/manifest.xml", BuildManifest(images.Select(i => i.Path)));
        WriteXml(zip, "meta.xml", BuildMeta(doc));
        WriteXml(zip, "styles.xml", BuildStyles(styles, doc.Page));
        WriteXml(zip, "content.xml", BuildContent(body, automatic));
        WriteXml(zip, "settings.xml", BuildSettings());

        foreach (var (path, image) in images)
        {
            var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
            using var stream = entry.Open();

            stream.Write(image.Data);
        }
    }

    /// <summary>
    /// Writes one part of the package, with no indentation at all.
    /// </summary>
    /// <remarks>
    /// <c>DisableFormatting</c> is not a size optimisation here, it is a
    /// correctness one. Indenting the XML puts newlines and spaces inside
    /// <c>text:p</c>, and in ODT the text inside a paragraph is content: a
    /// reader folds that run of whitespace into one space rather than throwing
    /// it away, so every indented paragraph gains a leading and a trailing
    /// space. Multiple spaces the document really has go out as
    /// <c>text:s</c> instead, which is exactly why the whitespace that is
    /// there must be meant.
    /// <para>
    /// It cost a round trip of "Hello, world." coming back as
    /// " Hello, world. " to find, which is the kind of thing that looks like a
    /// reader bug for an afternoon.
    /// </para>
    /// </remarks>
    private static void WriteXml(ZipArchive zip, string path, XDocument document)
    {
        var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
        using var stream = entry.Open();

        document.Save(stream, SaveOptions.DisableFormatting);
    }

    /// <summary>
    /// Collects the automatic styles a document needs, one per distinct set of
    /// properties.
    /// </summary>
    private sealed class AutomaticStyles
    {
        private readonly Dictionary<string, string> _textNames = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _paragraphNames = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _cellNames = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _columnNames = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _listNames = new(StringComparer.Ordinal);
        private readonly List<XElement> _elements = [];

        internal IReadOnlyList<XElement> Elements => _elements;

        /// <summary>The name of a text style for this character formatting.</summary>
        internal string? ForText(CharStyle style)
        {
            var properties = TextProperties(style);

            if (properties.Attributes().Count() == 0)
            {
                return null;        // nothing to say, so no span is needed
            }

            var key = properties.ToString(SaveOptions.DisableFormatting);

            if (_textNames.TryGetValue(key, out var existing))
            {
                return existing;
            }

            var name = "T" + (_textNames.Count + 1);
            _textNames[key] = name;

            _elements.Add(new XElement(
                OdtXml.Style + "style",
                new XAttribute(OdtXml.Style + "name", name),
                new XAttribute(OdtXml.Style + "family", "text"),
                properties));

            return name;
        }

        /// <summary>The name of a paragraph style for this paragraph's own formatting.</summary>
        internal string? ForParagraph(IrParagraph paragraph, string parent)
        {
            var properties = ParagraphProperties(paragraph);

            if (properties.Attributes().Count() == 0)
            {
                return null;
            }

            var key = parent + "|" + properties.ToString(SaveOptions.DisableFormatting);

            if (_paragraphNames.TryGetValue(key, out var existing))
            {
                return existing;
            }

            var name = "P" + (_paragraphNames.Count + 1);
            _paragraphNames[key] = name;

            _elements.Add(new XElement(
                OdtXml.Style + "style",
                new XAttribute(OdtXml.Style + "name", name),
                new XAttribute(OdtXml.Style + "family", "paragraph"),
                new XAttribute(OdtXml.Style + "parent-style-name", parent),
                properties));

            return name;
        }

        /// <summary>The name of a table-cell style with this shading.</summary>
        internal string ForCell(string shading)
        {
            var color = OdtXml.Color(shading) ?? "#ffffff";

            if (_cellNames.TryGetValue(color, out var existing))
            {
                return existing;
            }

            var name = "TC" + (_cellNames.Count + 1);
            _cellNames[color] = name;

            _elements.Add(new XElement(
                OdtXml.Style + "style",
                new XAttribute(OdtXml.Style + "name", name),
                new XAttribute(OdtXml.Style + "family", "table-cell"),
                new XElement(
                    OdtXml.Style + "table-cell-properties",
                    new XAttribute(OdtXml.Fo + "background-color", color),
                    new XAttribute(OdtXml.Fo + "padding", "0.05in"),
                    new XAttribute(OdtXml.Fo + "border", "0.5pt solid #808080"))));

            return name;
        }

        /// <summary>
        /// The name of a table-column style of this width, or null when the
        /// table has no width for the column.
        /// </summary>
        /// <remarks>
        /// ODT has no width attribute on <c>table:table-column</c>: a column
        /// width is only ever an automatic style, so a table whose columns have
        /// no styles is a table of equal columns however the IR describes it.
        /// </remarks>
        internal string? ForColumn(double? widthPt)
        {
            if (widthPt is not { } width || width <= 0)
            {
                return null;
            }

            var key = OdtXml.Points(width);

            if (_columnNames.TryGetValue(key, out var existing))
            {
                return existing;
            }

            var name = "TCol" + (_columnNames.Count + 1);
            _columnNames[key] = name;

            _elements.Add(new XElement(
                OdtXml.Style + "style",
                new XAttribute(OdtXml.Style + "name", name),
                new XAttribute(OdtXml.Style + "family", "table-column"),
                new XElement(
                    OdtXml.Style + "table-column-properties",
                    new XAttribute(OdtXml.Style + "column-width", key))));

            return name;
        }

        /// <summary>
        /// The name of a list style with this marker, declared on first use.
        /// </summary>
        /// <remarks>
        /// One style per marker, not one per list: a document with forty
        /// bulleted lists refers to one definition. The numbering format is
        /// part of the marker, so a lettered list and a numbered one are two
        /// styles - which is the only way ODT can say they differ.
        /// </remarks>
        internal string ForList(ListInfo info)
        {
            var key = info.Kind == ListKind.Bullet ? "bullet" : "number:" + info.NumberFormat;

            if (_listNames.TryGetValue(key, out var existing))
            {
                return existing;
            }

            var name = "L" + (_listNames.Count + 1);
            _listNames[key] = name;

            _elements.Add(OdtStyleMap.ListStyle(name, info.Kind, info.NumberFormat));

            return name;
        }
    }

    private static XElement TextProperties(CharStyle style)
    {
        var properties = new XElement(OdtXml.Style + "text-properties");

        if (style.Family is { } family)
        {
            properties.Add(new XAttribute(OdtXml.Style + "font-name", family));
            properties.Add(new XAttribute(OdtXml.Fo + "font-family", family));
        }

        if (style.SizePt is { } size)
        {
            properties.Add(new XAttribute(OdtXml.Fo + "font-size", OdtXml.Points(size)));
        }

        if (style.Bold)
        {
            properties.Add(new XAttribute(OdtXml.Fo + "font-weight", "bold"));
        }

        if (style.Italic)
        {
            properties.Add(new XAttribute(OdtXml.Fo + "font-style", "italic"));
        }

        if (style.Underline != UnderlineKind.None)
        {
            properties.Add(new XAttribute(OdtXml.Style + "text-underline-style", UnderlineStyle(style.Underline)));
            properties.Add(new XAttribute(OdtXml.Style + "text-underline-width", "auto"));
            properties.Add(new XAttribute(OdtXml.Style + "text-underline-color", "font-color"));

            if (style.Underline == UnderlineKind.Double)
            {
                properties.Add(new XAttribute(OdtXml.Style + "text-underline-type", "double"));
            }
        }

        if (style.Strike)
        {
            properties.Add(new XAttribute(OdtXml.Style + "text-line-through-style", "solid"));
        }

        if (style.Vertical != VAlign.Normal)
        {
            // ODT states super- and subscript as a percentage of the line plus
            // a size percentage, which is why the shrink factor appears here.
            properties.Add(new XAttribute(
                OdtXml.Style + "text-position",
                style.Vertical == VAlign.Super ? "super 58%" : "sub 58%"));
        }

        if (style.SmallCaps)
        {
            properties.Add(new XAttribute(OdtXml.Fo + "font-variant", "small-caps"));
        }

        if (style.AllCapsPreserved)
        {
            properties.Add(new XAttribute(OdtXml.Fo + "text-transform", "uppercase"));
        }

        if (style.LetterSpacingPtPreserved is { } tracking)
        {
            properties.Add(new XAttribute(OdtXml.Fo + "letter-spacing", OdtXml.Points(tracking)));
        }

        if (OdtXml.Color(style.Color) is { } color)
        {
            properties.Add(new XAttribute(OdtXml.Fo + "color", color));
        }

        if (OdtXml.Color(style.Highlight) is { } highlight)
        {
            properties.Add(new XAttribute(OdtXml.Fo + "background-color", highlight));
        }

        return properties;
    }

    private static string UnderlineStyle(UnderlineKind kind) => kind switch
    {
        UnderlineKind.Dotted => "dotted",
        UnderlineKind.Dashed => "dash",
        UnderlineKind.Wavy => "wave",
        _ => "solid",
    };

    private static XElement ParagraphProperties(IrParagraph paragraph)
    {
        var properties = new XElement(OdtXml.Style + "paragraph-properties");

        if (paragraph.Align != Align.Left)
        {
            properties.Add(new XAttribute(
                OdtXml.Fo + "text-align",
                paragraph.Align switch
                {
                    Align.Center => "center",
                    Align.Right => "end",
                    _ => "justify",
                }));
        }

        if (paragraph.IndentLeftPt != 0)
        {
            properties.Add(new XAttribute(OdtXml.Fo + "margin-left", OdtXml.Points(paragraph.IndentLeftPt)));
        }

        if (paragraph.IndentRightPt != 0)
        {
            properties.Add(new XAttribute(OdtXml.Fo + "margin-right", OdtXml.Points(paragraph.IndentRightPt)));
        }

        if (paragraph.FirstLinePt != 0)
        {
            properties.Add(new XAttribute(OdtXml.Fo + "text-indent", OdtXml.Points(paragraph.FirstLinePt)));
        }

        if (paragraph.SpaceBeforePt is { } before)
        {
            properties.Add(new XAttribute(OdtXml.Fo + "margin-top", OdtXml.Points(before)));
        }

        if (paragraph.SpaceAfterPt is { } after)
        {
            properties.Add(new XAttribute(OdtXml.Fo + "margin-bottom", OdtXml.Points(after)));
        }

        if (paragraph.LineSpacing is { } spacing)
        {
            properties.Add(new XAttribute(
                OdtXml.Fo + "line-height",
                (spacing * 100).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "%"));
        }

        if (paragraph.PageBreakBefore)
        {
            properties.Add(new XAttribute(OdtXml.Fo + "break-before", "page"));
        }

        return properties;
    }

    private static XElement BuildBody(
        SpDocument doc,
        AutomaticStyles automatic,
        List<(string Path, ImageRun Image)> images,
        StyleSheet styles,
        CancellationToken ct)
    {
        var text = new XElement(OdtXml.Office + "text");

        WriteBlocks(doc.Blocks, text, automatic, images, styles, ct);

        return new XElement(OdtXml.Office + "body", text);
    }

    private static void WriteBlocks(
        IReadOnlyList<Block> blocks,
        XElement parent,
        AutomaticStyles automatic,
        List<(string Path, ImageRun Image)> images,
        StyleSheet styles,
        CancellationToken ct)
    {
        // ODT nests lists the way WPF does, so a run of list paragraphs becomes
        // one structure - the same reshaping the bridge does, in reverse.
        var openLists = new List<(XElement List, ListInfo Info)>();

        foreach (var block in blocks)
        {
            ct.ThrowIfCancellationRequested();

            if (block is IrParagraph { List: not null } listed)
            {
                WriteListItem(listed, parent, openLists, automatic, images, styles, ct);
                continue;
            }

            openLists.Clear();

            switch (block)
            {
                case IrParagraph paragraph:
                    parent.Add(WriteParagraph(paragraph, automatic, images, styles));
                    break;

                case IrTable table:
                    parent.Add(WriteTable(table, automatic, images, styles, ct));
                    break;

                case HorizontalRule:
                    // ODT has no rule element; a paragraph with a bottom border
                    // is how every implementation writes one.
                    parent.Add(new XElement(
                        OdtXml.Text + "p",
                        new XAttribute(OdtXml.Text + "style-name", OdtStyleMap.RuleStyleName)));

                    break;
            }
        }
    }

    private static void WriteListItem(
        IrParagraph paragraph,
        XElement parent,
        List<(XElement List, ListInfo Info)> openLists,
        AutomaticStyles automatic,
        List<(string Path, ImageRun Image)> images,
        StyleSheet styles,
        CancellationToken ct)
    {
        var info = paragraph.List!;
        var level = Math.Clamp(info.Level, 0, 8);

        // Close levels that are deeper than this one, or whose marker differs.
        while (openLists.Count > level + 1
               || (openLists.Count == level + 1 && !SameMarker(openLists[level].Info, info)))
        {
            openLists.RemoveAt(openLists.Count - 1);
        }

        while (openLists.Count <= level)
        {
            var list = new XElement(
                OdtXml.Text + "list",
                new XAttribute(OdtXml.Text + "style-name", automatic.ForList(info)));

            if (openLists.Count == 0)
            {
                parent.Add(list);
            }
            else
            {
                // A nested list lives inside the last item of the level above.
                var items = openLists[^1].List.Elements(OdtXml.Text + "list-item").ToList();

                if (items.Count == 0)
                {
                    var filler = new XElement(OdtXml.Text + "list-item");
                    openLists[^1].List.Add(filler);
                    items.Add(filler);
                }

                items[^1].Add(list);
            }

            openLists.Add((list, info));
        }

        var item = new XElement(
            OdtXml.Text + "list-item",
            WriteParagraph(paragraph with { List = null }, automatic, images, styles));

        // A numbered list that does not start at one says so on its first
        // item. On any later item it would restart the numbering there.
        if (info.Kind == ListKind.Number
            && info.Start != 1
            && !openLists[level].List.Elements(OdtXml.Text + "list-item").Any())
        {
            item.Add(new XAttribute(OdtXml.Text + "start-value", info.Start));
        }

        openLists[level].List.Add(item);

        _ = ct;
    }

    private static bool SameMarker(ListInfo a, ListInfo b) =>
        a.Kind == b.Kind
        && (a.Kind == ListKind.Bullet
            || string.Equals(a.NumberFormat, b.NumberFormat, StringComparison.Ordinal));

    private static XElement WriteParagraph(
        IrParagraph paragraph,
        AutomaticStyles automatic,
        List<(string Path, ImageRun Image)> images,
        StyleSheet styles)
    {
        var style = styles[paragraph.StyleName];
        var parent = OdtStyleMap.StyleNameFor(paragraph.StyleName);

        // A heading is its own element in ODT, with an outline level, which is
        // what makes it a heading in the navigator and the table of contents.
        var element = style.OutlineLevel is { } outline
            ? new XElement(
                OdtXml.Text + "h",
                new XAttribute(OdtXml.Text + "outline-level", outline))
            : new XElement(OdtXml.Text + "p");

        var automaticName = automatic.ForParagraph(paragraph, parent);

        element.Add(new XAttribute(OdtXml.Text + "style-name", automaticName ?? parent));

        foreach (var inline in paragraph.Inlines)
        {
            WriteInline(inline, element, automatic, images);
        }

        return element;
    }

    private static void WriteInline(
        Model.Inline inline,
        XElement parent,
        AutomaticStyles automatic,
        List<(string Path, ImageRun Image)> images)
    {
        switch (inline)
        {
            case TextRun run:
            {
                var content = TextContent(run.Text);
                var styleName = automatic.ForText(run.Style);

                object node = styleName is null
                    ? new XElement(OdtXml.Text + "span", content)
                    : new XElement(
                        OdtXml.Text + "span",
                        new XAttribute(OdtXml.Text + "style-name", styleName),
                        content);

                if (run.Style.Link is { } link)
                {
                    node = new XElement(
                        OdtXml.Text + "a",
                        new XAttribute(OdtXml.Xlink + "href", link),
                        new XAttribute(OdtXml.Xlink + "type", "simple"),
                        node);
                }

                parent.Add(node);
                break;
            }

            case Model.LineBreak:
                parent.Add(new XElement(OdtXml.Text + "line-break"));
                break;

            case ImageRun image:
            {
                var path = $"Pictures/image{images.Count + 1}{Markdown.MarkdownWriter.ExtensionFor(image.Mime)}";
                images.Add((path, image));

                parent.Add(new XElement(
                    OdtXml.Draw + "frame",
                    new XAttribute(OdtXml.Draw + "name", $"Image{images.Count}"),
                    new XAttribute(OdtXml.Text + "anchor-type", "as-char"),
                    new XAttribute(OdtXml.Svg + "width", OdtXml.Points(image.WidthPt)),
                    new XAttribute(OdtXml.Svg + "height", OdtXml.Points(image.HeightPt)),
                    new XElement(
                        OdtXml.Draw + "image",
                        new XAttribute(OdtXml.Xlink + "href", path),
                        new XAttribute(OdtXml.Xlink + "type", "simple"),
                        new XAttribute(OdtXml.Xlink + "show", "embed"),
                        new XAttribute(OdtXml.Xlink + "actuate", "onLoad")),
                    image.Alt.Length == 0
                        ? null
                        : new XElement(OdtXml.Svg + "desc", image.Alt)));

                break;
            }
        }
    }

    /// <summary>
    /// Text, with the characters ODT does not keep in a text node turned into
    /// their own elements.
    /// </summary>
    /// <remarks>
    /// ODT collapses runs of spaces, so two spaces in a row would become one.
    /// <c>text:s</c> carries the extra ones, and <c>text:tab</c> carries a tab,
    /// which otherwise disappears entirely.
    /// </remarks>
    private static List<object> TextContent(string text)
    {
        var nodes = new List<object>();
        var pending = new StringBuilder();

        void Flush()
        {
            if (pending.Length > 0)
            {
                nodes.Add(new XText(pending.ToString()));
                pending.Clear();
            }
        }

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];

            if (c == '\t')
            {
                Flush();
                nodes.Add(new XElement(OdtXml.Text + "tab"));
                continue;
            }

            if (c == ' ' && i + 1 < text.Length && text[i + 1] == ' ')
            {
                // The first space stays in the text; the run that follows it
                // becomes one text:s with a count.
                pending.Append(' ');

                var extra = 0;
                while (i + 1 < text.Length && text[i + 1] == ' ')
                {
                    extra++;
                    i++;
                }

                Flush();

                nodes.Add(new XElement(
                    OdtXml.Text + "s",
                    new XAttribute(OdtXml.Text + "c", extra)));

                continue;
            }

            pending.Append(c);
        }

        Flush();

        return nodes;
    }

    private static XElement WriteTable(
        IrTable table,
        AutomaticStyles automatic,
        List<(string Path, ImageRun Image)> images,
        StyleSheet styles,
        CancellationToken ct)
    {
        var element = new XElement(
            OdtXml.Table + "table",
            new XAttribute(OdtXml.Table + "name", "Table1"));

        var columns = table.Rows.Count == 0 ? 0 : table.Rows.Max(r => r.Sum(c => Math.Max(1, c.ColSpan)));

        for (var c = 0; c < columns; c++)
        {
            var column = new XElement(OdtXml.Table + "table-column");
            var width = table.ColWidthsPt is { } widths && c < widths.Count ? widths[c] : (double?)null;

            if (automatic.ForColumn(width) is { } columnStyle)
            {
                column.Add(new XAttribute(OdtXml.Table + "style-name", columnStyle));
            }

            element.Add(column);
        }

        // ODT spans rows from the top cell down, like Word, and needs a
        // covered-cell placeholder in each row underneath.
        var pending = new Dictionary<int, (int RowsLeft, int ColSpan)>();

        foreach (var row in table.Rows)
        {
            ct.ThrowIfCancellationRequested();

            var rowElement = new XElement(OdtXml.Table + "table-row");
            var queue = new Queue<Cell>(row);
            var column = 0;

            while (queue.Count > 0 || pending.ContainsKey(column))
            {
                if (pending.TryGetValue(column, out var span))
                {
                    for (var i = 0; i < span.ColSpan; i++)
                    {
                        rowElement.Add(new XElement(OdtXml.Table + "covered-table-cell"));
                    }

                    pending.Remove(column);

                    if (span.RowsLeft > 1)
                    {
                        pending[column] = (span.RowsLeft - 1, span.ColSpan);
                    }

                    column += span.ColSpan;
                    continue;
                }

                if (queue.Count == 0)
                {
                    break;
                }

                var cell = queue.Dequeue();
                var colSpan = Math.Max(1, cell.ColSpan);
                var rowSpan = Math.Max(1, cell.RowSpan);

                var cellElement = new XElement(
                    OdtXml.Table + "table-cell",
                    new XAttribute(OdtXml.Office + "value-type", "string"));

                if (colSpan > 1)
                {
                    cellElement.Add(new XAttribute(OdtXml.Table + "number-columns-spanned", colSpan));
                }

                if (rowSpan > 1)
                {
                    cellElement.Add(new XAttribute(OdtXml.Table + "number-rows-spanned", rowSpan));
                    pending[column] = (rowSpan - 1, colSpan);
                }

                WriteBlocks(cell.Blocks, cellElement, automatic, images, styles, ct);

                if (!cellElement.Elements().Any(e => e.Name.LocalName is "p" or "h" or "list" or "table"))
                {
                    cellElement.Add(new XElement(OdtXml.Text + "p"));
                }

                rowElement.Add(cellElement);

                // Shading is on the cell's own automatic style in ODT; writing
                // it inline is not allowed.
                if (cell.Shading is not null)
                {
                    cellElement.Add(new XAttribute(
                        OdtXml.Table + "style-name",
                        automatic.ForCell(cell.Shading)));
                }

                column += colSpan;
            }

            element.Add(rowElement);
        }

        return element;
    }

    private static XDocument BuildContent(XElement body, AutomaticStyles automatic)
    {
        var automaticStyles = new XElement(OdtXml.Office + "automatic-styles");

        foreach (var element in automatic.Elements)
        {
            automaticStyles.Add(element);
        }

        return new XDocument(new XElement(
            OdtXml.Office + "document-content",
            OdtXml.Declarations(),
            new XAttribute(OdtXml.Office + "version", "1.3"),
            OdtStyleMap.FontFaceDeclarations(),
            automaticStyles,
            body));
    }

    private static XDocument BuildStyles(StyleSheet styles, PageSetup page) =>
        new(new XElement(
            OdtXml.Office + "document-styles",
            OdtXml.Declarations(),
            new XAttribute(OdtXml.Office + "version", "1.3"),
            OdtStyleMap.FontFaceDeclarations(),
            OdtStyleMap.NamedStyles(styles),
            OdtStyleMap.PageLayout(page)));

    private static XDocument BuildMeta(SpDocument doc)
    {
        var meta = new XElement(OdtXml.Office + "meta");

        if (doc.Meta.TryGetValue("title", out var title))
        {
            meta.Add(new XElement(OdtXml.Dc + "title", title));
        }

        if (doc.Meta.TryGetValue("author", out var author))
        {
            meta.Add(new XElement(OdtXml.Meta + "initial-creator", author));
            meta.Add(new XElement(OdtXml.Dc + "creator", author));
        }

        if (doc.Meta.TryGetValue("subject", out var subject))
        {
            meta.Add(new XElement(OdtXml.Dc + "subject", subject));
        }

        meta.Add(new XElement(OdtXml.Meta + "generator", "Scratchpad Lite"));
        meta.Add(new XElement(
            OdtXml.Dc + "date",
            DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss", System.Globalization.CultureInfo.InvariantCulture)));

        return new XDocument(new XElement(
            OdtXml.Office + "document-meta",
            OdtXml.Declarations(),
            new XAttribute(OdtXml.Office + "version", "1.3"),
            meta));
    }

    private static XDocument BuildSettings() =>
        new(new XElement(
            OdtXml.Office + "document-settings",
            OdtXml.Declarations(),
            new XAttribute(OdtXml.Office + "version", "1.3"),
            new XElement(OdtXml.Office + "settings")));

    private static XDocument BuildManifest(IEnumerable<string> images)
    {
        var manifest = new XElement(
            OdtXml.Manifest + "manifest",
            new XAttribute(XNamespace.Xmlns + "manifest", OdtXml.Manifest.NamespaceName),
            new XAttribute(OdtXml.Manifest + "version", "1.3"),
            Entry("/", OdtXml.MimeType),
            Entry("content.xml", "text/xml"),
            Entry("styles.xml", "text/xml"),
            Entry("meta.xml", "text/xml"),
            Entry("settings.xml", "text/xml"));

        foreach (var path in images)
        {
            manifest.Add(Entry(path, Markdown.MarkdownReader.MimeFor(path)));
        }

        return new XDocument(manifest);

        static XElement Entry(string path, string type) => new(
            OdtXml.Manifest + "file-entry",
            new XAttribute(OdtXml.Manifest + "full-path", path),
            new XAttribute(OdtXml.Manifest + "media-type", type));
    }
}
