namespace ScratchpadLite.Core.Fonts.Type1;

/// <summary>
/// Metrics and Windows names from the .afm or .pfm file that sits beside a
/// Type 1 font (Section 6.6.4). These supply the ascent, descent, line gap,
/// weight class, italic angle and the family and style names that the
/// generated OpenType name and OS/2 tables need.
/// </summary>
/// <remarks>Phase 3.</remarks>
public static class AfmPfmReader
{
    /// <summary>The metrics an OpenType head, hhea and OS/2 table need.</summary>
    public sealed record Metrics(
        string FamilyName,
        string StyleName,
        short Ascent,
        short Descent,
        short LineGap,
        ushort WeightClass,
        bool Italic,
        short[] BBox);

    public static Metrics? ReadBeside(string type1Path) =>
        throw new NotImplementedException("Phase 3");
}
