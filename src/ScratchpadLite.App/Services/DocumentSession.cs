using System.Windows.Documents;
using ScratchpadLite.Core.Model;

namespace ScratchpadLite.App.Services;

/// <summary>
/// One open document: its file path, format, FlowDocument, dirty flag, undo
/// state, recovery file and the warnings shown when it was opened
/// (Section 6.12). A tab owns exactly one of these.
/// </summary>
/// <remarks>Phase 4.</remarks>
internal sealed class DocumentSession
{
    internal string? FilePath { get; set; }

    internal string Title => throw new NotImplementedException("Phase 4");

    internal bool IsDirty { get; set; }

    internal FlowDocument? Document { get; set; }

    internal SpDocument? LastSavedIr { get; set; }
}
