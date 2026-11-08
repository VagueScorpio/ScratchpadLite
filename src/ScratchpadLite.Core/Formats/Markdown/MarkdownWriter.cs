using ScratchpadLite.Core.Model;

namespace ScratchpadLite.Core.Formats.Markdown;

/// <summary>
/// Hand-written IR to Markdown writer (Section 7.2). Escapes Markdown
/// special characters in text and writes pipe tables; merged cells are
/// split and reported as a warning.
/// </summary>
/// <remarks>Phase 5.</remarks>
internal static class MarkdownWriter
{
    internal static string Write(SpDocument doc, string targetPath, out IReadOnlyList<string> warnings) =>
        throw new NotImplementedException("Phase 5");
}
