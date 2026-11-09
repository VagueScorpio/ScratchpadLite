namespace ScratchpadLite.Core.Fonts.Type1;

/// <summary>
/// Adobe Type 1 to OpenType CFF (Section 6.6.4). The orchestration: parse,
/// convert every charstring, write the CFF table, synthesise the rest, and
/// assemble the font.
/// </summary>
/// <remarks>
/// The pieces are <see cref="Type1Parser"/>,
/// <see cref="CharStringConverter"/>, <see cref="Type2Writer"/>,
/// <see cref="CffWriter"/> and <see cref="SfntTables"/>. What is left here is
/// the decisions that need all of them at once: which glyph gets which index,
/// what character maps to it, and what the font is called.
/// <para>
/// Hints are dropped (Section 6.6.4 allows it), so a converted font can look
/// slightly soft on screen at small sizes. Printing is unaffected.
/// </para>
/// </remarks>
public static class Type1Converter
{
    /// <summary>The result, with what the Font Report needs to say about it.</summary>
    public sealed record Result(byte[] Sfnt, string FamilyName, string StyleName, int GlyphCount)
    {
        /// <summary>True when the font's glyph names meant nothing to the AGL.</summary>
        public bool IsSymbolFont { get; init; }

        /// <summary>How many glyphs got no character mapping at all.</summary>
        public int Unmapped { get; init; }
    }

    /// <summary>
    /// Converts a Type 1 font.
    /// </summary>
    /// <param name="font">The .pfb or .pfa bytes.</param>
    /// <param name="fontPath">
    /// Where it came from, used to look for a .pfm or .afm beside it.
    /// </param>
    /// <param name="sibling">Reads a file next to the font, or returns null.</param>
    public static Result Convert(
        ReadOnlySpan<byte> font,
        string fontPath,
        Func<string, byte[]?> sibling)
    {
        var parsed = Type1Parser.Parse(font);

        if (parsed.Unsupported is { } reason)
        {
            throw new FontFormatException(reason);
        }

        var metrics = AfmPfmReader.FindFor(fontPath, sibling);

        // Glyph order: .notdef first, because CFF requires it, then the rest
        // in a stable order so two conversions of the same font produce the
        // same bytes and therefore the same cache file.
        var names = new List<string> { ".notdef" };

        names.AddRange(parsed.CharStrings.Keys
            .Where(name => name != ".notdef")
            .OrderBy(name => name, StringComparer.Ordinal));

        var glyphs = new List<CffWriter.Glyph>(names.Count);
        var advances = new List<double>(names.Count);
        var bearings = new List<double>(names.Count);

        var scale = Scale(parsed.FontMatrix);
        var unitsPerEm = (int)Math.Round(1.0 / scale);

        if (unitsPerEm is < 16 or > 16384)
        {
            unitsPerEm = 1000;
        }

        var notDefWidth = 0.0;

        for (var index = 0; index < names.Count; index++)
        {
            var name = names[index];

            if (index == 0 && !parsed.CharStrings.ContainsKey(".notdef"))
            {
                // Every CFF font must have a .notdef, and a Type 1 font need
                // not provide one.
                glyphs.Add(new CffWriter.Glyph(".notdef", Type2Writer.NotDef(notDefWidth)));
                advances.Add(notDefWidth);
                bearings.Add(0);
                continue;
            }

            var outline = CharStringConverter.Convert(parsed, name);
            var width = outline.Width;

            // An AFM's widths win over the charstring's, because an AFM is the
            // font's published metrics and hsbw is sometimes rounded.
            if (metrics?.Widths.TryGetValue(name, out var published) == true && published > 0)
            {
                width = published;
            }

            glyphs.Add(new CffWriter.Glyph(name, Type2Writer.Write(outline with { Width = width })));
            advances.Add(width);
            bearings.Add(outline.LeftSideBearing);
        }

        var (unicode, symbol) = BuildMappings(parsed, names);

        var postScriptName = parsed.FontName ?? Path.GetFileNameWithoutExtension(fontPath);
        var (guessedFamily, guessedStyle) = AfmPfmReader.SplitPostScriptName(postScriptName);

        var family = metrics?.FamilyName ?? parsed.FamilyName ?? guessedFamily;
        var style = StyleFrom(metrics, parsed, guessedStyle);

        var cff = CffWriter.Write(
            new CffWriter.Info
            {
                FontName = postScriptName,
                FullName = parsed.FullName ?? $"{family} {style}".Trim(),
                FamilyName = family,
                Weight = parsed.Weight,
                Notice = parsed.Notice,
                ItalicAngle = parsed.ItalicAngle,
                IsFixedPitch = parsed.IsFixedPitch || metrics?.IsFixedPitch == true,
                UnderlinePosition = parsed.UnderlinePosition,
                UnderlineThickness = parsed.UnderlineThickness,
                FontBBox = parsed.FontBBox,
                BlueValues = parsed.BlueValues,
                OtherBlues = parsed.OtherBlues,
                StdHw = parsed.StdHw,
                StdVw = parsed.StdVw,
                UnitsPerEm = unitsPerEm,
            },
            glyphs);

        var isBold = metrics?.IsBold
                     ?? (AfmPfmReader.WeightFromName(parsed.Weight) >= 600);

        var isItalic = metrics?.IsItalic ?? Math.Abs(parsed.ItalicAngle) > 0.5;

        var tables = SfntTables.Build(
            new SfntTables.Metrics
            {
                UnitsPerEm = unitsPerEm,
                BBox = parsed.FontBBox,
                ItalicAngle = parsed.ItalicAngle,
                IsFixedPitch = parsed.IsFixedPitch || metrics?.IsFixedPitch == true,
                UnderlinePosition = parsed.UnderlinePosition,
                UnderlineThickness = parsed.UnderlineThickness,
                AdvanceWidths = advances,
                LeftSideBearings = bearings,
                UnicodeToGlyph = unicode,
                SymbolCodeToGlyph = symbol,
                FamilyName = family,
                StyleName = style,
                FullName = parsed.FullName ?? $"{family} {style}".Trim(),
                PostScriptName = postScriptName,
                Copyright = parsed.Notice,
                Version = parsed.Version is { Length: > 0 } v ? $"Version {v}" : "Version 1.000",
                WeightClass = metrics?.WeightClass ?? AfmPfmReader.WeightFromName(parsed.Weight),
                IsBold = isBold,
                IsItalic = isItalic,
                Ascender = metrics?.Ascender ?? 0,
                Descender = metrics?.Descender ?? 0,
            },
            cffOutlines: true);

        tables.Add(new Sfnt.Table(Sfnt.StringToTag("CFF "), cff));

        var sfnt = Sfnt.Build(Sfnt.CffFlavor, tables);

        return new Result(sfnt, family, style, glyphs.Count)
        {
            IsSymbolFont = symbol.Count > 0 && unicode.Count == 0,
            Unmapped = glyphs.Count - 1 - unicode.Count - symbol.Count,
        };
    }

