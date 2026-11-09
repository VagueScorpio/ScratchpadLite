using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace ScratchpadLite.Core.Fonts.Type1;

/// <summary>
/// Reads the metrics files that sit beside a Type 1 font: the binary
/// <c>.pfm</c> and the text <c>.afm</c> (Section 6.6.3).
/// </summary>
/// <remarks>
/// These matter more than they look. A Type 1 font's own <c>FontName</c> is
/// something like <c>Garamond-BoldItalic</c>, one name per file, with nothing
/// saying that four such files are one family. The PFM holds the Windows
/// family name and the bold and italic flags separately, which is exactly what
/// is needed to make Regular, Bold, Italic and Bold Italic group into one
/// family in the font picker instead of appearing as four unrelated fonts.
/// <para>
/// Without one, the family name has to be guessed by stripping the style off
/// the end of the PostScript name, which this also does - but the PFM is
/// right and the guess is a guess.
/// </para>
/// </remarks>
public static class AfmPfmReader
{
    /// <summary>What a metrics file adds to what the font itself said.</summary>
    public sealed record Metrics
    {
        public string? FamilyName { get; init; }

        public string? FullName { get; init; }

        public bool IsBold { get; init; }

        public bool IsItalic { get; init; }

        /// <summary>100 to 900, as OS/2 wants it.</summary>
        public int? WeightClass { get; init; }

        public double? Ascender { get; init; }

        public double? Descender { get; init; }

        public double? ItalicAngle { get; init; }

        public bool? IsFixedPitch { get; init; }

        /// <summary>Glyph name to advance width, from an AFM.</summary>
        public Dictionary<string, double> Widths { get; init; } = [];
    }

    /// <summary>
    /// Finds and reads the metrics file matching a font, if there is one.
    /// </summary>
    /// <param name="fontPath">The .pfb or .pfa path.</param>
    /// <param name="sibling">
    /// Returns the bytes of a file in the same folder, or null. Passed in
    /// because a font inside a .zip has siblings too, and they are not on disk.
    /// </param>
    public static Metrics? FindFor(string fontPath, Func<string, byte[]?> sibling)
    {
        ArgumentException.ThrowIfNullOrEmpty(fontPath);
        ArgumentNullException.ThrowIfNull(sibling);

        var baseName = Path.GetFileNameWithoutExtension(fontPath);
        var folder = Path.GetDirectoryName(fontPath) ?? "";

        // Case matters on the extension in practice: a font shipped as
        // NAME.PFB often has name.pfm beside it.
        foreach (var extension in new[] { ".pfm", ".PFM", ".Pfm" })
        {
            if (sibling(Path.Combine(folder, baseName + extension)) is { } pfm)
            {
                return ReadPfm(pfm);
            }
        }

        foreach (var extension in new[] { ".afm", ".AFM", ".Afm" })
        {
            if (sibling(Path.Combine(folder, baseName + extension)) is { } afm)
            {
                return ReadAfm(afm);
            }
        }

        return null;
    }

    /// <summary>
    /// Reads a PFM header. Little-endian, like everything that came from
    /// Windows.
    /// </summary>
    public static Metrics? ReadPfm(ReadOnlySpan<byte> pfm)
    {
        // The fixed part of the header is 117 bytes, then the extension block.
        if (pfm.Length < 117)
        {
            return null;
        }

        var italic = pfm[80] != 0;
        var weight = BinaryPrimitives.ReadUInt16LittleEndian(pfm[83..]);
        var pitchAndFamily = pfm[90];

        // dfFace, at 105, is an offset to a null-terminated name. 101 is
        // dfDevice, which for a printer font reads "PostScript" - which is how
        // this off-by-four was found.
        var faceOffset = BinaryPrimitives.ReadUInt32LittleEndian(pfm[105..]);
        var family = ReadCString(pfm, faceOffset);

        return new Metrics
        {
            FamilyName = family,
            FullName = family,
            IsBold = weight >= 600,
            IsItalic = italic,
            WeightClass = weight is >= 100 and <= 1000 ? RoundWeight(weight) : null,

            // Bit 0 of dfPitchAndFamily set means variable pitch, so clear
            // means fixed - the opposite way round from how it reads.
            IsFixedPitch = (pitchAndFamily & 0x01) == 0,
        };
    }

    /// <summary>Reads the header and character metrics of an AFM.</summary>
    public static Metrics ReadAfm(ReadOnlySpan<byte> afm)
    {
        var text = Encoding.Latin1.GetString(afm);
        var widths = new Dictionary<string, double>(StringComparer.Ordinal);

        string? family = null;
        string? full = null;
        string? weight = null;
        double? ascender = null;
        double? descender = null;
        double? italicAngle = null;
        bool? fixedPitch = null;

        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim('\r', ' ', '\t');

            if (line.Length == 0)
            {
                continue;
            }

            if (line.StartsWith("C ", StringComparison.Ordinal))
            {
                ReadCharMetrics(line, widths);
                continue;
            }

            var space = line.IndexOf(' ', StringComparison.Ordinal);
            if (space <= 0)
            {
                continue;
            }

            var key = line[..space];
            var value = line[(space + 1)..].Trim();

            switch (key)
            {
                case "FamilyName":
                    family = value;
                    break;
                case "FullName":
                    full = value;
                    break;
                case "Weight":
                    weight = value;
                    break;
                case "Ascender":
                    ascender = ParseDouble(value);
                    break;
                case "Descender":
                    descender = ParseDouble(value);
                    break;
                case "ItalicAngle":
                    italicAngle = ParseDouble(value);
                    break;
                case "IsFixedPitch":
                    fixedPitch = value.Equals("true", StringComparison.OrdinalIgnoreCase);
                    break;
            }
        }

