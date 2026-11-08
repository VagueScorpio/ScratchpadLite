using ScratchpadLite.Core.Model;

namespace ScratchpadLite.Core.Formats.Odt;

/// <summary>
/// Resolves ODT style inheritance through the parent style name chain and
/// turns the text properties into IR character and paragraph formatting
/// (Section 7.2). Font names resolve through the font face declarations.
/// </summary>
/// <remarks>Phase 5.</remarks>
internal sealed class OdtStyleMap
{
    internal CharStyle CharStyleFor(string styleName) => throw new NotImplementedException("Phase 5");

    internal Paragraph ParagraphStyleFor(string styleName) => throw new NotImplementedException("Phase 5");
}
