namespace ScratchpadLite.Core.Fonts;

/// <summary>
/// Reads the family name, face names, version, license text and the OS/2
/// fsType embedding permission straight from raw sfnt bytes (Section 6.6).
/// Used by the font index, the Font Report, and the FONT_LICENSES.txt
/// generator, so it must never need WPF.
/// </summary>
/// <remarks>Phase 2.</remarks>
public static class FontInspector
{
    /// <summary>What the name and OS/2 tables say about one font file.</summary>
    public sealed record Info(
        IReadOnlyList<string> Families,
        string? FullName,
        string? Version,
        string? LicenseDescription,
        string? LicenseUrl,
        ushort FsType);

    public static Info Read(Stream sfnt) => throw new NotImplementedException("Phase 2");
}
