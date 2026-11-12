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
/// Phase 6 - lists, tables, images, links
/// <list type="bullet">
///   <item>ListAndTableTests: toggling and nesting lists, the marker per level, Tab and Shift+Tab, Enter on an empty item, restart and continue numbering; inserting a table, rows and columns in and out, merging and splitting, shading, borders, Tab between cells.</item>
///   <item>ImageAndLinkTests: a picture keeps its exact bytes, is scaled to the text column, is stored once per document however many times it is used; the properties dialog; the rule about which link schemes open without asking.</item>
///   <item>EditedContentRoundTripTests: everything the editing commands build, written and read back in every format that claims to carry it. This is the phase gate.</item>
/// </list>
///
/// Phase 7 - tabs, find and replace, spell check
/// <list type="bullet">
///   <item>FindEngineTests: match case, whole word, regular expressions, a match that crosses runs, matches that stop at a paragraph, a line break and a picture, replace keeping the first character's formatting, Replace All as one undo step.</item>
///   <item>TabTests: a tab builds no editor until it is shown, only the active editor is visible, undo survives a tab switch, closing a tab picks the right neighbour, reordering, the same file finds its own tab, Ctrl+Tab wraps round, and the strip's names, dots and tooltips.</item>
///   <item>SpellCheckTests: the .lex file's header and encoding, adding words, the languages Windows reports, Code paragraphs marked as having no linguistic content, and the size above which checking is left off.</item>
/// </list>
///
/// Phase 8 - recovery, AutoSave, printing, single instance, Preferences
/// <list type="bullet">
///   <item>RecoveryTests: the lock cannot be opened while a run holds it and can be once that run is gone, proved against a real process that is killed; this run's own folder is never offered back; an untitled document is included; a damaged file is skipped without hiding a good one beside it; an abandoned folder with nothing in it is tidied away.</item>
///   <item>IrJsonTests (Core): the IR survives a JSON round trip, which nothing had ever asked it to do before - the font list could not be deserialized at all.</item>
///   <item>AutoSaveTests: off by default; each Section 6.13.2 skip rule - never saved, would lose formatting, read-only, changed on disk - with the cases that are fine beside each one; the idle pause, the longer pause for a document whose walk is slow, and the reason reported once rather than every tick.</item>
///   <item>PrintingTests: the phase gate. A laid-out page's glyph origins all fall in the left half of the sheet, which is the only way to tell one column from two; a control test shows WPF really does use columns when a column width is set; the copy carries formatting and pictures and leaves the document on screen untouched.</item>
///   <item>SingleInstanceTests: the names are stable per installation and per user; a second claim fails and a release lets the next one through; files really cross the pipe, a relative path arrives absolute, and a hand-off with nobody listening gives up in about three seconds.</item>
///   <item>PhaseEightDialogTests: Preferences gives back exactly what it was given, clamps an interval and refuses a word; Page Setup shows inches and stores points; the recovery dialog's Restore with nothing ticked means Later rather than Discard.</item>
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
