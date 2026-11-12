using ScratchpadLite.Core.Model;

namespace ScratchpadLite.Core.Formats;

/// <summary>
/// What a document actually uses, so the save warning can name only what
/// would really be lost (Section 7.1).
/// </summary>
/// <remarks>
/// The warning has to be about this document, not about the format in general.
/// Telling someone that saving as plain text will lose their tables when the
/// document has no tables trains them to dismiss the warning, and then they
/// dismiss the one that mattered.
/// </remarks>
public static class IrFeatures
{
    public static Capabilities Used(SpDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var used = Capabilities.None;

        Scan(document.Blocks, ref used);

        if (document.Page != new PageSetup())
        {
            used |= Capabilities.PageSetup;
        }

        return used;
    }

    private static void Scan(IReadOnlyList<Block> blocks, ref Capabilities used)
    {
        foreach (var block in blocks)
        {
            switch (block)
            {
                case Paragraph paragraph:
                    Scan(paragraph, ref used);
                    break;

                case Table table:
                    used |= Capabilities.Tables;

                    foreach (var row in table.Rows)
                    {
                        foreach (var cell in row)
                        {
                            if (cell.Shading is not null)
                            {
                                used |= Capabilities.Colors;
                            }

                            Scan(cell.Blocks, ref used);
                        }
                    }

                    break;
            }
        }
    }

    private static void Scan(Paragraph paragraph, ref Capabilities used)
    {
        if (paragraph.List is not null)
        {
            used |= Capabilities.Lists;
        }

        // Every style but Normal is a heading or a named look that only a
        // format with styles can carry.
        if (paragraph.StyleName is not ("Normal" or ""))
        {
            used |= Capabilities.Headings;
        }

        foreach (var inline in paragraph.Inlines)
        {
            switch (inline)
            {
                case TextRun run:
                    Scan(run.Style, ref used);
                    break;

                case ImageRun:
                    used |= Capabilities.Images;
                    break;
            }
        }
    }

    private static void Scan(CharStyle style, ref Capabilities used)
    {
        if (style.Bold || style.Italic || style.Strike
            || style.Vertical != VAlign.Normal
            || style.SmallCaps
            || style.AllCapsPreserved)
        {
            used |= Capabilities.CharFormat;
        }

        if (style.Underline != UnderlineKind.None)
        {
            used |= Capabilities.Underline;
        }

        if (style.Family is not null || style.SizePt is not null
            || style.LetterSpacingPtPreserved is not null)
        {
            used |= Capabilities.Fonts;
        }

        if (style.Color is not null || style.Highlight is not null)
        {
            used |= Capabilities.Colors;
        }

        if (style.Link is not null)
        {
            used |= Capabilities.Links;
        }
    }
}
