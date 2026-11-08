namespace ScratchpadLite.Core.Fonts;

/// <summary>
/// Embedded OpenType to sfnt (Section 6.6.3). Uncompressed and XOR-masked
/// files are recovered by stripping the EOT header; MicroType Express
/// compressed files are unsupported and reported with the reason
/// (Section 13).
/// </summary>
/// <remarks>Phase 3.</remarks>
public static class EotDecoder
{
    public static byte[] ToSfnt(Stream eot) => throw new NotImplementedException("Phase 3");
}
