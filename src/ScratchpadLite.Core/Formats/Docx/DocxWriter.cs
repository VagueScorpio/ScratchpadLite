using ScratchpadLite.Core.Model;

namespace ScratchpadLite.Core.Formats.Docx;

/// <summary>
/// IR to DOCX (Section 7.2). Writes a full style definitions part, a
/// numbering definitions part with one abstract bullet and one abstract
/// number definition of nine levels each, images as image parts, and the
/// core file properties. Arbitrary highlight colors use shading fill,
/// because the highlight element allows only sixteen colors. Every file it
/// writes is re-opened by OpenXmlValidator in the tests.
/// </summary>
/// <remarks>Phase 4.</remarks>
internal static class DocxWriter
{
    internal static void Write(SpDocument doc, Stream output, CancellationToken ct) =>
        throw new NotImplementedException("Phase 4");
}
