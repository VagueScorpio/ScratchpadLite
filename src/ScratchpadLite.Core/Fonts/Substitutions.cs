namespace ScratchpadLite.Core.Fonts;

/// <summary>
/// Picks the closest available family when a document asks for a font that
/// is not installed and not in the font folder (Section 6.6). The original
/// name is kept on the run so saving writes it back unchanged, and the
/// missing-font bar lists what was substituted.
/// </summary>
/// <remarks>
/// The table only covers fonts that are common in documents made somewhere
/// else: the metric-compatible Linux families, the Mac system fonts, and the
/// old PostScript names. Windows already has the Microsoft core fonts, so
/// there is nothing to substitute for them.
/// <para>
/// Substitution is display only. The document keeps the name it asked for, so
/// opening it on a machine that has the font shows the font - which is why
/// this returns a name to show rather than changing anything.
/// </para>
/// </remarks>
public static class Substitutions
{
    private static readonly Dictionary<string, string> Table = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Helvetica"] = "Arial",
        ["Helvetica Neue"] = "Arial",
        ["Liberation Sans"] = "Arial",
        ["Arimo"] = "Arial",
        ["Times"] = "Times New Roman",
        ["Liberation Serif"] = "Times New Roman",
        ["Tinos"] = "Times New Roman",
        ["Courier"] = "Courier New",
        ["Liberation Mono"] = "Courier New",
        ["Cousine"] = "Courier New",
        ["Carlito"] = "Calibri",
        ["Caladea"] = "Cambria",
        ["DejaVu Sans"] = "Verdana",
        ["Bitstream Vera Sans"] = "Verdana",
        ["Menlo"] = "Consolas",
        ["Monaco"] = "Consolas",
        ["DejaVu Sans Mono"] = "Consolas",
        ["San Francisco"] = "Segoe UI",
        ["SF Pro"] = "Segoe UI",
        ["-apple-system"] = "Segoe UI",
    };

    /// <summary>Every name the table knows, for the Help text and the tests.</summary>
    public static IReadOnlyDictionary<string, string> All => Table;

    /// <summary>
    /// The family to show instead of <paramref name="requestedFamily"/>, or
    /// null when there is nothing better than the default.
    /// </summary>
    /// <remarks>
    /// Returns null when the requested font is itself available: a substitute
    /// is only ever used for a font that is missing.
    /// </remarks>
    public static string? Resolve(string requestedFamily, IReadOnlyCollection<string> available)
    {
        ArgumentNullException.ThrowIfNull(available);

        if (string.IsNullOrWhiteSpace(requestedFamily))
        {
            return null;
        }

        var name = requestedFamily.Trim();

        if (Contains(available, name))
        {
            return null;
        }

        if (Table.TryGetValue(name, out var substitute) && Contains(available, substitute))
        {
            return substitute;
        }

        // A CSS-style stack - "Helvetica Neue, Helvetica, Arial, sans-serif" -
        // turns up in documents converted from HTML, so each name is tried in
        // turn before giving up.
        if (name.Contains(',', StringComparison.Ordinal))
        {
            foreach (var part in name.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                var candidate = part.Trim('\'', '"', ' ');

                if (Contains(available, candidate))
                {
                    return candidate;
                }

                if (Table.TryGetValue(candidate, out var mapped) && Contains(available, mapped))
                {
                    return mapped;
                }
            }
        }

        // The generic CSS families, which carry no font of their own.
        return name.ToLowerInvariant() switch
        {
            var g when g.EndsWith("serif", StringComparison.Ordinal)
                       && !g.EndsWith("sans-serif", StringComparison.Ordinal)
                       && Contains(available, "Times New Roman") => "Times New Roman",
            var g when g.EndsWith("sans-serif", StringComparison.Ordinal)
                       && Contains(available, "Arial") => "Arial",
            var g when g.EndsWith("monospace", StringComparison.Ordinal)
                       && Contains(available, "Consolas") => "Consolas",
            _ => null,
        };
    }

    private static bool Contains(IReadOnlyCollection<string> available, string name)
    {
        // A HashSet with the right comparer is the common case and is O(1);
        // anything else falls back to a scan, which keeps the signature simple
        // for callers that only have a list.
        if (available is HashSet<string> set && ReferenceEquals(set.Comparer, StringComparer.OrdinalIgnoreCase))
        {
            return set.Contains(name);
        }

        foreach (var candidate in available)
        {
            if (string.Equals(candidate, name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
