using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using ScratchpadLite.App.Fonts;

namespace ScratchpadLite.App.Editor;

/// <summary>
/// The characters the Special Character dialog offers (Section 8.1, Insert,
/// Special Character), grouped into ranges and filtered down to the ones the
/// chosen font can actually draw.
/// </summary>
/// <remarks>
/// <para>The filtering is the point of this class. A character grid that
/// shows every code point in a range is mostly empty boxes, because no font
/// has all of Unicode, and an empty box that inserts an invisible character
/// is worse than no box at all. Every group is therefore asked of the font
/// through its <see cref="GlyphTypeface.CharacterToGlyphMap"/>, which is the
/// font's own answer to "can you draw this".</para>
///
/// <para>The groups are deliberately small - every one of them is a single
/// Unicode block or less, so the widest is 256 characters. That keeps the
/// grid to a few hundred items, which WPF lays out in a plain
/// <c>WrapPanel</c> without virtualization; the alternative, one list of
/// every character in the font, would need a virtualizing wrap panel that
/// WPF does not have.</para>
/// </remarks>
internal static class CharacterPalette
{
    /// <summary>How many recently used characters are remembered.</summary>
    internal const int RecentLimit = 24;

    /// <summary>One named range of code points, inclusive at both ends.</summary>
    internal sealed record Group(string Name, int First, int Last);

    /// <summary>
    /// The ranges on offer, in the order the dialog lists them: the ones
    /// somebody reaches for most often first, the alphabets after.
    /// </summary>
    internal static IReadOnlyList<Group> Groups { get; } =
    [
        new("Punctuation", 0x2010, 0x205E),
        new("Latin-1 (accents and symbols)", 0x00A1, 0x00FF),
        new("Currency", 0x20A0, 0x20BF),
        new("Mathematical operators", 0x2200, 0x22FF),
        new("Arrows", 0x2190, 0x21FF),
        new("Fractions and number forms", 0x2150, 0x218F),
        new("Letterlike symbols", 0x2100, 0x214F),
        new("Geometric shapes", 0x25A0, 0x25FF),
        new("Miscellaneous symbols", 0x2600, 0x26FF),
        new("Dingbats", 0x2700, 0x27BF),
        new("Latin Extended-A", 0x0100, 0x017F),
        new("Greek", 0x0370, 0x03FF),
        new("Cyrillic", 0x0400, 0x04FF),
    ];

    /// <summary>
    /// The characters of this group that <paramref name="fontFamily"/> can
    /// draw. An empty list means the font has nothing in that range, which
    /// the dialog says rather than showing an empty grid.
    /// </summary>
    internal static IReadOnlyList<string> CharactersIn(Group group, string fontFamily)
    {
        ArgumentNullException.ThrowIfNull(group);

        var face = FaceFor(fontFamily);
        var found = new List<string>();

        for (var code = group.First; code <= group.Last; code++)
        {
            // Unassigned code points and the controls are not offered even if
            // a font claims a glyph for them: inserting one puts something in
            // the document that cannot be seen or deleted on purpose.
            if (!IsWorthOffering(code))
            {
                continue;
            }

            if (face is null || face.CharacterToGlyphMap.ContainsKey(code))
            {
                found.Add(char.ConvertFromUtf32(code));
            }
        }

        return found;
    }

    /// <summary>
    /// The glyph typeface for a family name, or null when WPF will not give
    /// one up - a font that has to be rendered through a composite family,
    /// for instance. A null face means "show the range unfiltered", which is
    /// the honest answer: the alternative is an empty dialog for a font that
    /// may well have the characters.
    /// </summary>
    internal static GlyphTypeface? FaceFor(string fontFamily)
    {
        try
        {
            var family = FontRegistry.Resolve(fontFamily ?? "");
            var typeface = new Typeface(
                family, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

            return typeface.TryGetGlyphTypeface(out var face) ? face : null;
        }
        catch (Exception ex) when (ex is NotSupportedException or IOException
                                      or UnauthorizedAccessException or ArgumentException)
        {
            // A broken or unreadable font must not stop the dialog opening.
            return null;
        }
    }

    private static bool IsWorthOffering(int code) =>
        CharUnicodeInfo.GetUnicodeCategory(code) is not (UnicodeCategory.Control
            or UnicodeCategory.Format
            or UnicodeCategory.Surrogate
            or UnicodeCategory.PrivateUse
            or UnicodeCategory.OtherNotAssigned);

    /// <summary>
    /// The recently used list with <paramref name="chosen"/> moved to the
    /// front, no duplicates, and no more than <see cref="RecentLimit"/>
    /// entries.
    /// </summary>
    internal static IReadOnlyList<string> Remember(IEnumerable<string> recent, string chosen)
    {
        ArgumentNullException.ThrowIfNull(recent);

        if (string.IsNullOrEmpty(chosen))
        {
            return [.. recent.Take(RecentLimit)];
        }

        return
        [
            chosen,
            .. recent
                .Where(c => !string.IsNullOrEmpty(c) && !string.Equals(c, chosen, StringComparison.Ordinal))
                .Take(RecentLimit - 1),
        ];
    }

    /// <summary>"U+00E9" for é, which is what a character's code point is called.</summary>
    internal static string Describe(string character)
    {
        if (string.IsNullOrEmpty(character))
        {
            return "";
        }

        var code = char.ConvertToUtf32(character, 0);
        var category = CharUnicodeInfo.GetUnicodeCategory(character, 0);

        return $"U+{code:X4}    {Readable(category)}";
    }

    private static string Readable(UnicodeCategory category) => category switch
    {
        UnicodeCategory.UppercaseLetter => "uppercase letter",
        UnicodeCategory.LowercaseLetter => "lowercase letter",
        UnicodeCategory.TitlecaseLetter => "titlecase letter",
        UnicodeCategory.OtherLetter or UnicodeCategory.ModifierLetter => "letter",
        UnicodeCategory.DecimalDigitNumber => "digit",
        UnicodeCategory.LetterNumber or UnicodeCategory.OtherNumber => "number",
        UnicodeCategory.MathSymbol => "mathematical symbol",
        UnicodeCategory.CurrencySymbol => "currency symbol",
        UnicodeCategory.ModifierSymbol => "modifier symbol",
        UnicodeCategory.OtherSymbol => "symbol",
        UnicodeCategory.DashPunctuation => "dash",
        UnicodeCategory.OpenPunctuation or UnicodeCategory.ClosePunctuation => "bracket",
        UnicodeCategory.InitialQuotePunctuation or UnicodeCategory.FinalQuotePunctuation => "quotation mark",
        UnicodeCategory.ConnectorPunctuation or UnicodeCategory.OtherPunctuation => "punctuation",
        UnicodeCategory.SpaceSeparator => "space",
        _ => "character",
    };
}
