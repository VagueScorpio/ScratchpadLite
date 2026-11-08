using ScratchpadLite.Core.Model;

namespace ScratchpadLite.Core.Formats.Docx;

/// <summary>
/// DOCX to IR (Section 7.2). Iterates the body children in order so
/// paragraphs and tables keep their sequence, resolves style inheritance
/// from the style definitions part and the document defaults, matches
/// paragraph styles by name rather than id (ids vary by language), reads
/// numbering definitions for lists, image parts for pictures, relationship
/// targets for hyperlinks, and the last section properties for page setup.
/// </summary>
/// <remarks>Phase 4.</remarks>
internal static class DocxReader
{
    internal static ReadResult Read(Stream input, string sourcePath, CancellationToken ct) =>
        throw new NotImplementedException("Phase 4");
}
