using System.IO;
using System.Windows.Documents;

namespace ScratchpadLite.App.Formats;

/// <summary>
/// The one exception to the warehouse rule (Section 4.1): a format that
/// loads straight onto the floor instead of onto a pallet, because WPF
/// already converts it. Only RTF uses this.
/// </summary>
/// <remarks>Phase 5.</remarks>
internal interface IFlowFormatHandler
{
    string Name { get; }

    IReadOnlyList<string> Extensions { get; }

    void Load(FlowDocument document, Stream input);

    void Save(FlowDocument document, Stream output);
}
