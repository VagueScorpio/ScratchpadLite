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
/// <para>Phase 4.</para>
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

    /// <summary>Every registered format, in dialog order.</summary>
    public static IReadOnlyList<FormatInfo> All => throw new NotImplementedException("Phase 4");

    /// <summary>The handler for an extension, created on first use.</summary>
    public static IFormatHandler? For(string extension) => throw new NotImplementedException("Phase 4");

    /// <summary>
    /// The Open dialog filter. The first entry is "All supported files".
    /// Builds from the static table only, so no package loads.
    /// </summary>
    public static string OpenFilter() => throw new NotImplementedException("Phase 4");

    /// <summary>The Save As dialog filter, writable formats only.</summary>
    public static string SaveFilter() => throw new NotImplementedException("Phase 4");
}
