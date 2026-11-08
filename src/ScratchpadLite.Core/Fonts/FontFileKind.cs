namespace ScratchpadLite.Core.Fonts;

/// <summary>
/// How FontScanner classifies one file it finds in the font folder
/// (Section 6.6.3). Direct kinds load straight into WPF; converted kinds go
/// through a decoder once and are cached; unsupported kinds are listed in
/// the Font Report with a reason.
/// </summary>
/// <remarks>Phase 2.</remarks>
public enum FontFileKind
{
    /// <summary>.ttf, .otf, .ttc, .otc - WPF loads these directly.</summary>
    Direct,

    /// <summary>.woff - inflate each table with ZLibStream.</summary>
    Woff,

    /// <summary>.woff2 - Brotli plus the transformed glyf and loca tables.</summary>
    Woff2,

    /// <summary>.pfb, .pfa - Adobe Type 1, converted to OpenType CFF.</summary>
    Type1,

    /// <summary>.dfont, AppleDouble underscore files, data-fork suitcases.</summary>
    MacResource,

    /// <summary>.eot - uncompressed or XOR only; compressed ones are unsupported.</summary>
    Eot,

    /// <summary>.t42 - a PostScript wrapper around an sfnt.</summary>
    Type42,

    /// <summary>.zip - opened through ZipFontSource and scanned inside.</summary>
    ZipArchive,

    /// <summary>Bitmap, SVG, Multiple Master and CID fonts (Section 13).</summary>
    Unsupported,
}
