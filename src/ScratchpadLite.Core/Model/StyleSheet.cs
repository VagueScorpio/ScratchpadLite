namespace ScratchpadLite.Core.Model;

/// <summary>One named paragraph style (Section 6.5).</summary>
public sealed record ParagraphStyle
{
    public string Name { get; init; } = "Normal";
    public string? Family { get; init; }
    public double SizePt { get; init; } = 11;
    public bool Bold { get; init; }
    public bool Italic { get; init; }
    public string? Color { get; init; }
    public string? Shading { get; init; }
    public double IndentLeftPt { get; init; }
    public double SpaceBeforePt { get; init; }
    public double SpaceAfterPt { get; init; }
    public double LineSpacing { get; init; } = 1.0;

    /// <summary>1-6 for Heading 1-6, used for DOCX outline levels and Markdown.</summary>
    public int? OutlineLevel { get; init; }
}

/// <summary>
/// The paragraph style sheet (Section 6.5). Shipped as
/// <c>Resources/default-styles.json</c> and copied to <c>data/styles.json</c>
/// on first run so the user can edit it.
/// </summary>
/// <remarks>Phase 1.</remarks>
public sealed class StyleSheet
{
    public static readonly string[] StyleNames =
    [
        "Normal", "Title", "Subtitle",
        "Heading 1", "Heading 2", "Heading 3", "Heading 4", "Heading 5", "Heading 6",
        "Quote", "Code",
    ];

    public static StyleSheet LoadOrDefault(string path) => throw new NotImplementedException("Phase 1");

    public ParagraphStyle this[string name] => throw new NotImplementedException("Phase 1");
}
