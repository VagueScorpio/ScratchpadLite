namespace ScratchpadLite.Core.Tests;

/// <summary>
/// The test map for this project (Section 10.1). Each phase fills in the class
/// named here; the list lives in code so a phase gate can be checked against it
/// rather than against memory.
/// </summary>
/// <remarks>
/// Phase 4 - formats
/// <list type="bullet">
///   <item>TxtFormatTests: encoding detection by BOM, strict UTF-8, then Windows-1252; line endings.</item>
///   <item>DocxFormatTests: read every fixture, then IR to write to read and compare, limited to the Capabilities of DOCX.</item>
///   <item>DocxValidationTests: every written .docx passes OpenXmlValidator with zero errors.</item>
///   <item>IrJsonTests: every IR type survives a JSON round trip, image bytes included.</item>
///   <item>LimitsTests: a zip bomb is rejected, a path-traversal entry is rejected, an oversize file is rejected.</item>
/// </list>
///
/// Phase 5 - the other formats
/// <list type="bullet">
///   <item>MarkdownFormatTests: round trip within Capabilities; special characters are escaped.</item>
///   <item>OdtFormatTests: mimetype is the first entry and uncompressed; the XML is well formed; style references resolve.</item>
/// </list>
///
/// Phase 2 - fonts
/// <list type="bullet">
///   <item>FontScannerTests: every type is sorted correctly by magic bytes even when the extension lies; oversize files are skipped.</item>
///   <item>FontIndexTests: a second scan of an unchanged folder opens no font file (counted through a test hook); a changed file is reprocessed; a deleted file loses its entry and its cache file; a corrupt index is rebuilt.</item>
///   <item>WoffDecoderTests and Woff2DecoderTests: decode fixtures and compare tables with the original TTF; truncated and corrupt files throw cleanly and never hang.</item>
///   <item>ZipFontSourceTests: fonts inside a zip load; a zip bomb and a traversal entry are rejected; nested zips past one level are ignored.</item>
///   <item>FontLicenseReportTests: flags a fixture with no license, and one whose license text forbids redistribution.</item>
/// </list>
///
/// Phase 3 - the converted font types
/// <list type="bullet">
///   <item>Type1ConverterTests: glyph count matches the source; advance widths match the AFM; cmap covers A-Z and a-z; the .pfm names group regular, bold and italic into one family; a symbol font gets a (3,0) cmap.</item>
///   <item>MacFontExtractorTests, EotDecoderTests, Type42ExtractorTests: decode fixtures and compare tables; corrupt files throw cleanly.</item>
/// </list>
/// </remarks>
internal static class TestPlan;
