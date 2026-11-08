using System.Windows.Documents;

namespace ScratchpadLite.App.Services;

/// <summary>
/// Print and export to PDF (Section 6.17). Printing paginates the
/// FlowDocument for the chosen paper size and margins. PDF export goes
/// through the Microsoft Print to PDF printer that ships with Windows 10 and
/// 11, so nothing extra is installed (R1).
///
/// Both must come out in one column, which is the Phase 8 gate test.
/// </summary>
/// <remarks>Phase 8.</remarks>
internal static class Printing
{
    internal static void Print(FlowDocument document, string documentName) =>
        throw new NotImplementedException("Phase 8");

    internal static void ExportPdf(FlowDocument document, string targetPath) =>
        throw new NotImplementedException("Phase 8");
}
