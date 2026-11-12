using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;
using ScratchpadLite.Core.Model;
using ScratchpadLite.Core.Platform;
using IrBlock = ScratchpadLite.Core.Model.Block;
using IrParagraph = ScratchpadLite.Core.Model.Paragraph;
using IrTable = ScratchpadLite.Core.Model.Table;

namespace ScratchpadLite.Core.Formats.Odt;

/// <summary>
/// Reads an OpenDocument text file into the IR (Section 7.2).
/// </summary>
/// <remarks>
/// The same shape of problem as .docx: formatting is in styles, and a style
/// may be based on another, so the definitions have to be flattened before the
/// content means anything. ODT splits them across two parts - the named styles
/// in <c>styles.xml</c> and the automatic styles for direct formatting in
/// <c>content.xml</c> - and a document refers to both by the same kind of
/// name, so both are loaded into one table.
/// <para>
/// Read with <see cref="XDocument"/> through <see cref="Limits.SafeXml"/>: an
/// ODT is untrusted input, and an XML parser with DTD processing left on is an
/// entity-expansion attack waiting to happen (Section 7.5).
/// </para>
/// </remarks>
internal static class OdtReader
{
    internal static ReadResult Read(Stream input, StyleSheet styles, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(styles);

        using var zip = new ZipArchive(input, ZipArchiveMode.Read);

        Limits.CheckZip(zip);
        CheckMimeType(zip);

        var warnings = new List<string>();
        var resolver = StyleTable.From(zip, styles);
        var content = Load(zip, "content.xml")
                      ?? throw new InvalidDataException("this .odt has no content.xml");

        var body = content.Root?
            .Element(OdtXml.Office + "body")?
            .Element(OdtXml.Office + "text");

        if (body is null)
        {
            return new ReadResult(SpDocument.Empty, warnings);
        }

        var context = new Context(zip, resolver, styles, warnings, ct);
        var blocks = ReadBlocks(body, context, listLevel: -1);

        if (blocks.Count == 0)
        {
            blocks = [new IrParagraph()];
        }

        var fonts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        CollectFonts(blocks, fonts);

        return new ReadResult(
            new SpDocument(blocks, ReadMeta(zip), ReadPage(zip), fonts),
            warnings);
    }

    private sealed record Context(
        ZipArchive Zip,
        StyleTable Styles,
        StyleSheet Sheet,
        List<string> Warnings,
        CancellationToken Ct);

    /// <summary>
    /// Checks the <c>mimetype</c> entry, which is how an ODT says what it is
    /// (Section 7.2).
    /// </summary>
    private static void CheckMimeType(ZipArchive zip)
    {
        var entry = zip.GetEntry("mimetype");

        if (entry is null)
        {
            // A zip with no mimetype entry is not an ODT. It may well be a
            // .docx someone renamed, which is worth saying.
            throw new InvalidDataException(
                "this file has no mimetype entry, so it is not an OpenDocument file");
        }

        using var stream = entry.Open();
        using var reader = new StreamReader(stream);

        var declared = reader.ReadToEnd().Trim();

        if (!declared.Equals(OdtXml.MimeType, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"this is an OpenDocument file of type '{declared}', not a text document");
        }
    }

    private static XDocument? Load(ZipArchive zip, string path)
    {
        var entry = zip.GetEntry(path);

        if (entry is null)
        {
            return null;
        }

        using var stream = entry.Open();

        // DTD processing prohibited and no resolver: an opened file is
        // untrusted (Section 7.5).
        using var reader = XmlReader.Create(stream, Limits.SafeXml());

        return XDocument.Load(reader);
    }

    private static List<IrBlock> ReadBlocks(XElement parent, Context context, int listLevel)
    {
        var blocks = new List<IrBlock>();

        foreach (var element in parent.Elements())
        {
            context.Ct.ThrowIfCancellationRequested();

            var name = element.Name;

            if (name == OdtXml.Text + "p" || name == OdtXml.Text + "h")
            {
                // A rule has to be recognised here rather than inside
                // ReadParagraph, which can only return a paragraph - and
                // returning an empty one is how a rule quietly became a blank
                // line on every round trip.
                blocks.Add(IsRule(element, context)
                    ? new HorizontalRule()
                    : ReadParagraph(element, context, list: null));
            }
            else if (name == OdtXml.Text + "list")
            {
                blocks.AddRange(ReadList(element, context, listLevel + 1));
            }
            else if (name == OdtXml.Table + "table")
            {
                blocks.Add(ReadTable(element, context));
            }
            else if (name == OdtXml.Text + "section" || name == OdtXml.Text + "list-item")
            {
                blocks.AddRange(ReadBlocks(element, context, listLevel));
            }
        }

        return blocks;
    }

