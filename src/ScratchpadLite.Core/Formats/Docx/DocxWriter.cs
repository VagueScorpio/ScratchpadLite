using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using ScratchpadLite.Core.Model;
using ScratchpadLite.Core.Platform;
using Drawing = DocumentFormat.OpenXml.Drawing;
using IrParagraph = ScratchpadLite.Core.Model.Paragraph;
using IrTable = ScratchpadLite.Core.Model.Table;
using Pictures = DocumentFormat.OpenXml.Drawing.Pictures;
using WpParagraph = DocumentFormat.OpenXml.Wordprocessing.Paragraph;
using WpTable = DocumentFormat.OpenXml.Wordprocessing.Table;

namespace ScratchpadLite.Core.Formats.Docx;

/// <summary>
/// Writes the IR as a .docx (Section 7.2).
/// </summary>
/// <remarks>
/// Word's units are all different from the IR's, and all different from each
/// other: indents and spacing in twips (a twentieth of a point), font sizes in
/// half-points, image sizes in EMU (12700 to the point), borders in eighths of
/// a point. Everything here goes through <see cref="Units"/> so the
/// conversions are in one place.
/// <para>
/// A full style sheet is written into the file rather than formatting every
/// paragraph directly. That is what makes a heading still a heading when it is
/// opened in Word: the paragraph says "I am Heading 1" and the style says what
/// that looks like, so Word's navigation pane, table of contents and style
/// pane all work.
/// </para>
/// </remarks>
internal static class DocxWriter
{
    /// <summary>The abstract numbering definition ids this writes.</summary>
    private const int BulletAbstractId = 0;
    private const int NumberAbstractId = 1;

    /// <summary>How many list levels Word defines, and so how many this writes.</summary>
    private const int ListLevels = 9;

    internal static void Write(SpDocument doc, Stream output, StyleSheet styles, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(styles);

        using var package = WordprocessingDocument.Create(output, WordprocessingDocumentType.Document);

        var main = package.AddMainDocumentPart();
        main.Document = new Document(new Body());

        WriteStyles(main, styles);

        var numbering = new NumberingState();
        var body = main.Document.Body!;

        WriteBlocks(doc.Blocks, body, main, numbering, styles, ct);

        // Word wants the section properties last in the body.
        body.AppendChild(PageOf(doc.Page));

        if (numbering.Used)
        {
            WriteNumbering(main, numbering);
        }

        WriteCoreProperties(package, doc);

        main.Document.Save();
    }

    private static void WriteBlocks(
        IReadOnlyList<Block> blocks,
        OpenXmlCompositeElement parent,
        MainDocumentPart main,
        NumberingState numbering,
        StyleSheet styles,
        CancellationToken ct)
    {
        // A run of list paragraphs shares one numbering instance; a change of
        // kind or format starts a new one, so the numbers restart where the
        // document says they do.
        ListInfo? openList = null;
        var instance = 0;

        foreach (var block in blocks)
        {
            ct.ThrowIfCancellationRequested();

            switch (block)
            {
                case IrParagraph paragraph:
                {
                    if (paragraph.List is { } list)
                    {
                        if (openList is null || !SameList(openList, list))
                        {
                            instance = numbering.NewInstance(list);
                        }

                        openList = list;
                        parent.AppendChild(WriteParagraph(paragraph, main, styles, instance));
                        break;
                    }

                    openList = null;
                    parent.AppendChild(WriteParagraph(paragraph, main, styles, numberingId: null));
                    break;
                }

                case IrTable table:
                    openList = null;
                    parent.AppendChild(WriteTable(table, main, numbering, styles, ct));

                    // Word needs a paragraph after a table, or the next thing
                    // merges into it and the file opens with a repair prompt.
                    parent.AppendChild(new WpParagraph());
                    break;

                case HorizontalRule:
                    openList = null;
                    parent.AppendChild(WriteRule());
                    break;
            }
        }
    }

    private static bool SameList(ListInfo a, ListInfo b) =>
        a.Kind == b.Kind
        && string.Equals(a.NumberFormat, b.NumberFormat, StringComparison.Ordinal)
        && a.Start == b.Start;

