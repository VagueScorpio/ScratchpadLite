using System.Windows.Documents;

namespace ScratchpadLite.App.Editor;

/// <summary>
/// Find and replace over the document (Section 6.10), walking TextPointer
/// positions so matches that span runs are still found. Supports match
/// case, whole word, wrap-around, replace and replace all, and reports the
/// count. Replace all is one undo unit.
/// </summary>
/// <remarks>Phase 7.</remarks>
internal static class FindEngine
{
    internal static TextRange? FindNext(
        FlowDocument document,
        TextPointer from,
        string needle,
        bool matchCase,
        bool wholeWord) => throw new NotImplementedException("Phase 7");
}
