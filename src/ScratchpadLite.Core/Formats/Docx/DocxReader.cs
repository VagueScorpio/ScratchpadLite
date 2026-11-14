using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using ScratchpadLite.Core.Model;
using ScratchpadLite.Core.Platform;
using EmbeddedFont = ScratchpadLite.Core.Fonts.EmbeddedFont;
using FontEmbedding = ScratchpadLite.Core.Fonts.FontEmbedding;
using FontFace = ScratchpadLite.Core.Fonts.FontFace;
using IrParagraph = ScratchpadLite.Core.Model.Paragraph;
using IrTable = ScratchpadLite.Core.Model.Table;
using WpParagraph = DocumentFormat.OpenXml.Wordprocessing.Paragraph;
using WpTable = DocumentFormat.OpenXml.Wordprocessing.Table;

namespace ScratchpadLite.Core.Formats.Docx;

/// <summary>
/// Reads a .docx into the IR (Section 7.2).
/// </summary>
/// <remarks>
/// The work is style resolution. A run in Word says almost nothing about
/// itself: "bold" may come from the run, from its character style, from the
/// paragraph style, from a style that style is based on, or from the document
/// defaults. So before anything is read, the style definitions are flattened
/// into an effective set of properties per style id, following
/// <c>basedOn</c> - and then a run's own properties are layered on top.
/// <para>
/// Styles are matched to the app's style sheet by <em>name</em>, never by id.
/// Word's ids are localised: a German Word writes <c>berschrift1</c> for
/// Heading 1. The name is in the file and is the same everywhere.
/// </para>
/// </remarks>
internal static class DocxReader
{
    /// <summary>
    /// How many embedded font faces one document may carry. A cap rather than
    /// a limit anybody will meet: four faces times a few families is the real
    /// case, and the number is here so a crafted file cannot ask for ten
    /// thousand (Section 7.5).
    /// </summary>
    private const int MaxEmbeddedFaces = 64;

    internal static ReadResult Read(Stream input, StyleSheet styles, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(styles);

        var warnings = new List<string>();

        using var package = WordprocessingDocument.Open(input, isEditable: false);

        var main = package.MainDocumentPart
                   ?? throw new InvalidDataException("this .docx has no document part");

        var resolver = StyleResolver.From(main, styles);
        var numbering = NumberingMap.From(main);
        var body = main.Document?.Body;

        if (body is null)
        {
            return new ReadResult(SpDocument.Empty, warnings);
        }

        var context = new Context(main, resolver, numbering, warnings, ct);
        var blocks = ReadBlocks(body, context);

        if (blocks.Count == 0)
        {
            blocks = [new IrParagraph()];
        }

        var fonts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        CollectFonts(blocks, fonts);

        return new ReadResult(
            new SpDocument(blocks, ReadMeta(package), ReadPage(body), fonts),
            warnings)
        {
            EmbeddedFonts = ReadEmbeddedFonts(main, warnings),
        };
    }

    /// <summary>
    /// The fonts the document brought with it (Section 7.4), unscrambled.
    /// </summary>
    /// <remarks>
    /// <para>Everything here is treated as hostile, because an embedded font
    /// is a block of bytes from a file somebody sent (Section 7.5): the
    /// number of faces is capped, each one is size-checked before it is read,
    /// and a face that is damaged or missing its key is passed over with a
    /// warning rather than stopping the document opening. A document whose
    /// fonts fail to load is still a document.</para>
    ///
    /// <para>A face is kept only if it looks like a font once the key has
    /// been applied. That check is worth more than it looks: it is the one
    /// thing that would catch the obfuscation being wrong, which is otherwise
    /// a silent failure that produces a tab of unreadable text.</para>
    /// </remarks>
    private static IReadOnlyList<EmbeddedFont> ReadEmbeddedFonts(
        MainDocumentPart main, List<string> warnings)
    {
        if (main.FontTablePart?.Fonts is not { } table)
        {
            return [];
        }

        var found = new List<EmbeddedFont>();

        foreach (var font in table.Elements<Font>())
        {
            var family = font.Name?.Value;

            if (string.IsNullOrWhiteSpace(family))
            {
                continue;
            }

            foreach (var (reference, face) in Faces(font))
            {
                if (found.Count >= MaxEmbeddedFaces)
                {
                    warnings.Add(
                        $"this document embeds more than {MaxEmbeddedFaces} font faces; "
                        + "the rest were left out");

                    return found;
                }

                if (ReadFace(main, reference, family!, face, warnings) is { } embedded)
                {
                    found.Add(embedded);
                }
            }
        }

        return found;
    }