    private static WpParagraph WriteParagraph(
        IrParagraph paragraph,
        MainDocumentPart main,
        StyleSheet styles,
        int? numberingId)
    {
        var wp = new WpParagraph();
        var properties = new ParagraphProperties();
        var children = new List<OpenXmlElement>
        {
            new ParagraphStyleId { Val = StyleIdFor(paragraph.StyleName) },
        };

        if (paragraph.Align != Align.Left)
        {
            children.Add(new Justification
            {
                Val = paragraph.Align switch
                {
                    Align.Center => JustificationValues.Center,
                    Align.Right => JustificationValues.Right,
                    _ => JustificationValues.Both,
                },
            });
        }

        if (numberingId is { } id)
        {
            children.Add(new NumberingProperties(
                new NumberingLevelReference { Val = Math.Clamp(paragraph.List!.Level, 0, ListLevels - 1) },
                new NumberingId { Val = id }));
        }

        if (paragraph.IndentLeftPt != 0 || paragraph.IndentRightPt != 0 || paragraph.FirstLinePt != 0)
        {
            var indentation = new Indentation();

            if (paragraph.IndentLeftPt != 0)
            {
                indentation.Left = Twips(paragraph.IndentLeftPt);
            }

            if (paragraph.IndentRightPt != 0)
            {
                indentation.Right = Twips(paragraph.IndentRightPt);
            }

            if (paragraph.FirstLinePt > 0)
            {
                indentation.FirstLine = Twips(paragraph.FirstLinePt);
            }
            else if (paragraph.FirstLinePt < 0)
            {
                // Word has no negative first line: a hanging indent is its own
                // attribute, and it is positive.
                indentation.Hanging = Twips(-paragraph.FirstLinePt);
            }

            children.Add(indentation);
        }

        if (paragraph.SpaceBeforePt is not null
            || paragraph.SpaceAfterPt is not null
            || paragraph.LineSpacing is not null)
        {
            var spacing = new SpacingBetweenLines();

            if (paragraph.SpaceBeforePt is { } before)
            {
                spacing.Before = Twips(before);
            }

            if (paragraph.SpaceAfterPt is { } after)
            {
                spacing.After = Twips(after);
            }

            if (paragraph.LineSpacing is { } line)
            {
                // Word's "auto" line rule measures in 240ths of a line, so
                // single spacing is 240 and 1.15 is 276.
                spacing.Line = Math.Round(line * 240).ToString("0", System.Globalization.CultureInfo.InvariantCulture);
                spacing.LineRule = LineSpacingRuleValues.Auto;
            }

            children.Add(spacing);
        }

        if (paragraph.PageBreakBefore)
        {
            children.Add(new PageBreakBefore());
        }

        AppendParagraphPropertiesInOrder(properties, children);
        wp.AppendChild(properties);

        var style = styles[paragraph.StyleName];

        foreach (var inline in paragraph.Inlines)
        {
            foreach (var element in WriteInline(inline, main, style, styles))
            {
                wp.AppendChild(element);
            }
        }

        return wp;
    }

    private static IEnumerable<OpenXmlElement> WriteInline(
        Model.Inline inline,
        MainDocumentPart main,
        ParagraphStyle style,
        StyleSheet styles)
    {
        switch (inline)
        {
            case TextRun text when text.Style.Link is { } link:
            {
                // A hyperlink is a relationship, not an attribute, so the
                // target lives in the part's relationship table.
                var id = main.AddHyperlinkRelationship(SafeUri(link), isExternal: true).Id;
                var hyperlink = new Hyperlink { Id = id };

                hyperlink.AppendChild(WriteRun(text, style, styles, asLink: true));

                yield return hyperlink;
                break;
            }

            case TextRun text:
                yield return WriteRun(text, style, styles, asLink: false);
                break;

            case ImageRun image:
                if (WriteImage(image, main) is { } drawing)
                {
                    yield return new Run(drawing);
                }

                break;

            case Model.LineBreak:
                yield return new Run(new Break { Type = BreakValues.TextWrapping });
                break;
        }
    }

