using ScratchpadLite.Core.Fonts;
using ScratchpadLite.Core.Model;

namespace ScratchpadLite.Core.Formats;

/// <summary>What survives a save in a given format (Section 7.1).</summary>
[Flags]
public enum Capabilities
{
    None = 0,

    /// <summary>Bold, italic and strikethrough.</summary>
    CharFormat = 1,
    Fonts = 2,
    Colors = 4,
    Headings = 8,
    Lists = 16,
    Tables = 32,
    Images = 64,
    Links = 128,
    PageSetup = 256,

    /// <summary>
    /// Underline, which is its own flag because Markdown keeps bold and
    /// italic and has no underline at all. Folding the two together would
    /// make the save warning claim a Markdown save loses bold.
    /// </summary>
    Underline = 512,
}

public sealed record ReadResult(SpDocument Document, IReadOnlyList<string> Warnings)
{
    /// <summary>
    /// The fonts the file carried with it, already deobfuscated (Section
    /// 7.4). Beside the document rather than inside it: the IR describes what
    /// a document says, and these are what it brought along. They are also
    /// too big to want in the recovery copy the IR is serialized into.
    /// </summary>
    public IReadOnlyList<EmbeddedFont> EmbeddedFonts { get; init; } = [];
}

/// <summary>
/// What a save needs to know beyond the document itself (Section 7.4).
/// </summary>
/// <remarks>
/// A parameter rather than a property on the handler, because the handlers
/// are shared: <see cref="FormatRegistry"/> makes one of each, lazily, and
/// two tabs can be saving at the same moment. A style sheet can live on the
/// handler because it is the same for every document; the fonts to embed are
/// this document's.
/// </remarks>
public sealed record WriteOptions(IReadOnlyList<EmbeddedFont> Fonts)
{
    /// <summary>Nothing to embed, which is what most saves want.</summary>
    public static readonly WriteOptions None = new([]);
}

/// <summary>
/// One loading dock (Section 4.1): it converts between its own file format and
/// the IR, and never touches WPF.
/// </summary>
public interface IFormatHandler
{
    /// <summary>Display name, for example "Word Document".</summary>
    string Name { get; }

    /// <summary>Extensions including the dot, for example ".docx".</summary>
    IReadOnlyList<string> Extensions { get; }

    bool CanRead { get; }

    bool CanWrite { get; }

    Capabilities Capabilities { get; }

    ReadResult Read(Stream input, string sourcePath, CancellationToken ct);

    /// <param name="options">
    /// Fonts to embed and anything else a save needs. Null means none, which
    /// is what every format but DOCX and ODT does with it anyway.
    /// </param>
    void Write(
        SpDocument doc,
        Stream output,
        string targetPath,
        CancellationToken ct,
        WriteOptions? options = null);
}
