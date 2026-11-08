using ScratchpadLite.Core.Model;

namespace ScratchpadLite.Core.Formats;

/// <summary>
/// Plain text (Section 7.2). Reads a BOM, then tries strict UTF-8, then
/// Windows-1252. Writes UTF-8 without a BOM and CRLF line endings by
/// default. Tables become tab-separated rows; list items get a bullet or
/// number prefix.
/// </summary>
/// <remarks>Phase 4.</remarks>
public sealed class TxtFormat : IFormatHandler
{
    public string Name => "Plain Text";

    public IReadOnlyList<string> Extensions => [".txt"];

    public bool CanRead => true;

    public bool CanWrite => true;

    public Capabilities Capabilities => Capabilities.None;

    public ReadResult Read(Stream input, string sourcePath, CancellationToken ct) =>
        throw new NotImplementedException("Phase 4");

    public void Write(SpDocument doc, Stream output, string targetPath, CancellationToken ct) =>
        throw new NotImplementedException("Phase 4");
}
