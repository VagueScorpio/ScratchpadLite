using System.IO;
using System.Windows.Documents;

namespace ScratchpadLite.App.Formats;

/// <summary>
/// RTF through the converter built into WPF (Section 7.2). Loads and saves
/// a TextRange with the RTF data format, on the UI thread. After loading,
/// the walker still runs so there is IR for recovery and the save warning.
///
/// The built-in converter does not keep paragraph style names, so headings
/// are guessed after loading: a paragraph whose size and weight match a
/// style sheet heading exactly gets that style name. Saving writes headings
/// out as directly formatted text, which is what the fidelity matrix
/// records.
/// </summary>
/// <remarks>Phase 5.</remarks>
internal sealed class RtfFormat : IFlowFormatHandler
{
    public string Name => "Rich Text Format";

    public IReadOnlyList<string> Extensions => [".rtf"];

    public void Load(FlowDocument document, Stream input) => throw new NotImplementedException("Phase 5");

    public void Save(FlowDocument document, Stream output) => throw new NotImplementedException("Phase 5");
}
