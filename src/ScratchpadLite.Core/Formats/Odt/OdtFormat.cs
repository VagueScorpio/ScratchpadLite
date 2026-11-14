using ScratchpadLite.Core.Model;

namespace ScratchpadLite.Core.Formats.Odt;

/// <summary>
/// OpenDocument text, hand-written on ZipArchive and System.Xml.Linq
/// (Section 7.2). No package: nothing maintained and free covers ODT, and the
/// parts of it this app needs are a zip of XML.
/// </summary>
/// <remarks>
/// The test that matters for this format cannot be automated here: a file this
/// writes has to open in LibreOffice without a repair prompt. The blueprint
/// says so and says the check is done by hand, because the app neither uses
/// nor tests against LibreOffice.
/// </remarks>
public sealed class OdtFormat : IFormatHandler
{
    public StyleSheet Styles { get; set; } = StyleSheet.Default;

    public string Name => "OpenDocument Text";

    public IReadOnlyList<string> Extensions => [".odt"];

    public bool CanRead => true;

    public bool CanWrite => true;

    /// <summary>Everything, like .docx (Section 7.3).</summary>
    public Capabilities Capabilities =>
        Capabilities.CharFormat | Capabilities.Underline | Capabilities.Fonts
        | Capabilities.Colors | Capabilities.Headings | Capabilities.Lists
        | Capabilities.Tables | Capabilities.Images | Capabilities.Links
        | Capabilities.PageSetup;

    public ReadResult Read(Stream input, string sourcePath, CancellationToken ct) =>
        OdtReader.Read(input, Styles, ct);

    public void Write(
        SpDocument doc,
        Stream output,
        string targetPath,
        CancellationToken ct,
        WriteOptions? options = null) =>
        OdtWriter.Write(doc, output, Styles, ct, options ?? WriteOptions.None);
}