    private static Run WriteRun(TextRun text, ParagraphStyle style, StyleSheet styles, bool asLink)
    {
        var run = new Run();
        var properties = new RunProperties();
        var children = new List<OpenXmlElement>();
        var character = text.Style;

        if (asLink)
        {
            // Word's own character style for links, so they look like links
            // without the colour being written on the run.
            children.Add(new RunStyle { Val = "Hyperlink" });
        }

        if (character.Family is { } family)
        {
            children.Add(new RunFonts { Ascii = family, HighAnsi = family });
        }

        if (character.SizePt is { } size)
        {
            // Half-points, and the complex-script size too or Word uses the
            // style's size for anything it decides is complex script.
            var halfPoints = Units.PointsToHalfPoints(size)
                .ToString("0", System.Globalization.CultureInfo.InvariantCulture);

            children.Add(new FontSize { Val = halfPoints });
            children.Add(new FontSizeComplexScript { Val = halfPoints });
        }

        // Written explicitly as on or off rather than only when true: a run
        // inside a bold style that is deliberately not bold has to say so.
        if (character.Bold != style.Bold)
        {
            children.Add(new Bold { Val = character.Bold });
        }

        if (character.Italic != style.Italic)
        {
            children.Add(new Italic { Val = character.Italic });
        }

        if (character.Underline != UnderlineKind.None)
        {
            children.Add(new Underline { Val = UnderlineFor(character.Underline) });
        }

        if (character.Strike)
        {
            children.Add(new Strike());
        }

        if (character.Vertical != VAlign.Normal)
        {
            children.Add(new VerticalTextAlignment
            {
                Val = character.Vertical == VAlign.Super
                    ? VerticalPositionValues.Superscript
                    : VerticalPositionValues.Subscript,
            });
        }

        if (character.SmallCaps)
        {
            children.Add(new SmallCaps());
        }

        if (character.AllCapsPreserved)
        {
            children.Add(new Caps());
        }

        if (character.LetterSpacingPtPreserved is { } tracking)
        {
            children.Add(new DocumentFormat.OpenXml.Wordprocessing.Spacing
            {
                Val = TwipsInt(tracking),
            });
        }

        if (HexOf(character.Color) is { } color)
        {
            children.Add(new Color { Val = color });
        }

        if (HexOf(character.Highlight) is { } highlight)
        {
            // Shading rather than Highlight: Word's Highlight element allows
            // only sixteen named colours, and the editor offers any colour at
            // all (Section 7.2).
            children.Add(new Shading
            {
                Val = ShadingPatternValues.Clear,
                Color = "auto",
                Fill = highlight,
            });
        }

        if (children.Count > 0)
        {
            AppendInOrder(properties, children);
            run.AppendChild(properties);
        }

        // Space="preserve" or Word drops leading and trailing spaces, which
        // silently runs words together.
        run.AppendChild(new Text(text.Text) { Space = SpaceProcessingModeValues.Preserve });

        _ = styles;
        return run;
    }

    private static UnderlineValues UnderlineFor(UnderlineKind kind) => kind switch
    {
        UnderlineKind.Double => UnderlineValues.Double,
        UnderlineKind.Dotted => UnderlineValues.Dotted,
        UnderlineKind.Dashed => UnderlineValues.Dash,
        UnderlineKind.Wavy => UnderlineValues.Wave,
        _ => UnderlineValues.Single,
    };