    private static IrParagraph ReadParagraph(XElement element, Context context, ListInfo? list)
    {
        var styleName = element.Attribute(OdtXml.Text + "style-name")?.Value;
        var resolved = context.Styles.Resolve(styleName);
        var sheetStyle = context.Sheet[resolved.AppStyleName];
        var inlines = ReadInlines(element, context, resolved, sheetStyle);

        return new IrParagraph
        {
            StyleName = resolved.AppStyleName,
            Align = resolved.Align,

            // A list item's indents come from the list style, not the
            // paragraph, so taking them would double them on the way out.
            // Elsewhere, an indent the app's own style already has is not
            // direct formatting - Quote is indented by the style sheet, and
            // reading that back as an indent on the paragraph would add it
            // again the next time the file was written.
            IndentLeftPt = list is not null || Near(resolved.IndentLeftPt, sheetStyle.IndentLeftPt)
                ? 0
                : resolved.IndentLeftPt ?? 0,
            IndentRightPt = resolved.IndentRightPt ?? 0,
            FirstLinePt = list is null ? resolved.FirstLinePt ?? 0 : 0,
            SpaceBeforePt = Near(resolved.SpaceBeforePt, sheetStyle.SpaceBeforePt)
                ? null
                : resolved.SpaceBeforePt,
            SpaceAfterPt = Near(resolved.SpaceAfterPt, sheetStyle.SpaceAfterPt)
                ? null
                : resolved.SpaceAfterPt,
            LineSpacing = Near(resolved.LineSpacing, sheetStyle.LineSpacing)
                ? null
                : resolved.LineSpacing,
            List = list,
            PageBreakBefore = resolved.PageBreakBefore,
            Inlines = inlines,
        };
    }

    /// <summary>
    /// Whether this paragraph is a horizontal rule.
    /// </summary>
    /// <remarks>
    /// ODT has no rule element, so every implementation writes one as an empty
    /// paragraph whose only border is at the bottom - the same convention
    /// .docx uses. "Empty" has to mean empty of anything at all, not just of
    /// text: a paragraph holding only a picture has no text either, and
    /// turning that into a rule would throw the picture away.
    /// </remarks>
    private static bool IsRule(XElement element, Context context)
    {
        var resolved = context.Styles.Resolve(element.Attribute(OdtXml.Text + "style-name")?.Value);

        return resolved.BorderBottomOnly
               && element.Value.Length == 0
               && !element.Elements().Any();
    }

    private static List<IrBlock> ReadList(XElement element, Context context, int level)
    {
        var blocks = new List<IrBlock>();
        var styleName = element.Attribute(OdtXml.Text + "style-name")?.Value;
        var kind = context.Styles.ListKindFor(styleName);

        var info = new ListInfo(
            kind,
            Math.Clamp(level, 0, 8),
            context.Styles.NumberFormatFor(styleName));

        foreach (var item in element.Elements(OdtXml.Text + "list-item"))
        {
            var first = true;

            // A start value belongs to the item it is on, not to the list, and
            // only the first item's can be carried: the IR says where a list
            // begins, not that it restarts in the middle.
            if (Int(item.Attribute(OdtXml.Text + "start-value")) is { } start
                && ReferenceEquals(item, element.Elements(OdtXml.Text + "list-item").First()))
            {
                info = info with { Start = start };
            }

            foreach (var inner in item.Elements())
            {
                if (inner.Name == OdtXml.Text + "p" || inner.Name == OdtXml.Text + "h")
                {
                    blocks.Add(ReadParagraph(
                        inner,
                        context,
                        first ? info : info with { Start = 1 }));

                    first = false;
                }
                else if (inner.Name == OdtXml.Text + "list")
                {
                    blocks.AddRange(ReadList(inner, context, level + 1));
                }
                else
                {
                    blocks.AddRange(ReadBlocks(inner, context, level));
                }
            }
        }

        return blocks;
    }

