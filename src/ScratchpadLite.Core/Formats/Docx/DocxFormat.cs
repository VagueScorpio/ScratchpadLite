using ScratchpadLite.Core.Model;

namespace ScratchpadLite.Core.Formats.Docx;

/// <summary>
/// Word documents through the Open XML SDK (Section 7.2). This type is
/// created only by the lazy FormatRegistry, so the SDK assembly is not
/// loaded at startup (Section 2.1).
/// </summary>
/// <remarks>Phase 4.</remarks>
public sealed class DocxFormat : IFormatHandler
{
    public string Name => "Word Document";

    public IReadOnlyList<string> Extensions => [".docx"];

    public bool CanRead => true;

    public bool CanWrite => true;

    public Capabilities Capabilities => Capabilities.CharFormat | Capabilities.Fonts | Capabilities.Colors | Capabilities.Headings | Capabilities.Lists | Capabilities.Tables | Capabilities.Images | Capabilities.Links | Capabilities.PageSetup;

    public ReadResult Read(Stream input, string sourcePath, CancellationToken ct) =>
        throw new NotImplementedException("Phase 4");

    public void Write(SpDocument doc, Stream output, string targetPath, CancellationToken ct) =>
        throw new NotImplementedException("Phase 4");
}
