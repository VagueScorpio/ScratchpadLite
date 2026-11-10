namespace ScratchpadLite.Core.Model;

/// <summary>
/// Compares documents by content (Section 6.3).
/// </summary>
/// <remarks>
/// Records give value equality for their own properties, but a record holding
/// an <see cref="IReadOnlyList{T}"/> compares that list by reference - so two
/// identical documents built separately are never equal. Hence this.
/// </remarks>
public static class IrComparer
{
    /// <summary>
    /// Whether two documents hold the same content, after normalising both.
    /// </summary>
    public static bool Equal(SpDocument a, SpDocument b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);

        return Exactly(IrNormalizer.Normalize(a), IrNormalizer.Normalize(b));
    }

    /// <summary>Whether two documents hold the same content, as given.</summary>
    public static bool Exactly(SpDocument a, SpDocument b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);

        return a.Page == b.Page
               && SameMeta(a.Meta, b.Meta)
               && a.FontsUsed.SetEquals(b.FontsUsed)
               && SameBlocks(a.Blocks, b.Blocks);
    }

    public static bool SameBlocks(IReadOnlyList<Block> a, IReadOnlyList<Block> b)
    {
        if (a.Count != b.Count)
        {
            return false;
        }

        for (var i = 0; i < a.Count; i++)
        {
            if (!SameBlock(a[i], b[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool SameBlock(Block a, Block b) => (a, b) switch
    {
        (Paragraph x, Paragraph y) => SameParagraph(x, y),
        (Table x, Table y) => SameTable(x, y),
        (HorizontalRule, HorizontalRule) => true,
        _ => false,
    };

    private static bool SameParagraph(Paragraph a, Paragraph b) =>
        // The record comparison covers every scalar property. Only the list
        // needs doing by hand, and "with { Inlines = [] }" is the trick that
        // stops the reference comparison spoiling it.
        a with { Inlines = [] } == b with { Inlines = [] }
        && SameInlines(a.Inlines, b.Inlines);

    private static bool SameTable(Table a, Table b)
    {
        if (a.Borders != b.Borders || a.Rows.Count != b.Rows.Count)
        {
            return false;
        }

        if (a.ColWidthsPt is null != (b.ColWidthsPt is null))
        {
            return false;
        }

        if (a.ColWidthsPt is not null && !a.ColWidthsPt.SequenceEqual(b.ColWidthsPt!))
        {
            return false;
        }

        for (var r = 0; r < a.Rows.Count; r++)
        {
            if (a.Rows[r].Count != b.Rows[r].Count)
            {
                return false;
            }

            for (var c = 0; c < a.Rows[r].Count; c++)
            {
                var x = a.Rows[r][c];
                var y = b.Rows[r][c];

                if (x.ColSpan != y.ColSpan
                    || x.RowSpan != y.RowSpan
                    || x.Shading != y.Shading
                    || !SameBlocks(x.Blocks, y.Blocks))
                {
                    return false;
                }
            }
        }

        return true;
    }

    public static bool SameInlines(IReadOnlyList<Inline> a, IReadOnlyList<Inline> b)
    {
        if (a.Count != b.Count)
        {
            return false;
        }

        for (var i = 0; i < a.Count; i++)
        {
            if (!SameInline(a[i], b[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool SameInline(Inline a, Inline b) => (a, b) switch
    {
        (TextRun x, TextRun y) => x == y,
        (LineBreak, LineBreak) => true,

        // Image bytes are the point of an ImageRun and the record would compare
        // the array by reference, so they are compared by content.
        (ImageRun x, ImageRun y) =>
            x with { Data = [] } == y with { Data = [] } && x.Data.AsSpan().SequenceEqual(y.Data),

        _ => false,
    };

    private static bool SameMeta(
        IReadOnlyDictionary<string, string> a,
        IReadOnlyDictionary<string, string> b)
    {
        if (a.Count != b.Count)
        {
            return false;
        }

        foreach (var (key, value) in a)
        {
            if (!b.TryGetValue(key, out var other) || other != value)
            {
                return false;
            }
        }

        return true;
    }
}