    private static IrTable ReadTable(XElement element, Context context)
    {
        var rows = new List<IReadOnlyList<Cell>>();

        foreach (var row in element.Descendants(OdtXml.Table + "table-row"))
        {
            context.Ct.ThrowIfCancellationRequested();

            var cells = new List<Cell>();

            foreach (var cell in row.Elements())
            {
                // A covered cell is the hole left by a span above it, not a
                // cell of its own.
                if (cell.Name == OdtXml.Table + "covered-table-cell")
                {
                    continue;
                }

                if (cell.Name != OdtXml.Table + "table-cell")
                {
                    continue;
                }

                var colSpan = Int(cell.Attribute(OdtXml.Table + "number-columns-spanned")) ?? 1;
                var rowSpan = Int(cell.Attribute(OdtXml.Table + "number-rows-spanned")) ?? 1;
                var cellStyle = cell.Attribute(OdtXml.Table + "style-name")?.Value;

                cells.Add(new Cell(
                    ReadBlocks(cell, context, listLevel: -1),
                    colSpan,
                    rowSpan,
                    context.Styles.CellShadingFor(cellStyle)));
            }

            if (cells.Count > 0)
            {
                rows.Add(cells);
            }
        }

        var widths = element.Elements(OdtXml.Table + "table-column")
            .Select(c => context.Styles.ColumnWidthFor(c.Attribute(OdtXml.Table + "style-name")?.Value) ?? 0)
            .ToList();

        return new IrTable(
            rows,
            widths.Count > 0 && widths.Any(w => w > 0) ? widths : null,
            Borders: true);
    }

    private static List<Model.Inline> ReadInlines(
        XElement parent,
        Context context,
        StyleTable.Resolved paragraphStyle,
        ParagraphStyle sheetStyle,
        CharStyle? inherited = null,
        string? link = null)
    {
        var inlines = new List<Model.Inline>();
        var style = inherited ?? paragraphStyle.Character(sheetStyle, context.Sheet);

        foreach (var node in parent.Nodes())
        {
            switch (node)
            {
                case XText text:
                    if (text.Value.Length > 0)
                    {
                        inlines.Add(new TextRun(text.Value, style));
                    }

                    break;

                case XElement element:
                    ReadInlineElement(element, context, paragraphStyle, sheetStyle, style, link, inlines);
                    break;
            }
        }

        return inlines;
    }

    private static void ReadInlineElement(
        XElement element,
        Context context,
        StyleTable.Resolved paragraphStyle,
        ParagraphStyle sheetStyle,
        CharStyle style,
        string? link,
        List<Model.Inline> inlines)
    {
        var name = element.Name;

        if (name == OdtXml.Text + "span")
        {
            var spanStyle = context.Styles.Resolve(element.Attribute(OdtXml.Text + "style-name")?.Value);
            var applied = spanStyle.ApplyTo(style, sheetStyle, context.Sheet);

            inlines.AddRange(ReadInlines(
                element, context, paragraphStyle, sheetStyle, applied with { Link = link }, link));
        }
        else if (name == OdtXml.Text + "a")
        {
            var target = element.Attribute(OdtXml.Xlink + "href")?.Value;

            inlines.AddRange(ReadInlines(
                element, context, paragraphStyle, sheetStyle, style with { Link = target }, target));
        }
        else if (name == OdtXml.Text + "s")
        {
            // text:s carries a run of spaces ODT would otherwise collapse.
            var count = Int(element.Attribute(OdtXml.Text + "c")) ?? 1;

            inlines.Add(new TextRun(new string(' ', Math.Clamp(count, 1, 1000)), style));
        }
        else if (name == OdtXml.Text + "tab")
        {
            inlines.Add(new TextRun("\t", style));
        }
        else if (name == OdtXml.Text + "line-break")
        {
            inlines.Add(new Model.LineBreak());
        }
        else if (name == OdtXml.Draw + "frame")
        {
            if (ReadImage(element, context) is { } image)
            {
                inlines.Add(image);
            }
        }
        else if (name == OdtXml.Text + "note" || name == OdtXml.Office + "annotation")
        {
            // Footnotes and comments, which the IR has no place for.
            Warn(context, "Footnotes and comments in this file were left out.");
        }
        else
        {
            // Something unknown that still holds text - a bookmark, a field,
            // a change-tracking marker. Its text is kept; the wrapper is not.
            inlines.AddRange(ReadInlines(element, context, paragraphStyle, sheetStyle, style, link));
        }
    }

    private static ImageRun? ReadImage(XElement frame, Context context)
    {
        var image = frame.Element(OdtXml.Draw + "image");
        var href = image?.Attribute(OdtXml.Xlink + "href")?.Value;

        if (href is null)
        {
            return null;
        }

        if (Uri.TryCreate(href, UriKind.Absolute, out var absolute) && absolute.Scheme is "http" or "https")
        {
            Warn(context, "Images from the web were not loaded.");
            return null;
        }

        var entry = context.Zip.GetEntry(href.TrimStart('.', '/'));

        if (entry is null)
        {
            Warn(context, "Some images could not be found inside the file.");
            return null;
        }

        byte[] bytes;

        try
        {
            using var stream = entry.Open();
            using var buffer = new MemoryStream();

            stream.CopyTo(buffer);
            bytes = buffer.ToArray();
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException)
        {
            Log.Warn($"an image inside an .odt could not be read: {ex.Message}");
            Warn(context, "Some images inside the file could not be read.");

            return null;
        }

        return new ImageRun(
            bytes,
            Markdown.MarkdownReader.MimeFor(href),
            OdtXml.ToPoints(frame.Attribute(OdtXml.Svg + "width")?.Value) ?? 0,
            OdtXml.ToPoints(frame.Attribute(OdtXml.Svg + "height")?.Value) ?? 0,
            frame.Element(OdtXml.Svg + "desc")?.Value ?? "");
    }

