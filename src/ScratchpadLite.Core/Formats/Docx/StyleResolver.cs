using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using ScratchpadLite.Core.Model;

namespace ScratchpadLite.Core.Formats.Docx;

/// <summary>
/// Flattens a .docx's style definitions, so a run's effective formatting is
/// known (Section 7.2).
/// </summary>
/// <remarks>
/// Word's formatting is inherited through four layers: the document defaults,
/// a paragraph style, any style that style is based on (recursively), and the
/// run's own properties. A reader that only looks at the run sees almost
/// nothing - a bold heading's runs usually carry no bold at all, because the
/// style has it.
/// <para>
/// Styles are matched to the app's own sheet by <b>name</b>, never by id,
/// because Word localises the ids: a German Word writes
/// <c>berschrift1</c> where an English one writes <c>Heading1</c>. The name
/// in the file is the same either way.
/// </para>
/// </remarks>
internal sealed class StyleResolver
{
    private readonly Dictionary<string, Effective> _byId = new(StringComparer.OrdinalIgnoreCase);
    private readonly Effective _defaults;
    private readonly StyleSheet _sheet;

    private StyleResolver(Effective defaults, StyleSheet sheet)
    {
        _defaults = defaults;
        _sheet = sheet;
    }

    /// <summary>The formatting a style resolves to.</summary>
    internal sealed record Effective
    {
        /// <summary>The app's style name this maps to, or Normal.</summary>
        internal string StyleName { get; init; } = StyleSheet.NormalStyleName;

        internal string? Family { get; init; }

        internal double? SizePt { get; init; }

        internal bool Bold { get; init; }

        internal bool Italic { get; init; }

        internal UnderlineKind Underline { get; init; }

        internal bool Strike { get; init; }

        internal bool SmallCaps { get; init; }

        internal bool AllCaps { get; init; }

        internal string? Color { get; init; }

        internal string? Highlight { get; init; }

        internal double? LetterSpacingPt { get; init; }

        /// <summary>The numbering id this style carries, if any.</summary>
        internal int? NumberingId { get; init; }

        internal int? NumberingLevel { get; init; }
    }

    internal static StyleResolver From(MainDocumentPart main, StyleSheet sheet)
    {
        var definitions = main.StyleDefinitionsPart?.Styles;
        var defaults = DefaultsOf(definitions);
        var resolver = new StyleResolver(defaults, sheet);

        if (definitions is null)
        {
            return resolver;
        }

        var byId = definitions.Elements<Style>()
            .Where(s => s.StyleId?.Value is not null)
            .ToDictionary(s => s.StyleId!.Value!, s => s, StringComparer.OrdinalIgnoreCase);

        foreach (var (id, _) in byId)
        {
            resolver._byId[id] = resolver.Flatten(id, byId, depth: 0);
        }

        return resolver;
    }

    /// <summary>The effective formatting for a style id.</summary>
    internal Effective EffectiveFor(string? styleId) =>
        styleId is not null && _byId.TryGetValue(styleId, out var effective) ? effective : _defaults;

    /// <summary>
    /// Walks the basedOn chain, applying each style's properties over its
    /// parent's.
    /// </summary>
    private Effective Flatten(string id, Dictionary<string, Style> byId, int depth)
    {
        // A basedOn chain that loops is malformed but does happen, and a
        // reader that follows it forever is worse than one that stops.
        if (depth > 16 || !byId.TryGetValue(id, out var style))
        {
            return _defaults;
        }

        var parent = style.BasedOn?.Val?.Value is { } baseId && !string.Equals(baseId, id, StringComparison.OrdinalIgnoreCase)
            ? Flatten(baseId, byId, depth + 1)
            : _defaults;

        var name = style.StyleName?.Val?.Value;
        var applied = Apply(parent, style.StyleRunProperties);

        var numbering = style.StyleParagraphProperties?.NumberingProperties;

        return applied with
        {
            StyleName = MapName(name, id),
            NumberingId = numbering?.NumberingId?.Val?.Value ?? applied.NumberingId,
            NumberingLevel = numbering?.NumberingLevelReference?.Val?.Value ?? applied.NumberingLevel,
        };
    }

    /// <summary>
    /// Maps a Word style name to one of the app's styles (Section 7.2).
    /// </summary>
    /// <remarks>
    /// Word writes "heading 1" where the sheet says "Heading 1", and the style
    /// id is the only other clue when the name is missing. An unknown style
    /// becomes Normal, with whatever formatting it resolved to applied
    /// directly - so the text still looks right even though the name is gone.
    /// </remarks>
    private string MapName(string? name, string id)
    {
        foreach (var candidate in new[] { name, id })
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            var trimmed = candidate.Trim();

            if (_sheet.Contains(trimmed))
            {
                return trimmed;
            }

            // "heading 1" to "Heading 1", and "Heading1" to "Heading 1".
            foreach (var known in StyleSheet.StyleNames)
            {
                if (string.Equals(trimmed, known, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(
                        trimmed.Replace(" ", "", StringComparison.Ordinal),
                        known.Replace(" ", "", StringComparison.Ordinal),
                        StringComparison.OrdinalIgnoreCase))
                {
                    return known;
                }
            }
        }

        return StyleSheet.NormalStyleName;
    }

