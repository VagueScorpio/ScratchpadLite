using ScratchpadLite.Core.Model;

namespace ScratchpadLite.Core.Formats.Odt;

/// <summary>
/// ODT to IR (Section 7.2). Checks the mimetype entry, parses styles.xml
/// and the automatic styles in content.xml through OdtStyleMap, then walks
/// headings, paragraphs, spans, lists, tables, image frames, links, line
/// breaks, spaces and tabs. Page setup comes from the page layout in
/// styles.xml.
/// </summary>
/// <remarks>Phase 5.</remarks>
internal static class OdtReader
{
    internal static ReadResult Read(Stream input, string sourcePath, CancellationToken ct) =>
        throw new NotImplementedException("Phase 5");
}