    /// <summary>
    /// Works out what characters the glyphs are, which is the whole reason the
    /// Adobe Glyph List is embedded (Section 6.6.4).
    /// </summary>
    /// <remarks>
    /// Two paths. A text font's glyph names are AGL names - <c>eacute</c>,
    /// <c>ampersand</c> - and map to real code points. A symbol or dingbat
    /// font's names are its own invention - <c>a12</c>, <c>H18533</c> - and
    /// mean nothing outside the font, so there is nothing to map them to:
    /// those get a symbol cmap built from the font's own encoding at U+F020
    /// upwards, which is where Windows looks for one.
    /// <para>
    /// The decision between them is made by counting: a font whose names the
    /// AGL mostly knows is a text font, and one whose names it mostly does not
    /// is a symbol font. Doing it per glyph rather than per font would give a
    /// font with a few recognisable names a half-working cmap, which is worse
    /// than either answer.
    /// </para>
    /// </remarks>
    private static (Dictionary<int, int> Unicode, Dictionary<int, int> Symbol) BuildMappings(
        Type1Parser.Font font,
        List<string> names)
    {
        var byName = new Dictionary<string, int>(names.Count, StringComparer.Ordinal);

        for (var i = 0; i < names.Count; i++)
        {
            byName.TryAdd(names[i], i);
        }

        var unicode = new Dictionary<int, int>(names.Count);
        var recognised = 0;

        for (var i = 1; i < names.Count; i++)
        {
            if (GlyphList.ToCodePoint(names[i]) is { } codePoint)
            {
                recognised++;
                unicode.TryAdd(codePoint, i);
            }
        }

        var namedGlyphs = names.Count - 1;

        // Half is the threshold. A text font scores close to 1; a dingbat font
        // scores close to 0, apart from "space".
        if (namedGlyphs > 0 && recognised * 2 >= namedGlyphs)
        {
            return (unicode, new Dictionary<int, int>());
        }

        var encoding = font.UsesStandardEncoding
            ? AdobeEncodings.StandardEncoding()
            : font.Encoding;

        var symbol = new Dictionary<int, int>(encoding.Count);

        foreach (var (code, name) in encoding)
        {
            if (byName.TryGetValue(name, out var glyph))
            {
                symbol.TryAdd(code, glyph);
            }
        }

        // A symbol font with no encoding of its own cannot be mapped at all, so
        // fall back to whatever the names did give.
        return symbol.Count == 0 ? (unicode, new Dictionary<int, int>()) : (new Dictionary<int, int>(), symbol);
    }

    private static string StyleFrom(
        AfmPfmReader.Metrics? metrics,
        Type1Parser.Font parsed,
        string guessed)
    {
        if (metrics is not null)
        {
            return (metrics.IsBold, metrics.IsItalic) switch
            {
                (true, true) => "Bold Italic",
                (true, false) => "Bold",
                (false, true) => "Italic",
                _ => guessed,
            };
        }

        var bold = AfmPfmReader.WeightFromName(parsed.Weight) >= 600;
        var italic = Math.Abs(parsed.ItalicAngle) > 0.5;

        return (bold, italic) switch
        {
            (true, true) => "Bold Italic",
            (true, false) => "Bold",
            (false, true) => "Italic",
            _ => guessed,
        };
    }

    /// <summary>
    /// The scale the FontMatrix implies, which says what the em is in font
    /// units (Section 6.6.4, step 6).
    /// </summary>
    private static double Scale(double[] matrix)
    {
        // [0.001 0 0 0.001 0 0] is the usual matrix, meaning 1000 units per em.
        var sx = matrix.Length > 0 && matrix[0] != 0 ? Math.Abs(matrix[0]) : 0.001;

        return sx;
    }
}
