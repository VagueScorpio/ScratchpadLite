using ScratchpadLite.Core.Model;

namespace ScratchpadLite.Core.Formats.Markdown;

/// <summary>
/// Walks the Markdig syntax tree into IR (Section 7.2): headings to
/// Heading 1-6, emphasis to bold and italic, double tilde to strikethrough,
/// lists, pipe tables, links, images, code blocks to the Code style, block
/// quotes to Quote, and thematic breaks to a horizontal rule.
/// </summary>
/// <remarks>Phase 5.</remarks>
internal static class MarkdownReader
{
    internal static ReadResult Read(string markdown, string sourcePath) =>
        throw new NotImplementedException("Phase 5");
}
