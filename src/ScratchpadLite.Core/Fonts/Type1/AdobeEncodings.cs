namespace ScratchpadLite.Core.Fonts.Type1;

/// <summary>
/// Adobe's StandardEncoding, which a Type 1 font may use instead of listing
/// an encoding of its own (Section 6.6.4).
/// </summary>
/// <remarks>
/// Needed twice: for a font whose clear-text part just says
/// <c>/Encoding StandardEncoding def</c>, and for the <c>seac</c> charstring
/// command, which names its base and accent glyphs by standard-encoding code
/// rather than by name - so an accented character cannot be built without
/// this table.
/// </remarks>
public static class AdobeEncodings
{
    private static readonly string?[] Standard = BuildStandard();

    /// <summary>The glyph name at a StandardEncoding code, or null.</summary>
    public static string? StandardName(int code) =>
        code is >= 0 and < 256 ? Standard[code] : null;

    /// <summary>StandardEncoding as a code-to-name map.</summary>
    public static Dictionary<int, string> StandardEncoding()
    {
        var map = new Dictionary<int, string>(149);

        for (var code = 0; code < 256; code++)
        {
            if (Standard[code] is { } name)
            {
                map[code] = name;
            }
        }

        return map;
    }

    private static string?[] BuildStandard()
    {
        var names = new string?[256];

        // 32 to 126, which is ASCII apart from the two quote characters: Adobe
        // put typographic quotes where ASCII has the vertical ones.
        string[] ascii =
        [
            "space", "exclam", "quotedbl", "numbersign", "dollar", "percent", "ampersand", "quoteright",
            "parenleft", "parenright", "asterisk", "plus", "comma", "hyphen", "period", "slash",
            "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine",
            "colon", "semicolon", "less", "equal", "greater", "question", "at",
            "A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L", "M",
            "N", "O", "P", "Q", "R", "S", "T", "U", "V", "W", "X", "Y", "Z",
            "bracketleft", "backslash", "bracketright", "asciicircum", "underscore", "quoteleft",
            "a", "b", "c", "d", "e", "f", "g", "h", "i", "j", "k", "l", "m",
            "n", "o", "p", "q", "r", "s", "t", "u", "v", "w", "x", "y", "z",
            "braceleft", "bar", "braceright", "asciitilde",
        ];

        for (var i = 0; i < ascii.Length; i++)
        {
            names[32 + i] = ascii[i];
        }

        // The upper half, which is sparse.
        (int Code, string Name)[] upper =
        [
            (161, "exclamdown"), (162, "cent"), (163, "sterling"), (164, "fraction"), (165, "yen"),
            (166, "florin"), (167, "section"), (168, "currency"), (169, "quotesingle"),
            (170, "quotedblleft"), (171, "guillemotleft"), (172, "guilsinglleft"),
            (173, "guilsinglright"), (174, "fi"), (175, "fl"), (177, "endash"), (178, "dagger"),
            (179, "daggerdbl"), (180, "periodcentered"), (182, "paragraph"), (183, "bullet"),
            (184, "quotesinglbase"), (185, "quotedblbase"), (186, "quotedblright"),
            (187, "guillemotright"), (188, "ellipsis"), (189, "perthousand"), (191, "questiondown"),
            (193, "grave"), (194, "acute"), (195, "circumflex"), (196, "tilde"), (197, "macron"),
            (198, "breve"), (199, "dotaccent"), (200, "dieresis"), (202, "ring"), (203, "cedilla"),
            (205, "hungarumlaut"), (206, "ogonek"), (207, "caron"), (208, "emdash"), (225, "AE"),
            (227, "ordfeminine"), (232, "Lslash"), (233, "Oslash"), (234, "OE"),
            (235, "ordmasculine"), (241, "ae"), (245, "dotlessi"), (248, "lslash"), (249, "oslash"),
            (250, "oe"), (251, "germandbls"),
        ];

        foreach (var (code, name) in upper)
        {
            names[code] = name;
        }

        return names;
    }
}
