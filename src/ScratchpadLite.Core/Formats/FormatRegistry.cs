using System.Text;

namespace ScratchpadLite.Core.Formats;

/// <summary>
/// The table of formats, and the one place the Open/Save As dialog filters come
/// from (Section 7.1).
/// </summary>
/// <remarks>
/// Speed rule (Section 2.1): names and extensions live in a static table and
/// each handler is created through <see cref="Lazy{T}"/>, so building a dialog
/// filter loads no format package. Nothing here may touch a format library type,
/// or .NET loads that assembly at startup.
/// <para>
/// That is not a style preference, it is the difference between an Open dialog
/// that appears at once and one that stalls while the Open XML SDK is loaded
/// and JIT-compiled. The rule is enforced by a test which opens the dialog
/// filter and then checks the loaded assemblies.
/// </para>
/// </remarks>
public static class FormatRegistry
{
    /// <summary>A format identity, available without creating its handler.</summary>
    /// <param name="Name">Display name.</param>
    /// <param name="Extensions">Extensions including the dot.</param>
    /// <param name="CanRead">Whether the format can be opened.</param>
    /// <param name="CanWrite">Whether the format can be saved.</param>
    /// <param name="Capabilities">What survives a save, for the save warning.</param>
    public sealed record FormatInfo(
        string Name,
        IReadOnlyList<string> Extensions,
        bool CanRead,
        bool CanWrite,
        Capabilities Capabilities);

    /// <summary>
    /// The formats, with how to make each one's handler.
    /// </summary>
    /// <remarks>
    /// The factory is a lambda rather than a type reference so that nothing in
    /// this table mentions a format type at all - mentioning one would be
    /// enough for the runtime to load its assembly when the table is first
    /// touched, which happens when the Open dialog is built.
    /// </remarks>
    private sealed record Entry(FormatInfo Info, Lazy<IFormatHandler> Handler);

    private static readonly Entry[] Formats =
    [
        new(
            new FormatInfo(
                "Word Document",
                [".docx"],
                CanRead: true,
                CanWrite: true,
                Capabilities.CharFormat | Capabilities.Fonts | Capabilities.Colors
                | Capabilities.Headings | Capabilities.Lists | Capabilities.Tables
                | Capabilities.Images | Capabilities.Links | Capabilities.PageSetup),
            new Lazy<IFormatHandler>(() => new Docx.DocxFormat(), LazyThreadSafetyMode.ExecutionAndPublication)),

        new(
            new FormatInfo(
                "Plain Text",
                [".txt"],
                CanRead: true,
                CanWrite: true,
                Capabilities.None),
            new Lazy<IFormatHandler>(() => new TxtFormat(), LazyThreadSafetyMode.ExecutionAndPublication)),
    ];

    /// <summary>Every registered format, in dialog order.</summary>
    public static IReadOnlyList<FormatInfo> All { get; } = Formats.Select(f => f.Info).ToList();

    /// <summary>The handler for an extension, created on first use.</summary>
    public static IFormatHandler? For(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return null;
        }

        var wanted = Normalize(extension);

        foreach (var format in Formats)
        {
            foreach (var candidate in format.Info.Extensions)
            {
                if (string.Equals(candidate, wanted, StringComparison.OrdinalIgnoreCase))
                {
                    return format.Handler.Value;
                }
            }
        }

        return null;
    }

    /// <summary>The format for an extension, without creating its handler.</summary>
    public static FormatInfo? InfoFor(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return null;
        }

        var wanted = Normalize(extension);

        return All.FirstOrDefault(
            f => f.Extensions.Any(e => string.Equals(e, wanted, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// Whether this is the old Word format, which is deliberately not
    /// supported (Section 7.2).
    /// </summary>
    public static bool IsLegacyWord(string path) =>
        Normalize(Path.GetExtension(path)) == ".doc";

    /// <summary>What to say about a .doc, which is refused rather than attempted.</summary>
    public const string LegacyWordMessage =
        "Old Word .doc files aren't supported. Open it in Word or LibreOffice and "
        + "save it as .docx, then open that.";

    /// <summary>
    /// The Open dialog filter. The first entry is "All supported files".
    /// Builds from the static table only, so no package loads.
    /// </summary>
    public static string OpenFilter()
    {
        var readable = All.Where(f => f.CanRead).ToList();

        var all = string.Join(
            ';',
            readable.SelectMany(f => f.Extensions).Select(e => "*" + e));

        var filter = new StringBuilder();
        filter.Append("All supported files|").Append(all);

        foreach (var format in readable)
        {
            filter.Append('|').Append(Describe(format));
        }

        filter.Append("|All files|*.*");

        return filter.ToString();
    }

    /// <summary>The Save As dialog filter, writable formats only.</summary>
    public static string SaveFilter() =>
        string.Join('|', All.Where(f => f.CanWrite).Select(Describe));

    /// <summary>
    /// Which format a Save As filter index means, so the chosen extension can
    /// be applied when the user did not type one.
    /// </summary>
    public static FormatInfo? SaveFormatAt(int oneBasedIndex)
    {
        var writable = All.Where(f => f.CanWrite).ToList();

        return oneBasedIndex >= 1 && oneBasedIndex <= writable.Count
            ? writable[oneBasedIndex - 1]
            : null;
    }

    /// <summary>
    /// What a save in this format would lose, as a list for the warning
    /// dialog (Section 7.1).
    /// </summary>
    public static IReadOnlyList<string> WhatWouldBeLost(Capabilities used, Capabilities supported)
    {
        var lost = used & ~supported;
        var names = new List<string>();

        if (lost.HasFlag(Capabilities.CharFormat))
        {
            names.Add("bold, italic and underline");
        }

        if (lost.HasFlag(Capabilities.Fonts))
        {
            names.Add("font names and sizes");
        }

        if (lost.HasFlag(Capabilities.Colors))
        {
            names.Add("colours and highlighting");
        }

        if (lost.HasFlag(Capabilities.Headings))
        {
            names.Add("heading styles");
        }

        if (lost.HasFlag(Capabilities.Lists))
        {
            names.Add("lists");
        }

        if (lost.HasFlag(Capabilities.Tables))
        {
            names.Add("tables");
        }

        if (lost.HasFlag(Capabilities.Images))
        {
            names.Add("images");
        }

        if (lost.HasFlag(Capabilities.Links))
        {
            names.Add("links");
        }

        if (lost.HasFlag(Capabilities.PageSetup))
        {
            names.Add("page setup");
        }

        return names;
    }

    private static string Describe(FormatInfo format) =>
        $"{format.Name} ({string.Join("; ", format.Extensions.Select(e => "*" + e))})"
        + $"|{string.Join(';', format.Extensions.Select(e => "*" + e))}";

    private static string Normalize(string extension)
    {
        var text = extension.Trim();

        return text.StartsWith('.') ? text.ToLowerInvariant() : "." + text.ToLowerInvariant();
    }
}
