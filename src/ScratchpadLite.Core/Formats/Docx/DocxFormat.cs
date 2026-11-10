using ScratchpadLite.Core.Model;

namespace ScratchpadLite.Core.Formats.Docx;

/// <summary>
/// Word documents through the Open XML SDK (Section 7.2). This type is
/// created only by the lazy FormatRegistry, so the SDK assembly is not
/// loaded at startup (Section 2.1).
/// </summary>
/// <remarks>
/// The style sheet is a property rather than a constructor argument because
/// FormatRegistry creates handlers with no arguments, and the sheet is not
/// known until the app has read data/styles.json. Reading or writing without
/// setting it uses the built-in sheet, which is right for the tests and for a
/// first run.
/// </remarks>
public sealed class DocxFormat : IFormatHandler
{
    /// <summary>
    /// The style sheet to match against. Set by the app once styles.json has
    /// been read.
    /// </summary>
    public StyleSheet Styles { get; set; } = StyleSheet.Default;

    public string Name => "Word Document";

    public IReadOnlyList<string> Extensions => [".docx"];

    public bool CanRead => true;

    public bool CanWrite => true;

    public Capabilities Capabilities =>
        Capabilities.CharFormat | Capabilities.Fonts | Capabilities.Colors
        | Capabilities.Headings | Capabilities.Lists | Capabilities.Tables
        | Capabilities.Images | Capabilities.Links | Capabilities.PageSetup;

    public ReadResult Read(Stream input, string sourcePath, CancellationToken ct) =>
        DocxReader.Read(input, Styles, ct);

    public void Write(SpDocument doc, Stream output, string targetPath, CancellationToken ct) =>
        DocxWriter.Write(doc, output, Styles, ct);
}
