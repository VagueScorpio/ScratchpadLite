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

public sealed record ReadResult(SpDocument Document, IReadOnlyList<string> Warnings);

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

    void Write(SpDocument doc, Stream output, string targetPath, CancellationToken ct);
}
