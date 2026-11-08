namespace ScratchpadLite.Core.Fonts.Type1;

/// <summary>
/// Assembles tables into a valid sfnt file: the table directory, four-byte
/// alignment with padding, per-table checksums and the checkSumAdjustment
/// in head (Section 6.6.4). Shared by every decoder and converter, which is
/// why it lands in Phase 2 with the WOFF decoders rather than Phase 3.
/// </summary>
/// <remarks>Phase 2.</remarks>
public static class SfntBuilder
{
    public static byte[] Build(IReadOnlyDictionary<string, byte[]> tables, bool cffOutlines) =>
        throw new NotImplementedException("Phase 2");
}
