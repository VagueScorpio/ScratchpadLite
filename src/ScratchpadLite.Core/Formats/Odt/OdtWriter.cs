using ScratchpadLite.Core.Model;

namespace ScratchpadLite.Core.Formats.Odt;

/// <summary>
/// IR to ODT (Section 7.2). The mimetype entry must be written first and
/// stored uncompressed, followed by the manifest, meta.xml, styles.xml with
/// the named styles and page layout, content.xml with deduplicated
/// automatic styles, a minimal settings.xml, and images under Pictures.
/// </summary>
/// <remarks>Phase 5.</remarks>
internal static class OdtWriter
{
    internal static void Write(SpDocument doc, Stream output, CancellationToken ct) =>
        throw new NotImplementedException("Phase 5");
}