        return new Metrics
        {
            FamilyName = family,
            FullName = full,
            IsBold = weight is not null && WeightFromName(weight) >= 600,
            IsItalic = italicAngle is not null && Math.Abs(italicAngle.Value) > 0.5,
            WeightClass = weight is null ? null : WeightFromName(weight),
            Ascender = ascender,
            Descender = descender,
            ItalicAngle = italicAngle,
            IsFixedPitch = fixedPitch,
            Widths = widths,
        };
    }

    /// <summary>Reads "C 32 ; WX 600 ; N space ; B 0 0 0 0 ;".</summary>
    private static void ReadCharMetrics(string line, Dictionary<string, double> widths)
    {
        double? width = null;
        string? name = null;

        foreach (var field in line.Split(';', StringSplitOptions.TrimEntries))
        {
            if (field.StartsWith("WX ", StringComparison.Ordinal))
            {
                width = ParseDouble(field[3..]);
            }
            else if (field.StartsWith("N ", StringComparison.Ordinal))
            {
                name = field[2..].Trim();
            }
        }

        if (name is not null && width is not null)
        {
            widths[name] = width.Value;
        }
    }

    /// <summary>
    /// The OS/2 weight class a Type 1 <c>/Weight</c> string means.
    /// </summary>
    public static int WeightFromName(string? weight)
    {
        if (string.IsNullOrWhiteSpace(weight))
        {
            return 400;
        }

        var name = weight.Trim();

        // Ordered longest-first, so "Extra Light" is not read as "Light" and
        // "Semibold" is not read as "Bold".
        (string Name, int Class)[] table =
        [
            ("ultralight", 100), ("extralight", 200), ("extra light", 200), ("ultra light", 100),
            ("semilight", 300), ("semibold", 600), ("demibold", 600), ("demi bold", 600),
            ("extrabold", 800), ("extra bold", 800), ("ultrabold", 900), ("ultra bold", 900),
            ("thin", 100), ("light", 300), ("book", 400), ("regular", 400), ("normal", 400),
            ("roman", 400), ("medium", 500), ("bold", 700), ("heavy", 900), ("black", 900),
        ];

        foreach (var (candidate, weightClass) in table)
        {
            if (name.Contains(candidate, StringComparison.OrdinalIgnoreCase))
            {
                return weightClass;
            }
        }

        return 400;
    }

    /// <summary>
    /// Splits a PostScript name such as <c>Garamond-BoldItalic</c> into a
    /// family and a style, for fonts with no metrics file.
    /// </summary>
    public static (string Family, string Style) SplitPostScriptName(string? postScriptName)
    {
        if (string.IsNullOrWhiteSpace(postScriptName))
        {
            return ("Converted Font", "Regular");
        }

        var name = postScriptName.Trim();
        var dash = name.LastIndexOf('-');

        if (dash <= 0 || dash == name.Length - 1)
        {
            return (Spaced(name), "Regular");
        }

        var suffix = name[(dash + 1)..];
        var style = KnownStyle(suffix);

        return style is null
            ? (Spaced(name), "Regular")
            : (Spaced(name[..dash]), style);
    }

    private static string? KnownStyle(string suffix)
    {
        var lower = suffix.ToLowerInvariant();

        return lower switch
        {
            "bold" => "Bold",
            "italic" or "oblique" or "it" => "Italic",
            "bolditalic" or "boldoblique" or "boldit" => "Bold Italic",
            "regular" or "roman" or "book" or "normal" => "Regular",
            "light" => "Light",
            "medium" => "Medium",
            "semibold" or "demibold" or "demi" => "SemiBold",
            "black" or "heavy" => "Black",
            "lightitalic" or "lightoblique" => "Light Italic",
            "mediumitalic" => "Medium Italic",
            "semibolditalic" or "demibolditalic" => "SemiBold Italic",
            "blackitalic" or "heavyitalic" => "Black Italic",
            _ => null,
        };
    }

    /// <summary>
    /// Puts spaces into a run-together PostScript family name:
    /// <c>TimesNewRoman</c> to <c>Times New Roman</c>.
    /// </summary>
    private static string Spaced(string name)
    {
        var text = new StringBuilder(name.Length + 4);

        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];

            if (c == '-' || c == '_')
            {
                // A dash that was not a style suffix is part of the name,
                // and reads better as a space: "SomeFont-NotAStyle".
                if (text.Length > 0 && text[^1] != ' ')
                {
                    text.Append(' ');
                }

                continue;
            }

            if (i > 0
                && char.IsUpper(c)
                && !char.IsUpper(name[i - 1])
                && name[i - 1] != ' '
                && name[i - 1] != '-')
            {
                text.Append(' ');
            }

            text.Append(c);
        }

        return text.ToString();
    }

    private static int RoundWeight(int weight) =>
        Math.Clamp((int)Math.Round(weight / 100.0) * 100, 100, 900);

    private static string? ReadCString(ReadOnlySpan<byte> data, uint offset)
    {
        if (offset == 0 || offset >= (uint)data.Length)
        {
            return null;
        }

        var start = (int)offset;
        var end = start;

        while (end < data.Length && data[end] != 0)
        {
            end++;
        }

        var value = Encoding.Latin1.GetString(data[start..end]).Trim();

        return value.Length == 0 ? null : value;
    }

    private static double? ParseDouble(string text) =>
        double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
}
