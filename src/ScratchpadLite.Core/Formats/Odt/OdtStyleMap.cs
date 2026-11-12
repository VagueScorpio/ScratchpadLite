using System.Xml.Linq;
using ScratchpadLite.Core.Model;

namespace ScratchpadLite.Core.Formats.Odt;

/// <summary>
/// The named styles an ODT carries, and the mapping between their names and
/// the app's own style sheet (Section 7.2).
/// </summary>
/// <remarks>
/// ODT's convention is "Heading_20_1" - a space encoded as <c>_20_</c>,
/// because a style name is also an XML identifier. Translating that both ways
/// is most of what this class does; the rest is the named styles, the page
/// layout and the list definitions that <see cref="OdtWriter"/> refers to by
/// name.
/// </remarks>
internal static class OdtStyleMap
{
    internal const string RuleStyleName = "SpHorizontalRule";

    /// <summary>
    /// The ODT style name for one of the app's styles.
    /// </summary>
    internal static string StyleNameFor(string? styleName) =>
        Encode(string.IsNullOrWhiteSpace(styleName) ? StyleSheet.NormalStyleName : styleName);

    /// <summary>
    /// The app's style name for an ODT style name, or null when it is not one
    /// of ours.
    /// </summary>
    internal static string? AppStyleFor(string? odtName, StyleSheet styles)
    {
        if (string.IsNullOrWhiteSpace(odtName))
        {
            return null;
        }

        var decoded = Decode(odtName);

        if (styles.Contains(decoded))
        {
            return decoded;
        }

        // LibreOffice and Word write "Heading 1" and "heading 1" and
        // "Heading_20_1" at different times, so the comparison is loose.
        foreach (var known in StyleSheet.StyleNames)
        {
            if (string.Equals(decoded, known, StringComparison.OrdinalIgnoreCase)
                || string.Equals(
                    decoded.Replace(" ", "", StringComparison.Ordinal),
                    known.Replace(" ", "", StringComparison.Ordinal),
                    StringComparison.OrdinalIgnoreCase))
            {
                return known;
            }
        }

        return null;
    }

    /// <summary>A style name as ODT spells it: spaces become <c>_20_</c>.</summary>
    internal static string Encode(string name) =>
        name.Replace(" ", "_20_", StringComparison.Ordinal);

    /// <summary>An ODT style name with its escapes undone.</summary>
    internal static string Decode(string name) =>
        name.Replace("_20_", " ", StringComparison.Ordinal);

    /// <summary>
    /// A list definition with one marker, for all nine levels.
    /// </summary>
    /// <remarks>
    /// The numbering format belongs to the definition rather than to the list
    /// that uses it, which is why the writer declares one of these per distinct
    /// marker instead of one for numbers and one for bullets.
    /// </remarks>
    internal static XElement ListStyle(string name, ListKind kind, string numberFormat)
    {
        var bullet = kind == ListKind.Bullet;

        var style = new XElement(
            OdtXml.Text + "list-style",
            new XAttribute(OdtXml.Style + "name", name));

        for (var level = 1; level <= 9; level++)
        {
            var properties = new XElement(
                OdtXml.Style + "list-level-properties",
                new XAttribute(OdtXml.Text + "list-level-position-and-space-mode", "label-alignment"),
                new XElement(
                    OdtXml.Style + "list-level-label-alignment",
                    new XAttribute(OdtXml.Text + "label-followed-by", "listtab"),
                    new XAttribute(OdtXml.Fo + "margin-left", OdtXml.Points(18 * level)),
                    new XAttribute(OdtXml.Fo + "text-indent", OdtXml.Points(-18))));

            if (bullet)
            {
                style.Add(new XElement(
                    OdtXml.Text + "list-level-style-bullet",
                    new XAttribute(OdtXml.Text + "level", level),
                    new XAttribute(
                        OdtXml.Text + "bullet-char",
                        ((level - 1) % 3) switch { 0 => "•", 1 => "◦", _ => "▪" }),
                    properties));
            }
            else
            {
                style.Add(new XElement(
                    OdtXml.Text + "list-level-style-number",
                    new XAttribute(OdtXml.Text + "level", level),
                    new XAttribute(OdtXml.Style + "num-suffix", "."),
                    new XAttribute(OdtXml.Style + "num-format", NumFormatFor(numberFormat)),
                    properties));
            }
        }

        return style;
    }

    /// <summary>
    /// The IR's numbering format as ODT spells it. ODT names a format by an
    /// example of its first value, so "a" means lower-case letters.
    /// </summary>
    internal static string NumFormatFor(string? numberFormat) => numberFormat switch
    {
        "lowerLetter" => "a",
        "upperLetter" => "A",
        "lowerRoman" => "i",
        "upperRoman" => "I",
        _ => "1",
    };

    /// <summary>The IR's name for an ODT numbering format.</summary>
    internal static string NumberFormatFrom(string? numFormat) => numFormat switch
    {
        "a" => "lowerLetter",
        "A" => "upperLetter",
        "i" => "lowerRoman",
        "I" => "upperRoman",
        _ => "decimal",
    };