    private static WpTable WriteTable(
        IrTable table,
        MainDocumentPart main,
        NumberingState numbering,
        StyleSheet styles,
        CancellationToken ct)
    {
        var wp = new WpTable();
        var properties = new TableProperties();

        if (table.Borders)
        {
            // Sizes are in eighths of a point, so 4 is half a point.
            properties.AppendChild(new TableBorders(
                new TopBorder { Val = BorderValues.Single, Size = 4, Color = "808080" },
                new LeftBorder { Val = BorderValues.Single, Size = 4, Color = "808080" },
                new BottomBorder { Val = BorderValues.Single, Size = 4, Color = "808080" },
                new RightBorder { Val = BorderValues.Single, Size = 4, Color = "808080" },
                new InsideHorizontalBorder { Val = BorderValues.Single, Size = 4, Color = "808080" },
                new InsideVerticalBorder { Val = BorderValues.Single, Size = 4, Color = "808080" }));
        }

        properties.AppendChild(new TableLayout { Type = TableLayoutValues.Fixed });
        wp.AppendChild(properties);

        var columns = table.Rows.Count == 0 ? 0 : table.Rows.Max(r => r.Sum(c => Math.Max(1, c.ColSpan)));
        var grid = new TableGrid();

        for (var c = 0; c < columns; c++)
        {
            grid.AppendChild(new GridColumn
            {
                Width = table.ColWidthsPt is { } widths && c < widths.Count
                    ? Twips(widths[c])
                    : Twips(90),
            });
        }

        wp.AppendChild(grid);

        // Rows still covered by a cell above them, by the column they start
        // at. Word spans rows by marking the top cell "restart" and putting a
        // "continue" cell in each row below it, and those continuation cells
        // have to be written: the IR leaves them out, so without this a
        // two-row span came back out of a file as a one-row cell.
        var pending = new Dictionary<int, (int RowsLeft, int ColSpan)>();

        foreach (var row in table.Rows)
        {
            ct.ThrowIfCancellationRequested();

            var wpRow = new TableRow();
            var queue = new Queue<Cell>(row);
            var column = 0;

            while (queue.Count > 0 || pending.ContainsKey(column))
            {
                if (pending.TryGetValue(column, out var span))
                {
                    wpRow.AppendChild(Continuation(span.ColSpan));

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

                wpRow.AppendChild(WriteCell(cell, colSpan, rowSpan, main, numbering, styles, ct));

                if (rowSpan > 1)
                {
                    pending[column] = (rowSpan - 1, colSpan);
                }

                column += colSpan;
            }

            wp.AppendChild(wpRow);
        }

        return wp;
    }

    private static TableCell WriteCell(
        Cell cell,
        int colSpan,
        int rowSpan,
        MainDocumentPart main,
        NumberingState numbering,
        StyleSheet styles,
        CancellationToken ct)
    {
        var wpCell = new TableCell();
        var cellProperties = new TableCellProperties();

        // In schema order: gridSpan, then vMerge, then shd.
        if (colSpan > 1)
        {
            cellProperties.AppendChild(new GridSpan { Val = colSpan });
        }

        if (rowSpan > 1)
        {
            cellProperties.AppendChild(new VerticalMerge { Val = MergedCellValues.Restart });
        }

        if (HexOf(cell.Shading) is { } shading)
        {
            cellProperties.AppendChild(new Shading
            {
                Val = ShadingPatternValues.Clear,
                Color = "auto",
                Fill = shading,
            });
        }

        if (cellProperties.HasChildren)
        {
            wpCell.AppendChild(cellProperties);
        }

        WriteBlocks(cell.Blocks, wpCell, main, numbering, styles, ct);

        // Word requires at least one paragraph in a cell.
        if (!wpCell.Elements<WpParagraph>().Any())
        {
            wpCell.AppendChild(new WpParagraph());
        }

        return wpCell;
    }

    /// <summary>A cell that continues a vertical merge from the row above.</summary>
    private static TableCell Continuation(int colSpan)
    {
        var properties = new TableCellProperties();

        if (colSpan > 1)
        {
            properties.AppendChild(new GridSpan { Val = colSpan });
        }

        properties.AppendChild(new VerticalMerge { Val = MergedCellValues.Continue });

        return new TableCell(properties, new WpParagraph());
    }

    /// <summary>
    /// A horizontal rule, which Word writes as a paragraph with only a bottom
    /// border.
    /// </summary>
    private static WpParagraph WriteRule() => new(
        new ParagraphProperties(
            new ParagraphBorders(new BottomBorder
            {
                Val = BorderValues.Single,
                Size = 6,
                Space = 1,
                Color = "auto",
            })));

    /// <summary>
    /// An inline image, as a Drawing referring to an image part
    /// (Section 7.2).
    /// </summary>
    private static DocumentFormat.OpenXml.Wordprocessing.Drawing? WriteImage(ImageRun image, MainDocumentPart main)
    {
        var type = PartTypeFor(image.Mime);

        if (type is null)
        {
            Log.Warn($"an image with type {image.Mime} was not written to the .docx");
            return null;
        }

        var part = main.AddImagePart(type.Value);

        using (var stream = new MemoryStream(image.Data, writable: false))
        {
            part.FeedData(stream);
        }

        var id = main.GetIdOfPart(part);
        var width = Units.PointsToEmu(image.WidthPt);
        var height = Units.PointsToEmu(image.HeightPt);

        // Every one of these elements is required, and the ids have to be
        // unique within the document; Word refuses the file otherwise.
        var unique = (uint)Math.Abs(HashCode.Combine(id, image.Data.Length)) + 1u;
        var name = image.Alt.Length > 0 ? image.Alt : "Image";

        return new DocumentFormat.OpenXml.Wordprocessing.Drawing(
            new DocumentFormat.OpenXml.Drawing.Wordprocessing.Inline(
                new DocumentFormat.OpenXml.Drawing.Wordprocessing.Extent
                {
                    Cx = (long)width,
                    Cy = (long)height,
                },
                new DocumentFormat.OpenXml.Drawing.Wordprocessing.DocProperties
                {
                    Id = unique,
                    Name = name,
                    Description = image.Alt,
                },
                new Drawing.Graphic(
                    new Drawing.GraphicData(
                        new Pictures.Picture(
                            new Pictures.NonVisualPictureProperties(
                                new Pictures.NonVisualDrawingProperties
                                {
                                    Id = 0u,
                                    Name = name,
                                },
                                new Pictures.NonVisualPictureDrawingProperties()),
                            new Pictures.BlipFill(
                                new Drawing.Blip { Embed = id },
                                new Drawing.Stretch(new Drawing.FillRectangle())),
                            new Pictures.ShapeProperties(
                                new Drawing.Transform2D(
                                    new Drawing.Offset { X = 0, Y = 0 },
                                    new Drawing.Extents { Cx = (long)width, Cy = (long)height }),
                                new Drawing.PresetGeometry(new Drawing.AdjustValueList())
                                {
                                    Preset = Drawing.ShapeTypeValues.Rectangle,
                                })))
                    {
                        Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture",
                    }))
            {
                DistanceFromTop = 0u,
                DistanceFromBottom = 0u,
                DistanceFromLeft = 0u,
                DistanceFromRight = 0u,
            });
    }

    internal static PartTypeInfo? PartTypeFor(string mime) => mime.ToLowerInvariant() switch
    {
        "image/png" => ImagePartType.Png,
        "image/jpeg" or "image/jpg" => ImagePartType.Jpeg,
        "image/gif" => ImagePartType.Gif,
        "image/bmp" => ImagePartType.Bmp,
        "image/tiff" => ImagePartType.Tiff,
        _ => null,
    };

    private static SectionProperties PageOf(PageSetup page)
    {
        var width = TwipsUInt(PaperWidthPt(page.Paper));
        var height = TwipsUInt(PaperHeightPt(page.Paper));

        return new SectionProperties(
            new PageSize
            {
                Width = page.Landscape ? height : width,
                Height = page.Landscape ? width : height,
                Orient = page.Landscape ? PageOrientationValues.Landscape : PageOrientationValues.Portrait,
            },
            new PageMargin
            {
                Top = TwipsInt(page.MarginTopPt),
                Bottom = TwipsInt(page.MarginBottomPt),
                Left = TwipsUInt(page.MarginLeftPt),
                Right = TwipsUInt(page.MarginRightPt),
                Header = 720u,
                Footer = 720u,
                Gutter = 0u,
            });
    }

    internal static double PaperWidthPt(string paper) => paper switch
    {
        "A4" => 595.3,
        "A5" => 419.5,
        "Legal" => 612,
        "Tabloid" => 792,
        _ => 612,
    };

    internal static double PaperHeightPt(string paper) => paper switch
    {
        "A4" => 841.9,
        "A5" => 595.3,
        "Legal" => 1008,
        "Tabloid" => 1224,
        _ => 792,
    };

    private static void WriteCoreProperties(WordprocessingDocument package, SpDocument doc)
    {
        var core = package.PackageProperties;

        if (doc.Meta.TryGetValue("title", out var title))
        {
            core.Title = title;
        }

        if (doc.Meta.TryGetValue("author", out var author))
        {
            core.Creator = author;
        }

        if (doc.Meta.TryGetValue("subject", out var subject))
        {
            core.Subject = subject;
        }

        core.Modified = DateTime.UtcNow;
        core.Created ??= DateTime.UtcNow;
    }

    /// <summary>
    /// Writes the style sheet into the file (Section 7.2).
    /// </summary>
    /// <remarks>
    /// Without this, a heading would have to be written as directly formatted
    /// 20-point bold text, and Word would have no idea it was a heading - no
    /// navigation pane entry, no table of contents, and no way for the reader
    /// to restyle it.
    /// </remarks>
    private static void WriteStyles(MainDocumentPart main, StyleSheet styles)
    {
        var part = main.AddNewPart<StyleDefinitionsPart>();
        var all = new Styles();

        var normal = styles[StyleSheet.NormalStyleName];

        all.AppendChild(new DocDefaults(
            new RunPropertiesDefault(new RunPropertiesBaseStyle(
                new RunFonts { Ascii = normal.Family, HighAnsi = normal.Family },
                new FontSize
                {
                    Val = Units.PointsToHalfPoints(normal.SizePt)
                        .ToString("0", System.Globalization.CultureInfo.InvariantCulture),
                })),
            new ParagraphPropertiesDefault()));

        foreach (var style in styles.All)
        {
            all.AppendChild(StyleFor(style, isDefault: style.Name == StyleSheet.NormalStyleName));
        }

        // The character style Word uses for hyperlinks, and the paragraph
        // style it puts on list items.
        all.AppendChild(new Style(
            new StyleName { Val = "Hyperlink" },
            new StyleRunProperties(
                new Color { Val = "0563C1" },
                new Underline { Val = UnderlineValues.Single }))
        {
            Type = StyleValues.Character,
            StyleId = "Hyperlink",
        });

        all.AppendChild(new Style(
            new StyleName { Val = "List Paragraph" },
            new BasedOn { Val = StyleIdFor(StyleSheet.NormalStyleName) })
        {
            Type = StyleValues.Paragraph,
            StyleId = "ListParagraph",
        });

        part.Styles = all;
        part.Styles.Save();
    }

    private static Style StyleFor(ParagraphStyle style, bool isDefault)
    {
        var runProperties = new StyleRunProperties();
        var runChildren = new List<OpenXmlElement>();

        if (style.Family is { } family)
        {
            runChildren.Add(new RunFonts { Ascii = family, HighAnsi = family });
        }

        var halfPoints = Units.PointsToHalfPoints(style.SizePt)
            .ToString("0", System.Globalization.CultureInfo.InvariantCulture);

        runChildren.Add(new FontSize { Val = halfPoints });
        runChildren.Add(new FontSizeComplexScript { Val = halfPoints });

        if (style.Bold)
        {
            runChildren.Add(new Bold());
        }

        if (style.Italic)
        {
            runChildren.Add(new Italic());
        }

        if (HexOf(style.Color) is { } color)
        {
            runChildren.Add(new Color { Val = color });
        }

        if (HexOf(style.Shading) is { } shading)
        {
            runChildren.Add(new Shading
            {
                Val = ShadingPatternValues.Clear,
                Color = "auto",
                Fill = shading,
            });
        }

        AppendInOrder(runProperties, runChildren);

        var paragraphProperties = new StyleParagraphProperties();

        if (style.SpaceBeforePt != 0 || style.SpaceAfterPt != 0 || style.LineSpacing != 1.0)
        {
            paragraphProperties.AppendChild(new SpacingBetweenLines
            {
                Before = Twips(style.SpaceBeforePt),
                After = Twips(style.SpaceAfterPt),
                Line = Math.Round(style.LineSpacing * 240)
                    .ToString("0", System.Globalization.CultureInfo.InvariantCulture),
                LineRule = LineSpacingRuleValues.Auto,
            });
        }

        if (style.IndentLeftPt != 0)
        {
            paragraphProperties.AppendChild(new Indentation { Left = Twips(style.IndentLeftPt) });
        }

        if (style.OutlineLevel is { } outline)
        {
            // What makes Word treat it as a heading in the navigation pane and
            // in a table of contents.
            paragraphProperties.AppendChild(new OutlineLevel { Val = outline - 1 });
        }

        var result = new Style(new StyleName { Val = style.Name })
        {
            Type = StyleValues.Paragraph,
            StyleId = StyleIdFor(style.Name),
            Default = isDefault ? OnOffValue.FromBoolean(true) : null,
        };

        if (!isDefault)
        {
            result.AppendChild(new BasedOn { Val = StyleIdFor(StyleSheet.NormalStyleName) });
        }

        // Paragraph properties before run properties: that is the order the
        // schema requires inside w:style, and the opposite order makes a file
        // Word calls unreadable.
        if (paragraphProperties.HasChildren)
        {
            result.AppendChild(paragraphProperties);
        }

        if (runProperties.HasChildren)
        {
            result.AppendChild(runProperties);
        }

        return result;
    }

    /// <summary>
    /// The style id for a style name. Word matches by id internally and shows
    /// the name, and ids may not contain spaces.
    /// </summary>
    internal static string StyleIdFor(string name) =>
        name.Replace(" ", "", StringComparison.Ordinal);

    /// <summary>Tracks the numbering instances a document needs.</summary>
    private sealed class NumberingState
    {
        private readonly List<(int Id, ListInfo Info)> _instances = [];

        internal bool Used => _instances.Count > 0;

        internal IReadOnlyList<(int Id, ListInfo Info)> Instances => _instances;

        internal int NewInstance(ListInfo info)
        {
            // Numbering ids start at 1; 0 means "no numbering" in Word.
            var id = _instances.Count + 1;
            _instances.Add((id, info));

            return id;
        }
    }

    /// <summary>
    /// Writes the numbering part: one abstract definition for bullets, one for
    /// numbers, and an instance per list in the document (Section 7.2).
    /// </summary>
    private static void WriteNumbering(MainDocumentPart main, NumberingState state)
    {
        var part = main.AddNewPart<NumberingDefinitionsPart>();
        var numbering = new Numbering();

        numbering.AppendChild(AbstractList(BulletAbstractId, bullet: true, "decimal"));
        numbering.AppendChild(AbstractList(NumberAbstractId, bullet: false, "decimal"));

        // A numbered list with a non-default format gets its own abstract
        // definition, because the format lives on the level rather than on the
        // instance.
        var extra = 2;
        var abstractFor = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var (_, info) in state.Instances)
        {
            if (info.Kind == ListKind.Bullet || info.NumberFormat == "decimal")
            {
                continue;
            }

            if (abstractFor.ContainsKey(info.NumberFormat))
            {
                continue;
            }

            abstractFor[info.NumberFormat] = extra;
            numbering.AppendChild(AbstractList(extra, bullet: false, info.NumberFormat));
            extra++;
        }

        foreach (var (id, info) in state.Instances)
        {
            var abstractId = info.Kind == ListKind.Bullet
                ? BulletAbstractId
                : abstractFor.GetValueOrDefault(info.NumberFormat, NumberAbstractId);

            var instance = new NumberingInstance(new AbstractNumId { Val = abstractId })
            {
                NumberID = id,
            };

            if (info.Start > 1)
            {
                instance.AppendChild(new LevelOverride(
                    new StartOverrideNumberingValue { Val = info.Start })
                {
                    LevelIndex = 0,
                });
            }

            numbering.AppendChild(instance);
        }

        part.Numbering = numbering;
        part.Numbering.Save();
    }

