using ScratchpadLite.Core.Model;

namespace ScratchpadLite.Core.Formats.Odt;

/// <summary>
/// OpenDocument Text, hand-written on top of ZipArchive and XDocument
/// (Section 7.2), because no maintained free .NET ODT library exists.
/// </summary>
/// <remarks>Phase 5.</remarks>
public sealed class OdtFormat : IFormatHandler
{
    public string Name => "OpenDocument Text";

    public IReadOnlyList<string> Extensions => [".odt"];

    public bool CanRead => true;

    public bool CanWrite => true;

    public Capabilities Capabilities => Capabilities.CharFormat | Capabilities.Fonts | Capabilities.Colors | Capabilities.Headings | Capabilities.Lists | Capabilities.Tables | Capabilities.Images | Capabilities.Links | Capabilities.PageSetup;

    public ReadResult Read(Stream input, string sourcePath, CancellationToken ct) =>
        throw new NotImplementedException("Phase 5");

    public void Write(SpDocument doc, Stream output, string targetPath, CancellationToken ct) =>
        throw new NotImplementedException("Phase 5");
}
