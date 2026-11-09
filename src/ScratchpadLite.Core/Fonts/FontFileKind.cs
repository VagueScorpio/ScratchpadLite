using System.Buffers.Binary;
using System.Text;

namespace ScratchpadLite.Core.Fonts;

/// <summary>
/// How FontScanner classifies one file it finds in the font folder
/// (Section 6.6.3). Direct kinds load straight into WPF; converted kinds go
/// through a decoder once and are cached; unsupported kinds are listed in
/// the Font Report with a reason.
/// </summary>
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

    /// <summary>.pfm, .afm - metrics for a Type 1 font, not a font on their own.</summary>
    Type1Metrics,

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

    /// <summary>A licence or readme beside the fonts - read for FONT_LICENSES.txt.</summary>
    LicenseText,

    /// <summary>Not a font and not interesting. Left out of the report entirely.</summary>
    Ignored,
}

/// <summary>
/// Decides what a file is (Section 6.6.2, step 2).
/// </summary>
/// <remarks>
/// Magic bytes first, extension second, because extensions lie - a .ttf that
/// is really a WOFF is common in font bundles downloaded from the web, and so
/// is an .otf containing TrueType outlines. The extension is only consulted
/// for the formats that have no usable signature, and for deciding that
/// something is deliberately unsupported rather than merely unrecognised.
/// </remarks>
public static class FontTypeDetector
{
    /// <summary>Extensions that are refused by name, with the reason for the report.</summary>
    private static readonly Dictionary<string, string> RefusedByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".fon"] = "Bitmap fonts are not supported. WPF needs outline fonts.",
        [".fnt"] = "Bitmap fonts are not supported. WPF needs outline fonts.",
        [".bdf"] = "Bitmap fonts are not supported. WPF needs outline fonts.",
        [".pcf"] = "Bitmap fonts are not supported. WPF needs outline fonts.",
        [".otb"] = "Bitmap fonts are not supported. WPF needs outline fonts.",
        [".svg"] = "SVG fonts are not supported. Look for a .ttf, .otf, or .woff version.",
        [".svgz"] = "SVG fonts are not supported. Look for a .ttf, .otf, or .woff version.",
        [".pfr"] = "Portable Font Resource files are not supported.",
        [".cff"] = "A bare CFF file is not a font on its own. Look for an .otf version.",
        [".vfb"] = "This is a font design file, not a finished font. Export it as .otf from the font editor.",
        [".vfc"] = "This is a font design file, not a finished font. Export it as .otf from the font editor.",
        [".vfj"] = "This is a font design file, not a finished font. Export it as .otf from the font editor.",
        [".glyphs"] = "This is a font design file, not a finished font. Export it as .otf from the font editor.",
        [".glyphspackage"] = "This is a font design file, not a finished font. Export it as .otf from the font editor.",
        [".ufo"] = "This is a font design file, not a finished font. Export it as .otf from the font editor.",
        [".sfd"] = "This is a font design file, not a finished font. Export it as .otf from the font editor.",
    };

    /// <summary>The message for a file refused by its extension, or null.</summary>
    public static string? RefusalFor(string path) =>
        RefusedByExtension.GetValueOrDefault(Path.GetExtension(path));

    /// <summary>
    /// Classifies a file from its first bytes and its name. The header may be
    /// short; anything missing simply does not match.
    /// </summary>
    public static FontFileKind Classify(string path, ReadOnlySpan<byte> header)
    {
        var extension = Path.GetExtension(path);

        if (RefusedByExtension.ContainsKey(extension))
        {
            return FontFileKind.Unsupported;
        }

        var byMagic = FromMagic(header);
        if (byMagic is not null)
        {
            return byMagic.Value;
        }

        // No signature matched. These formats are recognised by name because
        // they have none: AFM and PFM are plain data, and an AppleDouble file
        // is identified by the "._" prefix Windows copies leave behind.
        var name = Path.GetFileName(path);

        if (extension.Equals(".afm", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".pfm", StringComparison.OrdinalIgnoreCase))
        {
            return FontFileKind.Type1Metrics;
        }

        if (name.StartsWith("._", StringComparison.Ordinal))
        {
            return FontFileKind.MacResource;
        }

        if (extension.Equals(".dfont", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".suit", StringComparison.OrdinalIgnoreCase))
        {
            return FontFileKind.MacResource;
        }

        if (extension.Equals(".eot", StringComparison.OrdinalIgnoreCase))
        {
            return FontFileKind.Eot;
        }

        if (extension.Equals(".t42", StringComparison.OrdinalIgnoreCase))
        {
            return FontFileKind.Type42;
        }

        if (IsLicenseText(name))
        {
            return FontFileKind.LicenseText;
        }

        return FontFileKind.Ignored;
    }

    /// <summary>How many bytes <see cref="Classify"/> needs to see.</summary>
    public const int HeaderBytes = 64;

    private static FontFileKind? FromMagic(ReadOnlySpan<byte> header)
    {
        if (header.Length >= 4)
        {
            var tag = BinaryPrimitives.ReadUInt32BigEndian(header);

            switch (tag)
            {
                case Sfnt.TrueTypeFlavor:
                case Sfnt.CffFlavor:
                case Sfnt.CollectionTag:
                case 0x74727565:                    // "true" - an old Mac TrueType
                case 0x00020000:                    // an early TrueType version
                    return FontFileKind.Direct;

                case WoffDecoder.Signature:
                    return FontFileKind.Woff;

                case Woff2Decoder.Signature:
                    return FontFileKind.Woff2;
            }

            // "PK\x03\x04" and the empty-archive and spanned variants.
            if (header[0] == 'P' && header[1] == 'K' && header[2] <= 8)
            {
                return FontFileKind.ZipArchive;
            }
        }

        if (header.Length >= 2 && header[0] == 0x80 && header[1] == 0x01)
        {
            return FontFileKind.Type1;            // PFB segment header
        }

        if (header.Length >= 16)
        {
            var text = Encoding.Latin1.GetString(header);

            if (text.StartsWith("%!PS-TrueTypeFont", StringComparison.Ordinal))
            {
                return FontFileKind.Type42;
            }

            if (text.StartsWith("%!PS-AdobeFont", StringComparison.Ordinal)
                || text.StartsWith("%!FontType1", StringComparison.Ordinal)
                || text.StartsWith("%!PS-Adobe-3.0 Resource-Font", StringComparison.Ordinal))
            {
                return FontFileKind.Type1;        // PFA, the clear-text form
            }

            if (text.StartsWith("StartFontMetrics", StringComparison.Ordinal))
            {
                return FontFileKind.Type1Metrics;
            }

            // EOT has no signature at the start: bytes 34-35 hold a fixed magic
            // number, little-endian, after two length fields and a version.
            if (header.Length >= 36
                && BinaryPrimitives.ReadUInt16LittleEndian(header[34..]) == 0x504C)
            {
                return FontFileKind.Eot;
            }
        }

        return null;
    }

    private static bool IsLicenseText(string name) =>
        name.StartsWith("OFL", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("LICENSE", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("LICENCE", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("COPYING", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("README", StringComparison.OrdinalIgnoreCase);
}