    private static AbstractNum AbstractList(int id, bool bullet, string format)
    {
        var abstractNum = new AbstractNum(new MultiLevelType
        {
            Val = MultiLevelValues.HybridMultilevel,
        })
        {
            AbstractNumberId = id,
            // A unique id Word uses to tell abstract definitions apart.
            Nsid = new Nsid { Val = (0x10000000 + id).ToString("X8", System.Globalization.CultureInfo.InvariantCulture) },
        };

        for (var level = 0; level < ListLevels; level++)
        {
            abstractNum.AppendChild(LevelFor(level, bullet, format));
        }

        return abstractNum;
    }

    private static Level LevelFor(int level, bool bullet, string format)
    {
        // Each level is indented a quarter inch further than the one above,
        // with the text hanging back to the marker.
        var indent = Twips(18 * (level + 1));

        var numberFormat = bullet
            ? NumberFormatValues.Bullet
            : format switch
            {
                "lowerLetter" => NumberFormatValues.LowerLetter,
                "upperLetter" => NumberFormatValues.UpperLetter,
                "lowerRoman" => NumberFormatValues.LowerRoman,
                "upperRoman" => NumberFormatValues.UpperRoman,
                _ => NumberFormatValues.Decimal,
            };

        var text = bullet
            ? (level % 3) switch { 0 => "●", 1 => "○", _ => "■" }
            : $"%{level + 1}.";

        var result = new Level(
            new StartNumberingValue { Val = 1 },
            new NumberingFormat { Val = numberFormat },
            new LevelText { Val = text },
            new LevelJustification { Val = LevelJustificationValues.Left },
            new PreviousParagraphProperties(new Indentation
            {
                Left = indent,
                Hanging = Twips(18),
            }))
        {
            LevelIndex = level,
        };

        if (bullet)
        {
            result.AppendChild(new NumberingSymbolRunProperties(
                new RunFonts { Ascii = "Symbol", HighAnsi = "Symbol", Hint = FontTypeHintValues.Default }));
        }

        return result;
    }

