using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using ScratchpadLite.Core.Model;

namespace ScratchpadLite.Core.Formats.Docx;

/// <summary>
/// Reads the numbering definitions, so a list paragraph can be told from an
/// indented one (Section 7.2).
/// </summary>
/// <remarks>
/// A .docx paragraph does not say "I am a bullet". It says "numbering instance
/// 3, level 0", and instance 3 points at an abstract definition whose level 0
/// says whether that is a bullet or a number and in what format. Without
/// following that chain, every list in every Word document reads as a plain
/// indented paragraph.
/// </remarks>
internal sealed class NumberingMap
{
    /// <summary>Numbering id to the abstract definition it uses.</summary>
    private readonly Dictionary<int, int> _instanceToAbstract = [];

    /// <summary>Abstract id and level to what that level looks like.</summary>
    private readonly Dictionary<(int Abstract, int Level), LevelInfo> _levels = [];

    /// <summary>A start override on an instance, which restarts its numbering.</summary>
    private readonly Dictionary<(int Instance, int Level), int> _starts = [];

    private sealed record LevelInfo(ListKind Kind, string Format, int Start);

    internal static NumberingMap From(MainDocumentPart main)
    {
        var map = new NumberingMap();
        var numbering = main.NumberingDefinitionsPart?.Numbering;

        if (numbering is null)
        {
            return map;
        }

        foreach (var abstractNum in numbering.Elements<AbstractNum>())
        {
            if (abstractNum.AbstractNumberId?.Value is not { } abstractId)
            {
                continue;
            }

            foreach (var level in abstractNum.Elements<Level>())
            {
                if (level.LevelIndex?.Value is not { } index)
                {
                    continue;
                }

                var format = level.NumberingFormat?.Val?.Value;

                map._levels[(abstractId, index)] = new LevelInfo(
                    KindFor(format),
                    FormatFor(format),
                    level.StartNumberingValue?.Val?.Value ?? 1);
            }
        }

        foreach (var instance in numbering.Elements<NumberingInstance>())
        {
            if (instance.NumberID?.Value is not { } id
                || instance.AbstractNumId?.Val?.Value is not { } abstractId)
            {
                continue;
            }

            map._instanceToAbstract[id] = abstractId;

            foreach (var over in instance.Elements<LevelOverride>())
            {
                if (over.LevelIndex?.Value is { } level
                    && over.StartOverrideNumberingValue?.Val?.Value is { } start)
                {
                    map._starts[(id, level)] = start;
                }
            }
        }

        return map;
    }

    /// <summary>
    /// What a paragraph's numbering properties mean, or null when they mean
    /// "not a list".
    /// </summary>
    internal ListInfo? For(NumberingProperties properties, StyleResolver styles, string? styleId)
    {
        var id = properties.NumberingId?.Val?.Value;
        var level = properties.NumberingLevelReference?.Val?.Value ?? 0;

        // The numbering may be on the paragraph's style rather than on the
        // paragraph, which is how Word writes a list style.
        if (id is null)
        {
            var fromStyle = styles.EffectiveFor(styleId);

            id = fromStyle.NumberingId;
            level = properties.NumberingLevelReference?.Val?.Value
                    ?? fromStyle.NumberingLevel
                    ?? 0;
        }

        // Numbering id 0 means "explicitly not numbered", which Word uses to
        // take a paragraph out of a list it would otherwise inherit.
        if (id is null or 0)
        {
            return null;
        }

        if (!_instanceToAbstract.TryGetValue(id.Value, out var abstractId))
        {
            // An instance the file never defined. It is still a list - the
            // paragraph says so - so it becomes a bullet rather than nothing.
            return new ListInfo(ListKind.Bullet, Math.Clamp(level, 0, 8));
        }

        if (!_levels.TryGetValue((abstractId, level), out var info))
        {
            info = _levels.TryGetValue((abstractId, 0), out var first)
                ? first
                : new LevelInfo(ListKind.Bullet, "decimal", 1);
        }

        var start = _starts.TryGetValue((id.Value, level), out var overridden)
            ? overridden
            : info.Start;

        return new ListInfo(info.Kind, Math.Clamp(level, 0, 8), info.Format, Math.Max(1, start));
    }

    private static ListKind KindFor(NumberFormatValues? format)
    {
        if (format is null)
        {
            return ListKind.Number;
        }

        if (format == NumberFormatValues.Bullet)
        {
            return ListKind.Bullet;
        }

        // "none" means a list with no marker at all, which reads as a bullet
        // list whose bullets happen to be invisible - closer than a numbered
        // list with no numbers.
        return format == NumberFormatValues.None ? ListKind.Bullet : ListKind.Number;
    }

    private static string FormatFor(NumberFormatValues? format)
    {
        if (format is null)
        {
            return "decimal";
        }

        if (format == NumberFormatValues.LowerLetter)
        {
            return "lowerLetter";
        }

        if (format == NumberFormatValues.UpperLetter)
        {
            return "upperLetter";
        }

        if (format == NumberFormatValues.LowerRoman)
        {
            return "lowerRoman";
        }

        if (format == NumberFormatValues.UpperRoman)
        {
            return "upperRoman";
        }

        return "decimal";
    }
}
