using System.Text.Json.Serialization;

namespace ScratchpadLite.Core.Model;

// The "shipping pallet" (Section 4.1 / 6.3). Plain records, no WPF types, so
// this can be built on a background thread and tested without a window.
// Colors are "#RRGGBB" strings. Sizes are points.

public enum UnderlineKind { None, Single, Double, Dotted, Dashed, Wavy }

public enum VAlign { Normal, Super, Sub }

public enum Align { Left, Center, Right, Justify }

public enum ListKind { Bullet, Number }

public sealed record CharStyle
{
    /// <summary>null = inherit from the paragraph style.</summary>
    public string? Family { get; init; }
    public double? SizePt { get; init; }
    public bool Bold { get; init; }
    public bool Italic { get; init; }
    public UnderlineKind Underline { get; init; }
    public bool Strike { get; init; }
    public VAlign Vertical { get; init; }
    public bool SmallCaps { get; init; }

    /// <summary>Kept for round-trip, not displayed (Section 6.8: WPF has no all-caps).</summary>
    public bool AllCapsPreserved { get; init; }

    /// <summary>Kept for round-trip, not displayed (Section 6.8: WPF has no letter spacing).</summary>
    public double? LetterSpacingPtPreserved { get; init; }

    /// <summary>null = automatic.</summary>
    public string? Color { get; init; }
    public string? Highlight { get; init; }
    public string? Link { get; init; }
}

[JsonDerivedType(typeof(TextRun), "text")]
[JsonDerivedType(typeof(ImageRun), "image")]
[JsonDerivedType(typeof(LineBreak), "break")]
public abstract record Inline;

public sealed record TextRun(string Text, CharStyle Style) : Inline;

public sealed record ImageRun(byte[] Data, string Mime, double WidthPt, double HeightPt, string Alt = "") : Inline;

public sealed record LineBreak : Inline;

public sealed record ListInfo(ListKind Kind, int Level, string NumberFormat = "decimal", int Start = 1);

[JsonDerivedType(typeof(Paragraph), "p")]
[JsonDerivedType(typeof(Table), "table")]
[JsonDerivedType(typeof(HorizontalRule), "hr")]
public abstract record Block;

public sealed record Paragraph : Block
{
    public string StyleName { get; init; } = "Normal";
    public Align Align { get; init; }
    public double IndentLeftPt { get; init; }
    public double IndentRightPt { get; init; }
    public double FirstLinePt { get; init; }
    public double? SpaceBeforePt { get; init; }
    public double? SpaceAfterPt { get; init; }

    /// <summary>A multiple, for example 1.15.</summary>
    public double? LineSpacing { get; init; }

    public ListInfo? List { get; init; }
    public bool PageBreakBefore { get; init; }
    public IReadOnlyList<Inline> Inlines { get; init; } = [];
}

public sealed record Cell(IReadOnlyList<Block> Blocks, int ColSpan = 1, int RowSpan = 1, string? Shading = null);

public sealed record Table(
    IReadOnlyList<IReadOnlyList<Cell>> Rows,
    IReadOnlyList<double>? ColWidthsPt,
    bool Borders = true) : Block;

public sealed record HorizontalRule : Block;

public sealed record PageSetup(
    string Paper = "Letter",
    bool Landscape = false,
    double MarginTopPt = 72,
    double MarginBottomPt = 72,
    double MarginLeftPt = 72,
    double MarginRightPt = 72);

public sealed record SpDocument(
    IReadOnlyList<Block> Blocks,
    IReadOnlyDictionary<string, string> Meta,
    PageSetup Page,

    /// <summary>The fonts the document asks for.</summary>
    /// <remarks>
    /// The converter is not decoration: System.Text.Json writes an
    /// IReadOnlySet happily and will not read one back. See
    /// <see cref="ReadOnlyStringSetConverter"/>.
    /// </remarks>
    [property: JsonConverter(typeof(ReadOnlyStringSetConverter))]
    IReadOnlySet<string> FontsUsed)
{
    public static SpDocument Empty { get; } = new(
        [new Paragraph()],
        new Dictionary<string, string>(),
        new PageSetup(),
        new HashSet<string>());
}