    /// <summary>
    /// The order the schema requires inside a paragraph-properties element.
    /// </summary>
    /// <remarks>
    /// The same sequence rule as run properties, and the same consequence:
    /// spacing before indentation, and both before justification. Written in
    /// the order the code naturally produces them, Word calls the file
    /// unreadable.
    /// </remarks>
    private static readonly Type[] ParagraphPropertyOrder =
    [
        typeof(ParagraphStyleId),
        typeof(KeepNext),
        typeof(KeepLines),
        typeof(PageBreakBefore),
        typeof(WidowControl),
        typeof(NumberingProperties),
        typeof(ParagraphBorders),
        typeof(Shading),
        typeof(Tabs),
        typeof(SpacingBetweenLines),
        typeof(Indentation),
        typeof(ContextualSpacing),
        typeof(Justification),
        typeof(OutlineLevel),
    ];

    private static void AppendParagraphPropertiesInOrder(
        OpenXmlCompositeElement properties,
        List<OpenXmlElement> children)
    {
        foreach (var child in children.OrderBy(c =>
        {
            var index = Array.IndexOf(ParagraphPropertyOrder, c.GetType());

            return index < 0 ? ParagraphPropertyOrder.Length : index;
        }))
        {
            properties.AppendChild(child);
        }
    }

