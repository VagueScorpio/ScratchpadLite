using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using ScratchpadLite.Core.Model;
using ScratchpadLite.Core.Platform;
using IrBlock = ScratchpadLite.Core.Model.Block;
using IrParagraph = ScratchpadLite.Core.Model.Paragraph;
using IrTable = ScratchpadLite.Core.Model.Table;
using MdList = Markdig.Syntax.ListBlock;
using MdParagraph = Markdig.Syntax.ParagraphBlock;
using MdTable = Markdig.Extensions.Tables.Table;

namespace ScratchpadLite.Core.Formats.Markdown;

/// <summary>
/// Reads Markdown with Markdig and walks its syntax tree into the IR
/// (Section 7.2).
/// </summary>
/// <remarks>
/// Markdig does the parsing, which is the right division: Markdown's rules are
/// full of corner cases - how many spaces make a nested list, what closes an
/// emphasis run - and a hand-written parser would get them subtly wrong in
/// ways that only show up on someone else's file.
/// <para>
/// Images are read from paths relative to the file's own folder. Web images
/// are <b>not</b> downloaded: a document should not be able to make the app
/// fetch a URL just by being opened, so a remote image becomes its alt text
/// and the warning bar says so (Section 7.2).
/// </para>
/// </remarks>
internal static class MarkdownReader
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();

    internal static ReadResult Read(Stream input, string sourcePath, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(input);

        using var reader = new StreamReader(input, detectEncodingFromByteOrderMarks: true);
        var text = reader.ReadToEnd();

        Markdig.Syntax.MarkdownDocument syntax;

        try
        {
            syntax = Markdig.Markdown.Parse(text, Pipeline);
        }
        catch (ArgumentException ex)
        {
            // Markdig has a nesting limit and throws ArgumentException when a
            // file exceeds it - which is the right thing to do, since the
            // alternative is a stack overflow that takes the process with it.
            // An opened file is untrusted (Section 7.5), so this has to come
            // out as a bad file rather than as a bad argument: an
            // ArgumentException would reach the user as "could not be opened"
            // with no reason given.
            throw new InvalidDataException(
                "this Markdown file nests lists or quotations too deeply to open.",
                ex);
        }
        var context = new Context(
            Path.GetDirectoryName(Path.GetFullPath(sourcePath)) ?? "",
            [],
            ct);

        var blocks = ReadBlocks(syntax, context, listLevel: -1);

        if (blocks.Count == 0)
        {
            blocks = [new IrParagraph()];
        }

        return new ReadResult(
            new SpDocument(blocks, new Dictionary<string, string>(), new PageSetup(), new HashSet<string>()),
            context.Warnings);
    }

    private sealed record Context(string Folder, List<string> Warnings, CancellationToken Ct);

    private static List<IrBlock> ReadBlocks(ContainerBlock container, Context context, int listLevel)
    {
        var blocks = new List<IrBlock>();

        foreach (var block in container)
        {
            context.Ct.ThrowIfCancellationRequested();

            switch (block)
            {
                case HeadingBlock heading:
                    blocks.Add(new IrParagraph
                    {
                        StyleName = StyleForHeading(heading.Level),
                        Inlines = ReadInlines(heading.Inline, context, new CharStyle()),
                    });

                    break;

                case MdParagraph paragraph:
                    blocks.Add(new IrParagraph
                    {
                        Inlines = ReadInlines(paragraph.Inline, context, new CharStyle()),
                    });

                    break;

                case MdList list:
                    blocks.AddRange(ReadList(list, context, listLevel + 1));
                    break;

                case QuoteBlock quote:
                {
                    // A block quote's contents are ordinary blocks; the Quote
                    // style is what carries the quoting.
                    foreach (var inner in ReadBlocks(quote, context, listLevel))
                    {
                        blocks.Add(inner is IrParagraph p ? p with { StyleName = "Quote" } : inner);
                    }

                    break;
                }

                case CodeBlock code:
                    blocks.AddRange(ReadCode(code));
                    break;

                case MdTable table:
                    blocks.Add(ReadTable(table, context));
                    break;

                case ThematicBreakBlock:
                    blocks.Add(new HorizontalRule());
                    break;

                case HtmlBlock:
                    // Raw HTML in a Markdown file. There is no sensible IR for
                    // it, and rendering it would mean embedding a browser.
                    Warn(context, "Some raw HTML in this file was left out.");
                    break;

                case LinkReferenceDefinitionGroup:
                    break;      // link definitions, already resolved by Markdig

                case ContainerBlock nested:
                    blocks.AddRange(ReadBlocks(nested, context, listLevel));
                    break;
            }
        }

        return blocks;
    }

    private static string StyleForHeading(int level) => level switch
    {
        <= 1 => "Heading 1",
        2 => "Heading 2",
        3 => "Heading 3",
        4 => "Heading 4",
        5 => "Heading 5",
        _ => "Heading 6",
    };

    private static List<IrBlock> ReadList(MdList list, Context context, int level)
    {
        var blocks = new List<IrBlock>();

        var info = new ListInfo(
            list.IsOrdered ? ListKind.Number : ListKind.Bullet,
            Math.Clamp(level, 0, 8),
            "decimal",
            StartOf(list));

        foreach (var item in list.OfType<ListItemBlock>())
        {
            var first = true;

            foreach (var inner in ReadBlocks(item, context, level))
            {
                if (inner is IrParagraph paragraph && paragraph.List is null)
                {
                    // Only the item's first paragraph carries the marker; the
                    // rest is a continuation of the same item.
                    blocks.Add(paragraph with
                    {
                        List = first ? info : info with { Start = 1 },
                    });

                    first = false;
                    continue;
                }

                blocks.Add(inner);
            }
        }

        return blocks;
    }

    private static int StartOf(MdList list) =>
        list.IsOrdered && int.TryParse(list.OrderedStart, out var start) && start > 0 ? start : 1;

    /// <summary>
    /// A fenced or indented code block: one Code paragraph per line, so the
    /// lines stay separate.
    /// </summary>
    private static List<IrBlock> ReadCode(CodeBlock code)
    {
        var blocks = new List<IrBlock>();

        foreach (var line in code.Lines.Lines)
        {
            var text = line.Slice.ToString();

            blocks.Add(new IrParagraph
            {
                StyleName = "Code",
                Inlines = text.Length == 0 ? [] : [new TextRun(text, new CharStyle())],
            });
        }

        // An empty fence still has the StringLine array padded out, so trailing
        // blanks are dropped rather than becoming empty paragraphs.
        while (blocks.Count > 0 && blocks[^1] is IrParagraph { Inlines.Count: 0 })
        {
            blocks.RemoveAt(blocks.Count - 1);
        }

        return blocks;
    }

    private static IrTable ReadTable(MdTable table, Context context)
    {
        var rows = new List<IReadOnlyList<Cell>>();

        foreach (var row in table.OfType<TableRow>())
        {
            var cells = new List<Cell>();

            foreach (var cell in row.OfType<TableCell>())
            {
                cells.Add(new Cell(ReadBlocks(cell, context, listLevel: -1)));
            }

            rows.Add(cells);
        }

        return new IrTable(rows, ColWidthsPt: null);
    }

    private static List<Model.Inline> ReadInlines(
        ContainerInline? container,
        Context context,
        CharStyle style)
    {
        var inlines = new List<Model.Inline>();

        if (container is null)
        {
            return inlines;
        }

        foreach (var inline in container)
        {
            switch (inline)
            {
                case LiteralInline literal:
                    if (literal.Content.Length > 0)
                    {
                        inlines.Add(new TextRun(literal.Content.ToString(), style));
                    }

                    break;

                case EmphasisInline emphasis:
                    inlines.AddRange(ReadInlines(emphasis, context, Apply(style, emphasis)));
                    break;

                case CodeInline code:
                    inlines.Add(new TextRun(
                        code.Content,
                        style with { Family = "Consolas" }));

                    break;

                case LinkInline { IsImage: true } image:
                    ReadImage(image, context, style, inlines);
                    break;

                case LinkInline link:
                    inlines.AddRange(ReadInlines(
                        link,
                        context,
                        style with { Link = link.Url ?? "" }));

                    break;

                case AutolinkInline autolink:
                    inlines.Add(new TextRun(autolink.Url, style with { Link = autolink.Url }));
                    break;

                case LineBreakInline { IsHard: true }:
                    inlines.Add(new Model.LineBreak());
                    break;

                case LineBreakInline:
                    // A soft wrap in the source is a space, not a break.
                    inlines.Add(new TextRun(" ", style));
                    break;

                case HtmlInline html:
                    Warn(context, "Some raw HTML in this file was left out.");
                    _ = html;
                    break;

                case ContainerInline nested:
                    inlines.AddRange(ReadInlines(nested, context, style));
                    break;
            }
        }

        return inlines;
    }

    private static CharStyle Apply(CharStyle style, EmphasisInline emphasis) =>
        emphasis.DelimiterChar switch
        {
            '~' => style with { Strike = true },
            _ => emphasis.DelimiterCount >= 2
                ? style with { Bold = true }
                : style with { Italic = true },
        };

    /// <summary>
    /// An image. Local paths are read from the file's folder; a web image is
    /// not fetched (Section 7.2).
    /// </summary>
    private static void ReadImage(
        LinkInline image,
        Context context,
        CharStyle style,
        List<Model.Inline> inlines)
    {
        var url = image.Url ?? "";
        var alt = string.Concat(image.Descendants<LiteralInline>().Select(l => l.Content.ToString()));

        if (url.Length == 0)
        {
            return;
        }

        if (Uri.TryCreate(url, UriKind.Absolute, out var absolute)
            && absolute.Scheme is "http" or "https")
        {
            // Not downloaded. Opening a document must not be able to make the
            // app fetch a URL.
            Warn(context, "Images from the web were not loaded. Their descriptions are shown instead.");
            inlines.Add(new TextRun(alt.Length > 0 ? alt : url, style));

            return;
        }

        var path = ResolveLocal(url, context.Folder);

        if (path is null || !File.Exists(path))
        {
            Warn(context, "Some images could not be found next to the file.");
            inlines.Add(new TextRun(alt.Length > 0 ? alt : "[image]", style));

            return;
        }

        try
        {
            Limits.CheckFileSize(path, Limits.MaxFontFileBytes);

            var bytes = File.ReadAllBytes(path);

            // Markdown records no size, so the image goes in at its natural
            // size and the editor can be used to change it.
            inlines.Add(new ImageRun(bytes, MimeFor(path), 0, 0, alt));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FileTooLargeException)
        {
            Log.Warn($"an image beside a Markdown file could not be read: {ex.Message}");
            Warn(context, "Some images could not be read.");
            inlines.Add(new TextRun(alt.Length > 0 ? alt : "[image]", style));
        }
    }

    /// <summary>
    /// A relative image path, resolved inside the document's folder.
    /// </summary>
    /// <remarks>
    /// Refuses anything that escapes that folder. A Markdown file is untrusted
    /// input (Section 7.5), and <c>![](../../../Windows/System32/config/SAM)</c>
    /// should not make the app read that file.
    /// </remarks>
    internal static string? ResolveLocal(string url, string folder)
    {
        if (folder.Length == 0)
        {
            return null;
        }

        var cleaned = Uri.UnescapeDataString(url.Split('#')[0].Split('?')[0]);

        if (cleaned.Length == 0 || Path.IsPathRooted(cleaned))
        {
            return null;
        }

        try
        {
            var full = Path.GetFullPath(Path.Combine(folder, cleaned));
            var root = Path.GetFullPath(folder);

            return full.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? full : null;
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException)
        {
            return null;
        }
    }

    internal static string MimeFor(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".bmp" => "image/bmp",
        ".tif" or ".tiff" => "image/tiff",
        ".webp" => "image/webp",
        _ => "image/png",
    };

    private static void Warn(Context context, string message)
    {
        if (!context.Warnings.Contains(message, StringComparer.Ordinal))
        {
            context.Warnings.Add(message);
        }
    }
}
