using System.Globalization;
using System.Reflection;

namespace ScratchpadLite.Core.Fonts.Type1;

/// <summary>
/// The Adobe Glyph List, BSD-3 licensed, embedded as a resource
/// (Section 6.6.4). Maps a Type 1 glyph name to a Unicode code point so the
/// generated font can carry a real cmap table. Loaded on first use only.
/// </summary>
/// <remarks>
/// A Type 1 font has no cmap: it maps codes to glyph <em>names</em>, and the
/// names are the only clue to what character a glyph is. So a converted font's
/// cmap is built by looking every name up here.
/// <para>
/// The list covers the names Adobe defined. Three further rules from the AGL
/// specification cover everything else, and they matter because fonts from
/// type designers routinely use them:
/// <list type="bullet">
///   <item><c>uni0041</c> and <c>uni00410042</c> - one or more four-digit values.</item>
///   <item><c>u1F600</c> - four to six digits, for anything above U+FFFF.</item>
///   <item>
///     <c>a.sc</c>, <c>one.taboldstyle</c> - a known name with a variant
///     suffix. The suffix names a stylistic alternate of the same character.
///   </item>
/// </list>
/// </para>
/// <para>
/// Loaded lazily, and it is 78 KB of text, so a font folder with no Type 1
/// fonts in it never pays for this (Section 2.1).
/// </para>
/// </remarks>
public static class GlyphList
{
    private const string ResourceName = "ScratchpadLite.Core.glyphlist.txt";

    private static readonly Lazy<Dictionary<string, int[]>> Names =
        new(Load, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>How many names the list holds. For the tests.</summary>
    public static int Count => Names.Value.Count;

    /// <summary>
    /// The code point a glyph name means, or null when nothing here knows it.
    /// </summary>
    /// <remarks>
    /// A name can stand for a sequence - <c>fi</c> is the ligature, but
    /// <c>Idotaccent</c> maps to two code points - and a cmap can only point one
    /// character at a glyph, so the first is used. The alternative is leaving
    /// the glyph unreachable, which is worse.
    /// </remarks>
    public static int? ToCodePoint(string glyphName)
    {
        var all = ToCodePoints(glyphName);

        return all.Length > 0 ? all[0] : null;
    }

    /// <summary>Every code point a glyph name stands for, in order.</summary>
    public static int[] ToCodePoints(string glyphName)
    {
        if (string.IsNullOrEmpty(glyphName))
        {
            return [];
        }

        if (Names.Value.TryGetValue(glyphName, out var mapped))
        {
            return mapped;
        }

        // "a.sc" is a stylistic variant of "a". Checked before the uni/u forms
        // because "uni0041.alt" is also legal.
        var dot = glyphName.IndexOf('.', StringComparison.Ordinal);
        if (dot > 0)
        {
            return ToCodePoints(glyphName[..dot]);
        }

        if (FromUniForm(glyphName) is { Length: > 0 } uni)
        {
            return uni;
        }

        // "f_f_i" is how a ligature of three glyphs is spelled.
        if (glyphName.Contains('_', StringComparison.Ordinal))
        {
            var parts = glyphName.Split('_', StringSplitOptions.RemoveEmptyEntries);
            var points = new List<int>(parts.Length);

            foreach (var part in parts)
            {
                var resolved = ToCodePoints(part);
                if (resolved.Length == 0)
                {
                    return [];
                }

                points.AddRange(resolved);
            }

            return points.ToArray();
        }

        return [];
    }

    /// <summary>
    /// <c>uniXXXX</c> (one or more four-digit values) and <c>uXXXX</c> to
    /// <c>uXXXXXX</c>.
    /// </summary>
    private static int[] FromUniForm(string name)
    {
        if (name.StartsWith("uni", StringComparison.Ordinal))
        {
            var digits = name[3..];

            if (digits.Length == 0 || digits.Length % 4 != 0)
            {
                return [];
            }

            var points = new int[digits.Length / 4];

            for (var i = 0; i < points.Length; i++)
            {
                if (!TryHex(digits.AsSpan(i * 4, 4), out var value))
                {
                    return [];
                }

                points[i] = value;
            }

            return points;
        }

        if (name.Length is >= 5 and <= 7 && name[0] == 'u')
        {
            return TryHex(name.AsSpan(1), out var single) ? [single] : [];
        }

        return [];
    }

    private static bool TryHex(ReadOnlySpan<char> text, out int value)
    {
        if (!int.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value))
        {
            return false;
        }

        // Surrogate halves are not characters, and nothing above the last plane
        // exists, so either means the name only looked like a uni form.
        return value is >= 0 and <= 0x10FFFF && value is < 0xD800 or > 0xDFFF;
    }

    private static Dictionary<string, int[]> Load()
    {
        var map = new Dictionary<string, int[]>(4400, StringComparer.Ordinal);

        using var stream = typeof(GlyphList).Assembly.GetManifestResourceStream(ResourceName);

        if (stream is null)
        {
            // A build that forgot the resource would otherwise produce fonts
            // with an empty cmap, which looks like the converter being broken.
            throw new InvalidOperationException(
                $"the embedded resource '{ResourceName}' is missing. It is "
                + "src/ScratchpadLite.App/Resources/glyphlist.txt, embedded by "
                + "ScratchpadLite.Core.csproj with that LogicalName.");
        }

        using var reader = new StreamReader(stream);

        while (reader.ReadLine() is { } line)
        {
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            var semicolon = line.IndexOf(';', StringComparison.Ordinal);
            if (semicolon <= 0)
            {
                continue;
            }

            var name = line[..semicolon];
            var fields = line[(semicolon + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var points = new List<int>(fields.Length);

            foreach (var field in fields)
            {
                if (TryHex(field, out var value))
                {
                    points.Add(value);
                }
            }

            if (points.Count > 0)
            {
                map[name] = points.ToArray();
            }
        }

        return map;
    }

    private static bool TryHex(string text, out int value) => TryHex(text.AsSpan(), out value);
}