    private static IEnumerable<(FontRelationshipType Reference, FontFace Face)> Faces(Font font)
    {
        foreach (var child in font.ChildElements.OfType<FontRelationshipType>())
        {
            var face = child switch
            {
                EmbedBoldFont => FontFace.Bold,
                EmbedItalicFont => FontFace.Italic,
                EmbedBoldItalicFont => FontFace.BoldItalic,
                EmbedRegularFont => FontFace.Regular,

                // The same base type carries w:altName and the rest, which
                // are not faces.
                _ => (FontFace?)null,
            };

            if (face is { } which)
            {
                yield return (child, which);
            }
        }
    }

    private static EmbeddedFont? ReadFace(
        MainDocumentPart main,
        FontRelationshipType reference,
        string family,
        FontFace face,
        List<string> warnings)
    {
        var id = reference.Id?.Value;
        var rawKey = reference.FontKey?.Value;

        if (string.IsNullOrEmpty(id) || !Guid.TryParse(rawKey, out var key))
        {
            warnings.Add($"an embedded face of {family} has no usable font key and was left out");

            return null;
        }

        try
        {
            if (main.FontTablePart?.GetPartById(id) is not FontPart part)
            {
                return null;
            }

            using var stream = part.GetStream(FileMode.Open, FileAccess.Read);

            if (stream.CanSeek)
            {
                Limits.CheckDecodedFontSize(stream.Length);
            }

            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);

            var bytes = DocxFontEmbedder.Deobfuscate(buffer.ToArray(), key);

            if (!LooksLikeAFont(bytes))
            {
                warnings.Add($"an embedded face of {family} could not be unscrambled and was left out");

                return null;
            }

            return new EmbeddedFont(family, face, bytes)
            {
                Extension = FontEmbedding.ExtensionFor(bytes),
            };
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException
                                      or ArgumentException or KeyNotFoundException
                                      or FileTooLargeException)
        {
            warnings.Add($"an embedded face of {family} could not be read: {ex.Message}");

            return null;
        }
    }

    /// <summary>The four sfnt signatures, which is what a deobfuscated face must start with.</summary>
    private static bool LooksLikeAFont(ReadOnlySpan<byte> bytes) =>
        bytes.Length > 32
        && (bytes.StartsWith("\0\u0001\0\0"u8)
            || bytes.StartsWith("OTTO"u8)
            || bytes.StartsWith("true"u8)
            || bytes.StartsWith("ttcf"u8));

    private sealed record Context(
        MainDocumentPart Main,
        StyleResolver Styles,
        NumberingMap Numbering,
        List<string> Warnings,
        CancellationToken Ct);

    /// <summary>
    /// Reads the body's children in order, so paragraphs and tables stay in
    /// sequence (Section 7.2).
    /// </summary>
    private static List<Block> ReadBlocks(OpenXmlElement parent, Context context)
    {
        var blocks = new List<Block>();

        foreach (var element in parent.ChildElements)
        {
            context.Ct.ThrowIfCancellationRequested();

            switch (element)
            {
                case WpParagraph paragraph:
                    if (ReadParagraph(paragraph, context) is { } read)
                    {
                        blocks.Add(read);
                    }

                    break;

                case WpTable table:
                    blocks.Add(ReadTable(table, context));
                    break;

                case SdtBlock structured:
                    // A content control. Its content is ordinary blocks one
                    // level down, and the control itself has no IR meaning.
                    if (structured.Descendants<SdtContentBlock>().FirstOrDefault() is { } content)
                    {
                        blocks.AddRange(ReadBlocks(content, context));
                    }

                    break;
            }
        }

        return blocks;
    }

    private static Block? ReadParagraph(WpParagraph paragraph, Context context)
    {
        var properties = paragraph.ParagraphProperties;
        var styleId = properties?.ParagraphStyleId?.Val?.Value;
        var effective = context.Styles.EffectiveFor(styleId);

        // A paragraph whose only border is at the bottom and which holds no
        // text is how Word writes a horizontal rule.
        if (IsRule(paragraph, properties))
        {
            return new HorizontalRule();
        }

        var inlines = ReadInlines(paragraph, effective, context);

        var list = properties?.NumberingProperties is { } numberingProperties
            ? context.Numbering.For(numberingProperties, context.Styles, styleId)
            : null;

        var indentation = properties?.Indentation;
        var spacing = properties?.SpacingBetweenLines;

        var firstLine = Points(indentation?.FirstLine)
                        ?? (Points(indentation?.Hanging) is { } hanging ? -hanging : null);

        return new IrParagraph
        {
            StyleName = effective.StyleName,
            Align = AlignOf(properties?.Justification?.Val?.Value),

            // A list paragraph's indents come from the numbering definition in
            // Word, so taking them as the paragraph's own would double them up
            // on the way back out.
            IndentLeftPt = list is null ? Points(indentation?.Left) ?? 0 : 0,
            IndentRightPt = Points(indentation?.Right) ?? 0,
            FirstLinePt = list is null ? firstLine ?? 0 : 0,
            SpaceBeforePt = Points(spacing?.Before),
            SpaceAfterPt = Points(spacing?.After),
            LineSpacing = LineSpacingOf(spacing),
            List = list,
            PageBreakBefore = properties?.PageBreakBefore is not null
                              && properties.PageBreakBefore.Val?.Value != false,
            Inlines = inlines,
        };
    }

    private static bool IsRule(WpParagraph paragraph, ParagraphProperties? properties)
    {
        if (properties?.ParagraphBorders is not { } borders)
        {
            return false;
        }

        var onlyBottom = borders.BottomBorder is not null
                         && borders.TopBorder is null
                         && borders.LeftBorder is null
                         && borders.RightBorder is null;

        return onlyBottom && paragraph.InnerText.Length == 0;
    }

    private static double? LineSpacingOf(SpacingBetweenLines? spacing)
    {
        if (spacing?.Line?.Value is not { } line
            || !int.TryParse(line, out var twentieths))
        {
            return null;
        }

        var rule = spacing.LineRule?.Value ?? LineSpacingRuleValues.Auto;

        // "auto" means a multiple measured in 240ths of a line. "exact" and
        // "atLeast" are a measurement in twips, which the IR has no place for.
        return rule == LineSpacingRuleValues.Auto ? twentieths / 240.0 : null;
    }

    private static List<Model.Inline> ReadInlines(
        OpenXmlElement parent,
        StyleResolver.Effective paragraphStyle,
        Context context,
        string? link = null)
    {
        var inlines = new List<Model.Inline>();

        foreach (var element in parent.ChildElements)
        {
            switch (element)
            {
                case Run run:
                    ReadRun(run, paragraphStyle, context, link, inlines);
                    break;

                case Hyperlink hyperlink:
                {
                    var target = TargetOf(hyperlink, context.Main);
                    inlines.AddRange(ReadInlines(hyperlink, paragraphStyle, context, target));
                    break;
                }

                case SimpleField field:
                    inlines.AddRange(ReadInlines(field, paragraphStyle, context, link));
                    break;

                case SdtRun structured:
                    if (structured.Descendants<SdtContentRun>().FirstOrDefault() is { } content)
                    {
                        inlines.AddRange(ReadInlines(content, paragraphStyle, context, link));
                    }

                    break;
            }
        }

        return inlines;
    }

    private static void ReadRun(
        Run run,
        StyleResolver.Effective paragraphStyle,
        Context context,
        string? link,
        List<Model.Inline> inlines)
    {
        var style = context.Styles.ForRun(run, paragraphStyle, link);

        foreach (var child in run.ChildElements)
        {
            switch (child)
            {
                case Text text:
                    if (text.Text.Length > 0)
                    {
                        inlines.Add(new TextRun(text.Text, style));
                    }

                    break;

                case Break line when line.Type?.Value is null
                                     || line.Type.Value == BreakValues.TextWrapping:
                    inlines.Add(new Model.LineBreak());
                    break;

                case TabChar:
                    inlines.Add(new TextRun("\t", style));
                    break;

                case DocumentFormat.OpenXml.Wordprocessing.Drawing drawing:
                    if (ReadImage(drawing, context) is { } image)
                    {
                        inlines.Add(image);
                    }

                    break;

                case Picture:
                    // A VML picture, which is how Word 2003 and some tools
                    // write images. Reading the old shape format is out of
                    // scope, so it is reported rather than dropped silently.
                    Warn(context, "Some images are in an old format and could not be read.");
                    break;
            }
        }
    }

    private static ImageRun? ReadImage(
        DocumentFormat.OpenXml.Wordprocessing.Drawing drawing,
        Context context)
    {
        var blip = drawing.Descendants<DocumentFormat.OpenXml.Drawing.Blip>().FirstOrDefault();
        var id = blip?.Embed?.Value;

        if (id is null)
        {
            return null;
        }

        if (context.Main.GetPartById(id) is not ImagePart part)
        {
            Warn(context, "An image could not be found inside the file.");
            return null;
        }

        byte[] bytes;

        try
        {
            using var stream = part.GetStream();
            using var buffer = new MemoryStream();

            stream.CopyTo(buffer);
            bytes = buffer.ToArray();
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException)
        {
            Warn(context, "An image inside the file could not be read.");
            return null;
        }

        var extent = drawing
            .Descendants<DocumentFormat.OpenXml.Drawing.Wordprocessing.Extent>()
            .FirstOrDefault();

        var properties = drawing
            .Descendants<DocumentFormat.OpenXml.Drawing.Wordprocessing.DocProperties>()
            .FirstOrDefault();

        return new ImageRun(
            bytes,
            MimeOf(part),
            extent?.Cx is { } cx ? Units.EmuToPoints(cx) : 0,
            extent?.Cy is { } cy ? Units.EmuToPoints(cy) : 0,
            properties?.Description?.Value ?? "");
    }

    private static string MimeOf(ImagePart part)
    {
        var type = part.ContentType;

        return string.IsNullOrEmpty(type) ? "image/png" : type;
    }

    private static string? TargetOf(Hyperlink hyperlink, MainDocumentPart main)
    {
        if (hyperlink.Id?.Value is { } id)
        {
            try
            {
                return main.HyperlinkRelationships
                    .FirstOrDefault(r => r.Id == id)?.Uri.ToString();
            }
            catch (Exception ex) when (ex is KeyNotFoundException or UriFormatException)
            {
                return null;
            }
        }

        // An internal link to a bookmark in the same document.
        return hyperlink.Anchor?.Value is { } anchor ? "#" + anchor : null;
    }

    private static IrTable ReadTable(WpTable table, Context context)
    {
        var rows = new List<IReadOnlyList<Cell>>();
        var borders = HasBorders(table.GetFirstChild<TableProperties>()?.TableBorders);

        foreach (var row in table.Elements<TableRow>())
        {
            context.Ct.ThrowIfCancellationRequested();

            var cells = new List<Cell>();

            foreach (var cell in row.Elements<TableCell>())
            {
                var properties = cell.TableCellProperties;
                var merge = properties?.VerticalMerge;

                // A cell continuing a vertical merge is not a cell of its own:
                // its content belongs to the one above, which already said how
                // many rows it spans.
                if (merge is not null && merge.Val?.Value != MergedCellValues.Restart)
                {
                    continue;
                }

                cells.Add(new Cell(
                    ReadBlocks(cell, context),
                    properties?.GridSpan?.Val?.Value ?? 1,
                    merge is null ? 1 : CountMergedRows(table, row, cells.Count),
                    ShadingOf(properties?.Shading)));
            }

            if (cells.Count > 0)
            {
                rows.Add(cells);
            }
        }

        var widths = table.GetFirstChild<TableGrid>()?
            .Elements<GridColumn>()
            .Select(c => Points(c.Width) ?? 0)
            .ToList();

        return new IrTable(
            rows,
            widths is { Count: > 0 } && widths.Any(w => w > 0) ? widths : null,
            borders);
    }

    /// <summary>
    /// How many rows a vertically merged cell covers, by counting the
    /// continuation cells below it.
    /// </summary>
    private static int CountMergedRows(WpTable table, TableRow start, int column)
    {
        var rows = table.Elements<TableRow>().ToList();
        var index = rows.IndexOf(start);
        var span = 1;

        for (var r = index + 1; r < rows.Count; r++)
        {
            var cells = rows[r].Elements<TableCell>().ToList();

            if (column >= cells.Count)
            {
                break;
            }

            var merge = cells[column].TableCellProperties?.VerticalMerge;

            if (merge is null || merge.Val?.Value == MergedCellValues.Restart)
            {
                break;
            }

            span++;
        }

        return span;
    }

    private static string? ShadingOf(Shading? shading)
    {
        var fill = shading?.Fill?.Value;

        return fill is null or "auto" or "FFFFFF" ? null : "#" + fill.ToUpperInvariant();
    }

    private static Dictionary<string, string> ReadMeta(WordprocessingDocument package)
    {
        var meta = new Dictionary<string, string>();
        var core = package.PackageProperties;

        if (!string.IsNullOrWhiteSpace(core.Title))
        {
            meta["title"] = core.Title;
        }

        if (!string.IsNullOrWhiteSpace(core.Creator))
        {
            meta["author"] = core.Creator;
        }

        if (!string.IsNullOrWhiteSpace(core.Subject))
        {
            meta["subject"] = core.Subject;
        }

        return meta;
    }

    /// <summary>Page setup from the last section properties (Section 7.2).</summary>
    private static PageSetup ReadPage(Body body)
    {
        var section = body.Elements<SectionProperties>().LastOrDefault();

        if (section is null)
        {
            return new PageSetup();
        }

        var size = section.GetFirstChild<PageSize>();
        var margin = section.GetFirstChild<PageMargin>();

        var landscape = size?.Orient?.Value == PageOrientationValues.Landscape;
        var widthPt = size?.Width is { } w ? Units.TwipsToPoints(w.Value) : 612;
        var heightPt = size?.Height is { } h ? Units.TwipsToPoints(h.Value) : 792;

        // Compared against the paper sizes rather than stored, because the IR
        // names a paper rather than measuring one.
        var shortSide = Math.Min(widthPt, heightPt);
        var longSide = Math.Max(widthPt, heightPt);

        return new PageSetup(
            PaperFor(shortSide, longSide),
            landscape,
            margin?.Top is { } top ? Units.TwipsToPoints(top.Value) : 72,
            margin?.Bottom is { } bottom ? Units.TwipsToPoints(bottom.Value) : 72,
            margin?.Left is { } left ? Units.TwipsToPoints(left.Value) : 72,
            margin?.Right is { } right ? Units.TwipsToPoints(right.Value) : 72);
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

    private static void CollectFonts(IReadOnlyList<Block> blocks, HashSet<string> fonts)
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

    /// <summary>
    /// The SDK's enum-like values are structs rather than constants, so they
    /// cannot be used in a switch pattern - hence these.
    /// </summary>
    private static Align AlignOf(JustificationValues? value)
    {
        if (value is null)
        {
            return Align.Left;
        }

        if (value == JustificationValues.Center)
        {
            return Align.Center;
        }

        if (value == JustificationValues.Right || value == JustificationValues.End)
        {
            return Align.Right;
        }

        if (value == JustificationValues.Both || value == JustificationValues.Distribute)
        {
            return Align.Justify;
        }

        return Align.Left;
    }

    private static bool HasBorders(TableBorders? borders)
    {
        var top = borders?.TopBorder?.Val?.Value;

        return top is not null && top != BorderValues.None && top != BorderValues.Nil;
    }

    private static void Warn(Context context, string message)
    {
        if (!context.Warnings.Contains(message, StringComparer.Ordinal))
        {
            context.Warnings.Add(message);
        }
    }

    /// <summary>A twips attribute as points.</summary>
    private static double? Points(StringValue? twips) =>
        twips?.Value is { } text && int.TryParse(text, out var value)
            ? Units.TwipsToPoints(value)
            : null;

    private static double? Points(Int32Value? twips) =>
        twips is { } value ? Units.TwipsToPoints(value.Value) : null;

    private static double? Points(UInt32Value? twips) =>
        twips is { } value ? Units.TwipsToPoints(value.Value) : null;
}
