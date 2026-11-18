using ScratchpadLite.Core.Model;

namespace ScratchpadLite.Core.Formats;

/// <summary>
/// Facts every format reader needs and none of them owns.
/// </summary>
/// <remarks>
/// <c>DocxReader</c> and <c>OdtReader</c> held byte-identical private copies of
/// both of these, so a paper size added to one silently did not exist in the
/// other. Markdown, RTF and TXT carry neither a page size nor a font list, so
/// two copies was the whole set.
/// </remarks>
internal static class PageFacts
{
    /// <summary>
    /// The paper name for a page of these dimensions, or <c>"Letter"</c>.
    /// </summary>
    /// <remarks>
    /// Three points of tolerance, because a file stores its size in its own
    /// unit — twips in DOCX, centimetres in ODT — and the trip to points does
    /// not land exactly.
    /// <para>
    /// This knows one paper the rest of the app does not: <c>Printing.Papers</c>
    /// and the Page Setup dialog have Letter, Legal, A4 and A5, so a file that
    /// really is Tabloid reads as Tabloid and prints at Letter. Left as it was
    /// — dropping it or teaching the printer about it both change behaviour.
    /// </para>
    /// </remarks>
    internal static string PaperFor(double shortSide, double longSide)
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

    /// <summary>
    /// Adds every font family named by a run in these blocks, and in any table
    /// cell inside them, to <paramref name="fonts"/>.
    /// </summary>
    /// <remarks>
    /// Fills the caller's set rather than returning one, because the caller
    /// owns the comparer: both readers pass
    /// <see cref="StringComparer.OrdinalIgnoreCase"/>, since a document saying
    /// "Calibri" in one run and "calibri" in the next uses one font.
    /// </remarks>
    internal static void CollectFonts(IReadOnlyList<Block> blocks, HashSet<string> fonts)
    {
        foreach (var block in blocks)
        {
            switch (block)
            {
                case Paragraph paragraph:
                    foreach (var inline in paragraph.Inlines)
                    {
                        if (inline is TextRun { Style.Family: { } family })
                        {
                            fonts.Add(family);
                        }
                    }

                    break;

                case Table table:
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
}