    /// <summary>
    /// The font faces the document refers to. ODT wants them declared.
    /// </summary>
    internal static XElement FontFaceDeclarations() => new(
        OdtXml.Office + "font-face-decls",
        new XElement(
            OdtXml.Style + "font-face",
            new XAttribute(OdtXml.Style + "name", "Calibri"),
            new XAttribute(OdtXml.Svg + "font-family", "Calibri")),
        new XElement(
            OdtXml.Style + "font-face",
            new XAttribute(OdtXml.Style + "name", "Consolas"),
            new XAttribute(OdtXml.Svg + "font-family", "Consolas")));

    /// <summary>The app's style sheet as ODT named styles.</summary>
    internal static XElement NamedStyles(StyleSheet styles)
    {
        var element = new XElement(OdtXml.Office + "styles");

        foreach (var style in styles.All)
        {
            element.Add(NamedStyle(style, styles));
        }

        // The paragraph style a horizontal rule uses.
        element.Add(new XElement(
            OdtXml.Style + "style",
            new XAttribute(OdtXml.Style + "name", RuleStyleName),
            new XAttribute(OdtXml.Style + "family", "paragraph"),
            new XAttribute(OdtXml.Style + "parent-style-name", StyleNameFor(StyleSheet.NormalStyleName)),
            new XElement(
                OdtXml.Style + "paragraph-properties",
                new XAttribute(OdtXml.Fo + "border-bottom", "0.5pt solid #808080"),
                new XAttribute(OdtXml.Fo + "padding-bottom", "0.04in"),
                new XAttribute(OdtXml.Fo + "margin-bottom", "0.08in"))));

        return element;
    }

    private static XElement NamedStyle(ParagraphStyle style, StyleSheet styles)
    {
        var textProperties = new XElement(
            OdtXml.Style + "text-properties",
            new XAttribute(OdtXml.Fo + "font-size", OdtXml.Points(style.SizePt)));

        if (styles.FamilyFor(style) is { } family)
        {
            textProperties.Add(new XAttribute(OdtXml.Style + "font-name", family));
            textProperties.Add(new XAttribute(OdtXml.Fo + "font-family", family));
        }

        if (style.Bold)
        {
            textProperties.Add(new XAttribute(OdtXml.Fo + "font-weight", "bold"));
        }

        if (style.Italic)
        {
            textProperties.Add(new XAttribute(OdtXml.Fo + "font-style", "italic"));
        }

        if (OdtXml.Color(style.Color) is { } color)
        {
            textProperties.Add(new XAttribute(OdtXml.Fo + "color", color));
        }

        if (OdtXml.Color(style.Shading) is { } shading)
        {
            textProperties.Add(new XAttribute(OdtXml.Fo + "background-color", shading));
        }

        var paragraphProperties = new XElement(
            OdtXml.Style + "paragraph-properties",
            new XAttribute(OdtXml.Fo + "margin-top", OdtXml.Points(style.SpaceBeforePt)),
            new XAttribute(OdtXml.Fo + "margin-bottom", OdtXml.Points(style.SpaceAfterPt)),
            new XAttribute(
                OdtXml.Fo + "line-height",
                (style.LineSpacing * 100).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "%"));

        if (style.IndentLeftPt != 0)
        {
            paragraphProperties.Add(new XAttribute(OdtXml.Fo + "margin-left", OdtXml.Points(style.IndentLeftPt)));
        }

        var result = new XElement(
            OdtXml.Style + "style",
            new XAttribute(OdtXml.Style + "name", Encode(style.Name)),
            new XAttribute(OdtXml.Style + "family", "paragraph"),
            paragraphProperties,
            textProperties);

        if (style.OutlineLevel is { } outline)
        {
            result.Add(new XAttribute(OdtXml.Style + "default-outline-level", outline));
        }

        if (style.Name != StyleSheet.NormalStyleName)
        {
            result.Add(new XAttribute(
                OdtXml.Style + "parent-style-name",
                Encode(StyleSheet.NormalStyleName)));
        }

        return result;
    }

    /// <summary>The page layout, from the IR's page setup.</summary>
    internal static XElement PageLayout(PageSetup page)
    {
        var width = Docx.DocxWriter.PaperWidthPt(page.Paper);
        var height = Docx.DocxWriter.PaperHeightPt(page.Paper);

        return new XElement(
            OdtXml.Office + "automatic-styles",
            new XElement(
                OdtXml.Style + "page-layout",
                new XAttribute(OdtXml.Style + "name", "SpPage"),
                new XElement(
                    OdtXml.Style + "page-layout-properties",
                    new XAttribute(OdtXml.Fo + "page-width", OdtXml.Points(page.Landscape ? height : width)),
                    new XAttribute(OdtXml.Fo + "page-height", OdtXml.Points(page.Landscape ? width : height)),
                    new XAttribute(
                        OdtXml.Style + "print-orientation",
                        page.Landscape ? "landscape" : "portrait"),
                    new XAttribute(OdtXml.Fo + "margin-top", OdtXml.Points(page.MarginTopPt)),
                    new XAttribute(OdtXml.Fo + "margin-bottom", OdtXml.Points(page.MarginBottomPt)),
                    new XAttribute(OdtXml.Fo + "margin-left", OdtXml.Points(page.MarginLeftPt)),
                    new XAttribute(OdtXml.Fo + "margin-right", OdtXml.Points(page.MarginRightPt)))));
    }
}