    private static Dictionary<string, string> ReadMeta(ZipArchive zip)
    {
        var meta = new Dictionary<string, string>();
        var document = Load(zip, "meta.xml");
        var element = document?.Root?.Element(OdtXml.Office + "meta");

        if (element is null)
        {
            return meta;
        }

        if (element.Element(OdtXml.Dc + "title")?.Value is { Length: > 0 } title)
        {
            meta["title"] = title;
        }

        var author = element.Element(OdtXml.Dc + "creator")?.Value
                     ?? element.Element(OdtXml.Meta + "initial-creator")?.Value;

        if (author is { Length: > 0 })
        {
            meta["author"] = author;
        }

        if (element.Element(OdtXml.Dc + "subject")?.Value is { Length: > 0 } subject)
        {
            meta["subject"] = subject;
        }

        return meta;
    }

    private static PageSetup ReadPage(ZipArchive zip)
    {
        var document = Load(zip, "styles.xml");

        var layout = document?.Root?
            .Elements(OdtXml.Office + "automatic-styles")
            .Elements(OdtXml.Style + "page-layout")
            .FirstOrDefault()?
            .Element(OdtXml.Style + "page-layout-properties");

        if (layout is null)
        {
            return new PageSetup();
        }

        var width = OdtXml.ToPoints(layout.Attribute(OdtXml.Fo + "page-width")?.Value) ?? 612;
        var height = OdtXml.ToPoints(layout.Attribute(OdtXml.Fo + "page-height")?.Value) ?? 792;

        var landscape = layout.Attribute(OdtXml.Style + "print-orientation")?.Value
            is { } orientation && orientation.Equals("landscape", StringComparison.OrdinalIgnoreCase);

        return new PageSetup(
            PaperFor(Math.Min(width, height), Math.Max(width, height)),
            landscape,
            OdtXml.ToPoints(layout.Attribute(OdtXml.Fo + "margin-top")?.Value) ?? 72,
            OdtXml.ToPoints(layout.Attribute(OdtXml.Fo + "margin-bottom")?.Value) ?? 72,
            OdtXml.ToPoints(layout.Attribute(OdtXml.Fo + "margin-left")?.Value) ?? 72,
            OdtXml.ToPoints(layout.Attribute(OdtXml.Fo + "margin-right")?.Value) ?? 72);
    }

    private static string PaperFor(double shortSide, double longSide)
    {
        (string Name, double Short, double Long)[] papers =
        [
            ("Letter", 612, 792),
            ("A4", 595.3, 841.9),
            ("A5", 419.5, 595.3),
            ("Legal", 612, 1008),
            ("Tabloid", 792, 1224),
        ];

        foreach (var (name, expectedShort, expectedLong) in papers)
        {
            if (Math.Abs(shortSide - expectedShort) < 3 && Math.Abs(longSide - expectedLong) < 3)
            {
                return name;
            }
        }

        return "Letter";
    }

    private static void CollectFonts(IReadOnlyList<IrBlock> blocks, HashSet<string> fonts)
    {
        foreach (var block in blocks)
        {
            switch (block)
            {
                case IrParagraph paragraph:
                    foreach (var inline in paragraph.Inlines)
                    {
                        if (inline is TextRun { Style.Family: { } family })
                        {
                            fonts.Add(family);
                        }
                    }

                    break;

                case IrTable table:
                    foreach (var row in table.Rows)
                    {
                        foreach (var cell in row)
                        {
                            CollectFonts(cell.Blocks, fonts);
                        }
                    }

                    break;
            }
        }
    }

    private static void Warn(Context context, string message)
    {
        if (!context.Warnings.Contains(message, StringComparer.Ordinal))
        {
            context.Warnings.Add(message);
        }
    }

    private static int? Int(XAttribute? attribute) =>
        attribute?.Value is { } value && int.TryParse(value, out var parsed) ? parsed : null;

    private static bool Near(double? a, double b) => a is null || Math.Abs(a.Value - b) < 0.01;
}