    /// <summary>
    /// The order the schema requires inside a run-properties element.
    /// </summary>
    /// <remarks>
    /// WordprocessingML is a sequence, not a set: <c>w:rPr</c> must hold its
    /// children in exactly this order, and a file with <c>w:sz</c> before
    /// <c>w:b</c> is invalid even though every element in it is legal. Word
    /// refuses such a file with "unreadable content", so the order cannot be
    /// left to the order the code happens to find things in - hence one list
    /// here rather than care at a dozen call sites.
    /// </remarks>
    private static readonly Type[] RunPropertyOrder =
    [
        typeof(RunStyle),
        typeof(RunFonts),
        typeof(Bold),
        typeof(BoldComplexScript),
        typeof(Italic),
        typeof(ItalicComplexScript),
        typeof(Caps),
        typeof(SmallCaps),
        typeof(Strike),
        typeof(DoubleStrike),
        typeof(Color),
        typeof(DocumentFormat.OpenXml.Wordprocessing.Spacing),
        typeof(FontSize),
        typeof(FontSizeComplexScript),
        typeof(Highlight),
        typeof(Underline),
        typeof(Shading),
        typeof(VerticalTextAlignment),
    ];

    /// <summary>Appends run properties in the order the schema wants.</summary>
    private static void AppendInOrder(OpenXmlCompositeElement properties, List<OpenXmlElement> children)
    {
        foreach (var child in children.OrderBy(c => IndexOf(c.GetType())))
        {
            properties.AppendChild(child);
        }
    }

