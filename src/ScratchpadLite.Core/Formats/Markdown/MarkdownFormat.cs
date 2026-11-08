using ScratchpadLite.Core.Model;

namespace ScratchpadLite.Core.Formats.Markdown;

/// <summary>
/// Markdown (Section 7.2). Read with Markdig using the advanced extensions,
/// written by the hand-written MarkdownWriter. Images are saved to a sidecar
/// folder next to the file; web images are never downloaded unless the user
/// clicks Load web images in the warning bar.
/// </summary>
/// <remarks>Phase 5.</remarks>
public sealed class MarkdownFormat : IFormatHandler
{
    public string Name => "Markdown";

    public IReadOnlyList<string> Extensions => [".md"];

    public bool CanRead => true;

    public bool CanWrite => true;

    public Capabilities Capabilities => Capabilities.Headings | Capabilities.Lists | Capabilities.Tables | Capabilities.Images | Capabilities.Links | Capabilities.CharFormat;

    public ReadResult Read(Stream input, string sourcePath, CancellationToken ct) =>
        throw new NotImplementedException("Phase 5");

    public void Write(SpDocument doc, Stream output, string targetPath, CancellationToken ct) =>
        throw new NotImplementedException("Phase 5");
}