    /// <summary>
    /// A run's formatting: its style, then its own properties on top.
    /// </summary>
    internal CharStyle ForRun(Run run, Effective paragraphStyle, string? link)
    {
        var baseStyle = paragraphStyle;

        // A character style on the run, which sits between the paragraph style
        // and the run's own properties.
        if (run.RunProperties?.RunStyle?.Val?.Value is { } characterStyleId)
        {
            var character = EffectiveFor(characterStyleId);

            baseStyle = baseStyle with
            {
                Family = character.Family ?? baseStyle.Family,
                SizePt = character.SizePt ?? baseStyle.SizePt,
                Bold = character.Bold || baseStyle.Bold,
                Italic = character.Italic || baseStyle.Italic,
                Underline = character.Underline != UnderlineKind.None
                    ? character.Underline
                    : baseStyle.Underline,
                Strike = character.Strike || baseStyle.Strike,
                SmallCaps = character.SmallCaps || baseStyle.SmallCaps,
                AllCaps = character.AllCaps || baseStyle.AllCaps,

                // A link's own colour and underline come from the Hyperlink
                // character style, which is the style being a link rather than
                // a choice - so they are not carried into the IR.
                Color = link is null ? character.Color ?? baseStyle.Color : baseStyle.Color,
                Highlight = character.Highlight ?? baseStyle.Highlight,
                LetterSpacingPt = character.LetterSpacingPt ?? baseStyle.LetterSpacingPt,
            };

            if (link is not null)
            {
                baseStyle = baseStyle with { Underline = paragraphStyle.Underline };
            }
        }

        var effective = Apply(baseStyle, run.RunProperties);
        var sheetStyle = _sheet[paragraphStyle.StyleName];

        // Only what differs from the app's own style sheet becomes direct
        // formatting, the same rule the bridge uses - otherwise every run in a
        // heading would carry the heading's size and bold as its own.
        return new CharStyle
        {
            Family = Same(effective.Family, _sheet.FamilyFor(sheetStyle)) ? null : effective.Family,
            SizePt = Near(effective.SizePt, sheetStyle.SizePt) ? null : effective.SizePt,
            Bold = effective.Bold,
            Italic = effective.Italic,
            Underline = effective.Underline,
            Strike = effective.Strike,
            Vertical = VerticalOf(run),
            SmallCaps = effective.SmallCaps,
            AllCapsPreserved = effective.AllCaps,
            LetterSpacingPtPreserved = effective.LetterSpacingPt,
            Color = SameColor(effective.Color, sheetStyle.Color) ? null : effective.Color,
            // A style with shading of its own - Code has a light grey - gives
            // every run in it that shading as an effective value. That is the
            // style, not a highlight anyone applied.
            Highlight = SameHighlight(effective.Highlight, sheetStyle.Shading)
                ? null
                : effective.Highlight,
            Link = link,
        };
    }

    private static VAlign VerticalOf(Run run)
    {
        var value = run.RunProperties?.VerticalTextAlignment?.Val?.Value;

        if (value is null)
        {
            return VAlign.Normal;
        }

        if (value == VerticalPositionValues.Superscript)
        {
            return VAlign.Super;
        }

        return value == VerticalPositionValues.Subscript ? VAlign.Sub : VAlign.Normal;
    }

    /// <summary>
    /// Applies a set of run properties over an inherited set.
    /// </summary>
    /// <remarks>
    /// The on/off elements are three-valued in Word, and the difference
    /// matters: absent means "inherit", present means on, and present with
    /// <c>val="0"</c> means off. Treating absent and off the same way makes a
    /// deliberately-not-bold run inside a bold style come out bold.
    /// </remarks>
    private static Effective Apply(Effective inherited, OpenXmlElement? properties)
    {
        if (properties is null)
        {
            return inherited;
        }

        var result = inherited;

        if (properties.GetFirstChild<RunFonts>() is { } fonts)
        {
            var family = fonts.Ascii?.Value ?? fonts.HighAnsi?.Value ?? fonts.ComplexScript?.Value;

            if (!string.IsNullOrWhiteSpace(family))
            {
                result = result with { Family = family };
            }
        }

        if (properties.GetFirstChild<FontSize>()?.Val?.Value is { } size
            && double.TryParse(size, System.Globalization.CultureInfo.InvariantCulture, out var halfPoints))
        {
            result = result with { SizePt = Units.HalfPointsToPoints(halfPoints) };
        }

        if (properties.GetFirstChild<Bold>() is { } bold)
        {
            result = result with { Bold = On(bold.Val) };
        }

        if (properties.GetFirstChild<Italic>() is { } italic)
        {
            result = result with { Italic = On(italic.Val) };
        }

        if (properties.GetFirstChild<Underline>() is { } underline)
        {
            result = result with { Underline = UnderlineFor(underline.Val?.Value) };
        }

        if (properties.GetFirstChild<Strike>() is { } strike)
        {
            result = result with { Strike = On(strike.Val) };
        }

        if (properties.GetFirstChild<DoubleStrike>() is { } doubleStrike && On(doubleStrike.Val))
        {
            // The IR has one strikethrough, so a double one becomes a single.
            result = result with { Strike = true };
        }

        if (properties.GetFirstChild<SmallCaps>() is { } smallCaps)
        {
            result = result with { SmallCaps = On(smallCaps.Val) };
        }

        if (properties.GetFirstChild<Caps>() is { } caps)
        {
            result = result with { AllCaps = On(caps.Val) };
        }

        if (properties.GetFirstChild<DocumentFormat.OpenXml.Wordprocessing.Spacing>()?.Val?.Value
            is { } tracking)
        {
            result = result with { LetterSpacingPt = Units.TwipsToPoints(tracking) };
        }

        if (properties.GetFirstChild<Color>()?.Val?.Value is { } color && color != "auto")
        {
            result = result with { Color = "#" + color.ToUpperInvariant() };
        }

        // Shading first, then Highlight, because the writer prefers Shading
        // and a file may carry both.
        if (properties.GetFirstChild<Shading>()?.Fill?.Value is { } fill
            && fill is not ("auto" or "FFFFFF"))
        {
            result = result with { Highlight = "#" + fill.ToUpperInvariant() };
        }
        else if (properties.GetFirstChild<Highlight>()?.Val?.Value is { } named
                 && named != HighlightColorValues.None)
        {
            result = result with { Highlight = HexForNamed(named) };
        }

        return result;
    }