    private static int IndexOf(Type type)
    {
        var index = Array.IndexOf(RunPropertyOrder, type);

        // Anything not listed goes last, which is where the schema puts the
        // elements this writer does not use.
        return index < 0 ? RunPropertyOrder.Length : index;
    }

    /// <summary>A measurement in twips, which is what Word's attributes want.</summary>
    private static string Twips(double points) =>
        TwipsInt(points).ToString("0", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Twips as a number, for the attributes that are typed.</summary>
    private static int TwipsInt(double points) => (int)Math.Round(Units.PointsToTwips(points));

    private static uint TwipsUInt(double points) => (uint)Math.Max(0, Math.Round(Units.PointsToTwips(points)));

    /// <summary>A colour as six hex digits with no hash, which is Word's form.</summary>
    internal static string? HexOf(string? color)
    {
        var normalized = IrNormalizer.NormalizeColor(color);

        return normalized is { Length: 7 } hex && hex[0] == '#' ? hex[1..] : null;
    }

    private static Uri SafeUri(string link)
    {
        // A relationship target has to be a Uri. Something that will not parse
        // is written as a fragment, which keeps the text and the relationship
        // valid rather than failing the save.
        return Uri.TryCreate(link, UriKind.Absolute, out var parsed) ? parsed : new Uri("#", UriKind.Relative);
    }
}
