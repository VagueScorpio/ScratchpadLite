using System.Text;
using ScratchpadLite.Core.Platform;

namespace ScratchpadLite.Core.Fonts;

/// <summary>
/// Generates FONT_LICENSES.txt from the fonts in the font folder
/// (Section 6.6.6).
/// </summary>
/// <remarks>
/// The bundled fonts ship inside the zip, so their licences travel with them
/// and have to be stated. This reads the names out of each font's own
/// <c>name</c> table - copyright (ID 0), licence text (ID 13) and licence URL
/// (ID 14) - plus any licence files sitting beside them, and puts anything
/// questionable in a list at the top.
/// <para>
/// It flags and never blocks. Whether a flagged font may ship is a decision
/// for a person, so the generator's job is to make sure nobody has to go
/// looking.
/// </para>
/// </remarks>
public static class FontLicenses
{
    /// <summary>
    /// Phrases that mean a font probably cannot be redistributed. Matched
    /// case-insensitively against the copyright and licence text.
    /// </summary>
    private static readonly string[] Worrying =
    [
        "Adobe Fonts",
        "Typekit",
        "not for redistribution",
        "may not be redistributed",
        "desktop license",
        "desktop licence",
        "evaluation",
        "trial version",
        "all rights reserved",
    ];

    /// <summary>The licence text cut to this many characters, per Section 6.6.6.</summary>
    private const int LicenseExcerpt = 300;

    private sealed record Family(string Name)
    {
        public List<string> Files { get; } = [];

        public string? Copyright { get; set; }

        public string? License { get; set; }

        public string? Url { get; set; }

        public string Embedding { get; set; } = "";

        public List<string> Flags { get; } = [];
    }

    /// <summary>
    /// Builds the file's contents. Returns a short note when the folder holds
    /// no fonts, rather than an empty file.
    /// </summary>
    public static string Generate(string fontDir)
    {
        ArgumentException.ThrowIfNullOrEmpty(fontDir);

        var families = new Dictionary<string, Family>(StringComparer.OrdinalIgnoreCase);
        var licenseFiles = new List<string>();
        var unreadable = new List<string>();

        foreach (var found in FontScanner.Scan(fontDir, CancellationToken.None))
        {
            if (found.Kind == FontFileKind.LicenseText)
            {
                licenseFiles.Add(found.RelativePath);
                continue;
            }

            if (found.Kind is not (FontFileKind.Direct or FontFileKind.Woff or FontFileKind.Woff2))
            {
                continue;
            }

            try
            {
                Add(families, found, ReadInfo(found));
            }
            catch (Exception ex) when (ex is FontFormatException or IOException
                                           or UnauthorizedAccessException
                                           or Formats.FileTooLargeException)
            {
                unreadable.Add($"{found.RelativePath} - {ex.Message}");
            }
        }

        return Render(fontDir, families, licenseFiles, unreadable);
    }

    /// <summary>Writes the file, replacing whatever was there.</summary>
    public static void Write(string fontDir, string outputPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(outputPath);

        File.WriteAllText(outputPath, Generate(fontDir));
        Log.Info($"wrote {outputPath}");
    }

    private static FontInspector.Info ReadInfo(FontScanner.Found found)
    {
        var bytes = File.ReadAllBytes(found.Path);

        using var input = new MemoryStream(bytes, writable: false);

        var sfnt = found.Kind switch
        {
            FontFileKind.Woff => WoffDecoder.ToSfnt(input),
            FontFileKind.Woff2 => Woff2Decoder.ToSfnt(input),
            _ => bytes,
        };

        return FontInspector.Read(sfnt);
    }

    private static void Add(
        Dictionary<string, Family> families,
        FontScanner.Found found,
        FontInspector.Info info)
    {
        var name = info.Families.Count > 0 ? info.Families[0] : "(no family name)";

        if (!families.TryGetValue(name, out var family))
        {
            family = new Family(name);
            families[name] = family;
        }

        family.Files.Add(found.RelativePath);
        family.Copyright ??= info.Copyright;
        family.License ??= info.LicenseDescription;
        family.Url ??= info.LicenseUrl;
        family.Embedding = info.EmbeddingRights;

        if (string.IsNullOrWhiteSpace(info.Copyright)
            && string.IsNullOrWhiteSpace(info.LicenseDescription)
            && string.IsNullOrWhiteSpace(info.LicenseUrl))
        {
            Flag(family, "no licence information in the font at all");
        }

        if ((info.FsType & 0x0200) != 0)
        {
            Flag(family, "embedding is Restricted");
        }

        var text = $"{info.Copyright} {info.LicenseDescription}";

        foreach (var phrase in Worrying)
        {
            if (text.Contains(phrase, StringComparison.OrdinalIgnoreCase))
            {
                Flag(family, $"its text mentions \"{phrase}\"");
            }
        }
    }