    /// <summary>
    /// An on/off element's value. Absent <c>val</c> means on, which is the
    /// opposite of what the name suggests.
    /// </summary>
    private static bool On(DocumentFormat.OpenXml.OnOffValue? value) => value?.Value ?? true;

    private static UnderlineKind UnderlineFor(UnderlineValues? value)
    {
        if (value is null)
        {
            return UnderlineKind.Single;
        }

        if (value == UnderlineValues.None)
        {
            return UnderlineKind.None;
        }

        if (value == UnderlineValues.Double)
        {
            return UnderlineKind.Double;
        }

        if (value == UnderlineValues.Dotted || value == UnderlineValues.DottedHeavy
            || value == UnderlineValues.DotDash || value == UnderlineValues.DotDotDash)
        {
            return UnderlineKind.Dotted;
        }

        if (value == UnderlineValues.Dash || value == UnderlineValues.DashedHeavy
            || value == UnderlineValues.DashLong || value == UnderlineValues.DashLongHeavy)
        {
            return UnderlineKind.Dashed;
        }

        if (value == UnderlineValues.Wave || value == UnderlineValues.WavyDouble
            || value == UnderlineValues.WavyHeavy)
        {
            return UnderlineKind.Wavy;
        }

        return UnderlineKind.Single;
    }

    /// <summary>
    /// Word's sixteen named highlight colours as hex (Section 7.2).
    /// </summary>
    private static string HexForNamed(HighlightColorValues value)
    {
        if (value == HighlightColorValues.Yellow) return "#FFFF00";
        if (value == HighlightColorValues.Green) return "#00FF00";
        if (value == HighlightColorValues.Cyan) return "#00FFFF";
        if (value == HighlightColorValues.Magenta) return "#FF00FF";
        if (value == HighlightColorValues.Blue) return "#0000FF";
        if (value == HighlightColorValues.Red) return "#FF0000";
        if (value == HighlightColorValues.DarkBlue) return "#000080";
        if (value == HighlightColorValues.DarkCyan) return "#008080";
        if (value == HighlightColorValues.DarkGreen) return "#008000";
        if (value == HighlightColorValues.DarkMagenta) return "#800080";
        if (value == HighlightColorValues.DarkRed) return "#800000";
        if (value == HighlightColorValues.DarkYellow) return "#808000";
        if (value == HighlightColorValues.DarkGray) return "#808080";
        if (value == HighlightColorValues.LightGray) return "#C0C0C0";
        if (value == HighlightColorValues.Black) return "#000000";
        if (value == HighlightColorValues.White) return "#FFFFFF";

        return "#FFFF00";
    }

    /// <summary>The document defaults, which everything inherits from.</summary>
    private static Effective DefaultsOf(Styles? styles)
    {
        var defaults = new Effective();
        var runDefaults = styles?.DocDefaults?.RunPropertiesDefault?.RunPropertiesBaseStyle;

        return runDefaults is null ? defaults : Apply(defaults, runDefaults);
    }

    private static bool Same(string? a, string? b) =>
        string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool SameHighlight(string? effective, string? styleShading)
    {
        if (effective is null)
        {
            return true;
        }

        return IrNormalizer.NormalizeColor(effective) == IrNormalizer.NormalizeColor(styleShading);
    }

    private static bool SameColor(string? a, string? b)
    {
        var left = IrNormalizer.NormalizeColor(a);
        var right = IrNormalizer.NormalizeColor(b) ?? "#000000";

        return left is null || left == right;
    }

    private static bool Near(double? a, double b) =>
        a is null || Math.Abs(a.Value - b) < 0.01;
}
