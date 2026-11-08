using System.Windows.Documents;
using ScratchpadLite.Core.Model;

namespace ScratchpadLite.App.Bridge;

/// <summary>
/// The forklift that moves a pallet onto the warehouse floor: IR to
/// FlowDocument (Section 4.1 and 6.4). UI thread only, because a
/// FlowDocument belongs to the thread that owns the window. Everything
/// before this step can run in the background, which is how a large file
/// opens without freezing the window.
///
/// Font sizes convert points to device-independent pixels, consecutive
/// paragraphs that share list info nest into List and ListItem elements,
/// images keep their original bytes and MIME type in the Image tag so a
/// save writes them back untouched, and every brush and pen it creates is
/// frozen.
/// </summary>
/// <remarks>Phase 4.</remarks>
internal static class FlowBuilder
{
    /// <summary>Awaits FontRegistry.IndexReady first, so cached fonts resolve (Section 4.3).</summary>
    internal static FlowDocument Build(SpDocument doc) => throw new NotImplementedException("Phase 4");
}
