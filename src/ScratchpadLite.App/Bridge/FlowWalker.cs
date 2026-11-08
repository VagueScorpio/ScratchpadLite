using System.Windows.Documents;
using ScratchpadLite.Core.Model;

namespace ScratchpadLite.App.Bridge;

/// <summary>
/// The forklift in the other direction: FlowDocument to IR (Section 6.4).
/// UI thread only. Walks the blocks in order, turns List and ListItem
/// nesting back into paragraphs carrying list info, reads effective
/// inherited values for each run and compares them with the paragraph style
/// to decide what counts as direct formatting, and collects the fonts used.
///
/// Fonts from the font folder are recorded under their plain family name,
/// never the file URI with the fragment, or saved documents would carry a
/// path from this PC.
///
/// The most important test in the project (Section 6.4): for any IR
/// document d, walking what the builder built equals d after
/// normalization.
/// </summary>
/// <remarks>Phase 4.</remarks>
internal static class FlowWalker
{
    internal static SpDocument Walk(FlowDocument document) => throw new NotImplementedException("Phase 4");
}
