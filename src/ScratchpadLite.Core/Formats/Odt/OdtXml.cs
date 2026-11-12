using System.Xml.Linq;

namespace ScratchpadLite.Core.Formats.Odt;

/// <summary>
/// The OpenDocument namespaces and the small helpers both the reader and the
/// writer need (Section 7.2).
/// </summary>
/// <remarks>
/// ODT is namespace-heavy: a single paragraph touches four of them, and an
/// attribute in the wrong namespace is silently ignored rather than rejected -
/// which makes a mistake here show up as a property that quietly does nothing.
/// So every name used anywhere is defined once, here.
/// </remarks>
internal static class OdtXml
{
    internal static readonly XNamespace Office =
        "urn:oasis:names:tc:opendocument:xmlns:office:1.0";

    internal static readonly XNamespace Style =
        "urn:oasis:names:tc:opendocument:xmlns:style:1.0";

    internal static readonly XNamespace Text =
        "urn:oasis:names:tc:opendocument:xmlns:text:1.0";

    internal static readonly XNamespace Table =
        "urn:oasis:names:tc:opendocument:xmlns:table:1.0";

    internal static readonly XNamespace Draw =
        "urn:oasis:names:tc:opendocument:xmlns:drawing:1.0";

    internal static readonly XNamespace Fo =
        "urn:oasis:names:tc:opendocument:xmlns:xsl-fo-compatible:1.0";

    internal static readonly XNamespace Svg =
        "urn:oasis:names:tc:opendocument:xmlns:svg-compatible:1.0";

    internal static readonly XNamespace Xlink = "http://www.w3.org/1999/xlink";

    internal static readonly XNamespace Meta =
        "urn:oasis:names:tc:opendocument:xmlns:meta:1.0";

    internal static readonly XNamespace Manifest =
        "urn:oasis:names:tc:opendocument:xmlns:manifest:1.0";

    internal static readonly XNamespace Dc = "http://purl.org/dc/elements/1.1/";

    internal const string MimeType = "application/vnd.oasis.opendocument.text";

    /// <summary>The namespace declarations every ODT part carries.</summary>
    internal static XAttribute[] Declarations() =>
    [
        new(XNamespace.Xmlns + "office", Office.NamespaceName),
        new(XNamespace.Xmlns + "style", Style.NamespaceName),
        new(XNamespace.Xmlns + "text", Text.NamespaceName),
        new(XNamespace.Xmlns + "table", Table.NamespaceName),
        new(XNamespace.Xmlns + "draw", Draw.NamespaceName),
        new(XNamespace.Xmlns + "fo", Fo.NamespaceName),
        new(XNamespace.Xmlns + "svg", Svg.NamespaceName),
        new(XNamespace.Xmlns + "xlink", Xlink.NamespaceName),
        new(XNamespace.Xmlns + "meta", Meta.NamespaceName),
        new(XNamespace.Xmlns + "dc", Dc.NamespaceName),
    ];

    /// <summary>A length in points, as ODT writes it.</summary>
    internal static string Points(double value) =>
        value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + "pt";

    /// <summary>
    /// Reads an ODT length into points.
    /// </summary>
    /// <remarks>
    /// ODT allows any CSS unit, and real files use all of them: LibreOffice
    /// writes centimetres or inches depending on the locale it was set up in,
    /// and a file from a converter may use millimetres or pixels. Reading only
    /// points would silently ignore most files' measurements.
    /// </remarks>
    internal static double? ToPoints(string? length)
    {
        if (string.IsNullOrWhiteSpace(length))
        {
            return null;
        }

        var text = length.Trim();
        var digits = text.Length;

        while (digits > 0 && !char.IsAsciiDigit(text[digits - 1]) && text[digits - 1] != '.')
        {
            digits--;
        }

        var unit = text[digits..].ToLowerInvariant();

        if (!double.TryParse(
                text.AsSpan(0, digits),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out var value))
        {
            return null;
        }

        return unit switch
        {
            "pt" or "" => value,
            "cm" => value * 72 / 2.54,
            "mm" => value * 72 / 25.4,
            "in" => value * 72,
            "pc" => value * 12,
            "px" => value * 72 / 96,
            _ => null,
        };
    }

    /// <summary>A colour as ODT writes it, or null.</summary>
    internal static string? Color(string? color)
    {
        var normalized = Model.IrNormalizer.NormalizeColor(color);

        return normalized is { Length: 7 } ? normalized.ToLowerInvariant() : null;
    }

    /// <summary>An ODT colour as an IR colour, or null for "transparent".</summary>
    internal static string? FromColor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Equals("transparent", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return Model.IrNormalizer.NormalizeColor(value);
    }
}