    private static void Flag(Family family, string reason)
    {
        if (!family.Flags.Contains(reason, StringComparer.Ordinal))
        {
            family.Flags.Add(reason);
        }
    }

    private static string Render(
        string fontDir,
        Dictionary<string, Family> families,
        List<string> licenseFiles,
        List<string> unreadable)
    {
        var text = new StringBuilder();

        text.AppendLine("Scratchpad Lite - Bundled Font Licenses");
        text.AppendLine("=======================================");
        text.AppendLine();

        if (families.Count == 0)
        {
            text.AppendLine("No fonts are bundled with this build.");
            text.AppendLine();
            text.AppendLine(
                "The fonts/ folder beside the .exe is empty apart from its README, so there is");
            text.AppendLine(
                "nothing here to license. Fonts you drop into that folder yourself stay on your");
            text.AppendLine("PC and are not redistributed.");
            text.AppendLine();
            text.AppendLine("This file is regenerated by build/build.ps1 whenever fonts are added.");

            return text.ToString();
        }

        text.AppendLine($"Generated from {fontDir}");
        text.AppendLine($"{families.Count} families, {families.Sum(f => f.Value.Files.Count)} files.");
        text.AppendLine();

        var flagged = families.Values.Where(f => f.Flags.Count > 0).OrderBy(f => f.Name).ToList();

        if (flagged.Count > 0)
        {
            text.AppendLine("NEEDS A DECISION BEFORE SHIPPING");
            text.AppendLine("--------------------------------");
            text.AppendLine();
            text.AppendLine(
                "These are flagged, not rejected. Check each one before the release build:");
            text.AppendLine();

            foreach (var family in flagged)
            {
                text.AppendLine($"  {family.Name}");
                foreach (var flag in family.Flags)
                {
                    text.AppendLine($"      - {flag}");
                }
            }

            text.AppendLine();
        }

        if (unreadable.Count > 0)
        {
            text.AppendLine("COULD NOT BE READ");
            text.AppendLine("-----------------");
            text.AppendLine();

            foreach (var line in unreadable)
            {
                text.AppendLine($"  {line}");
            }

            text.AppendLine();
        }

        foreach (var family in families.Values.OrderBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            text.AppendLine(family.Name);
            text.AppendLine(new string('-', family.Name.Length));
            text.AppendLine();

            text.AppendLine($"  Files:      {string.Join(", ", family.Files.Order(StringComparer.Ordinal))}");
            text.AppendLine($"  Embedding:  {family.Embedding}");

            if (!string.IsNullOrWhiteSpace(family.Copyright))
            {
                text.AppendLine($"  Copyright:  {OneLine(family.Copyright)}");
            }

            if (!string.IsNullOrWhiteSpace(family.Url))
            {
                text.AppendLine($"  Licence:    {OneLine(family.Url)}");
            }

            if (!string.IsNullOrWhiteSpace(family.License))
            {
                text.AppendLine($"  Terms:      {Excerpt(family.License)}");
            }

            text.AppendLine();
        }

        if (licenseFiles.Count > 0)
        {
            text.AppendLine("Licence files found beside the fonts");
            text.AppendLine("-----------------------------------");
            text.AppendLine();

            foreach (var file in licenseFiles.Order(StringComparer.Ordinal))
            {
                text.AppendLine($"  {file}");
            }

            text.AppendLine();
        }

        return text.ToString();
    }

    private static string OneLine(string? value) =>
        (value ?? "").ReplaceLineEndings(" ").Trim();

    private static string Excerpt(string? value)
    {
        var line = OneLine(value);

        return line.Length <= LicenseExcerpt ? line : line[..LicenseExcerpt] + "...";
    }
}
