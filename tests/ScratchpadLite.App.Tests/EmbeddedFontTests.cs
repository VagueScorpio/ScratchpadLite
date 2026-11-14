using System.IO;
using System.Windows.Documents;
using ScratchpadLite.App.Bridge;
using ScratchpadLite.App.Fonts;
using ScratchpadLite.Core.Fonts;
using ScratchpadLite.Core.Model;
using ScratchpadLite.Core.Platform;
using Xunit;

using IrParagraph = ScratchpadLite.Core.Model.Paragraph;
using WpfParagraph = System.Windows.Documents.Paragraph;

namespace ScratchpadLite.App.Tests;

/// <summary>
/// A font that came inside a document, loaded for that document alone
/// (Section 7.4).
/// </summary>
/// <remarks>
/// <para>The rule being tested is the one that is easy to get wrong and
/// quiet when it is: an embedded font belongs to the document it arrived in.
/// Two documents can carry different fonts under the same family name, and
/// a font the user never installed must not turn up in the picker or apply
/// itself to another tab.</para>
///
/// <para>A real font file is needed, because WPF will not register a family
/// it cannot read, so these borrow one of Windows' own. Nothing is copied
/// into the repository and nothing is redistributed: the bytes go to a temp
/// folder and are deleted.</para>
/// </remarks>
[Collection(nameof(AppHost))]
public class EmbeddedFontTests : IDisposable
{
    private static readonly string WindowsFont =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts", "arial.ttf");

    private readonly List<Guid> _documents = [];

    private static bool Available() => File.Exists(WindowsFont);

    private IReadOnlyList<(EmbeddedFont Font, string Path)> Unpack(Guid document, string family)
    {
        AppPaths.Initialize();
        _documents.Add(document);

        var bytes = File.ReadAllBytes(WindowsFont);

        return FontEmbedding.Unpack([new EmbeddedFont(family, FontFace.Regular, bytes)], document);
    }

    [StaFact]
    public void AnEmbeddedFontResolvesForItsOwnDocument()
    {
        if (!Available())
        {
            return;
        }

        var document = Guid.NewGuid();

        var loaded = AppHost.Run(() => FontRegistry.UseEmbedded(document, Unpack(document, "Arial")));

        Assert.Equal(1, loaded);

        var family = AppHost.Run(() => FontRegistry.Resolve("Arial", document));

        // A family loaded from a file says so in its source: "./file.ttf#Arial"
        // relative to the folder it was unpacked into.
        Assert.Contains("#", family.Source, StringComparison.Ordinal);
        Assert.EndsWith("#Arial", family.Source, StringComparison.Ordinal);
    }

    [StaFact]
    public void AnotherDocumentDoesNotSeeIt()
    {
        if (!Available())
        {
            return;
        }

        var mine = Guid.NewGuid();
        var theirs = Guid.NewGuid();

        AppHost.Run(() => FontRegistry.UseEmbedded(mine, Unpack(mine, "Arial")));

        var elsewhere = AppHost.Run(() => FontRegistry.Resolve("Arial", theirs));
        var nowhere = AppHost.Run(() => FontRegistry.Resolve("Arial"));

        Assert.Equal("Arial", elsewhere.Source);
        Assert.Equal("Arial", nowhere.Source);
    }

    [StaFact]
    public void ClosingTheDocumentStopsItResolving()
    {
        if (!Available())
        {
            return;
        }

        var document = Guid.NewGuid();

        AppHost.Run(() => FontRegistry.UseEmbedded(document, Unpack(document, "Arial")));
        Assert.True(FontRegistry.HasEmbedded(document));

        FontRegistry.ForgetEmbedded(document);

        Assert.False(FontRegistry.HasEmbedded(document));
        Assert.Equal("Arial", AppHost.Run(() => FontRegistry.Resolve("Arial", document)).Source);
    }

    [StaFact]
    public void AnEmbeddedFontIsNotOfferedInThePicker()
    {
        if (!Available())
        {
            return;
        }

        var document = Guid.NewGuid();

        AppHost.Run(() => FontRegistry.UseEmbedded(document, Unpack(document, "Sp Test Embedded")));

        // It is the document's font, not something the user installed. The
        // folder list is what the picker shows under "Font folder".
        Assert.DoesNotContain("Sp Test Embedded", AppHost.Run(FontRegistry.FolderFamilyNames));
    }

    [StaFact]
    public void TheDocumentIsBuiltWithTheFontItCarried()
    {
        if (!Available())
        {
            return;
        }

        var document = Guid.NewGuid();

        AppHost.Run(() => FontRegistry.UseEmbedded(document, Unpack(document, "Arial")));

        var ir = new SpDocument(
            [new IrParagraph { Inlines = [new TextRun("hello", new CharStyle { Family = "Arial" })] }],
            new Dictionary<string, string>(),
            new PageSetup(),
            new HashSet<string> { "Arial" });

        var source = AppHost.Run(() =>
        {
            var flow = FlowBuilder.Build(ir, StyleSheet.Default, document);

            return flow.Blocks.OfType<WpfParagraph>()
                .SelectMany(p => p.Inlines.OfType<Run>())
                .First()
                .FontFamily.Source;
        });

        // The whole point: the run uses the file that came in the document,
        // not the Arial that happens to be on this PC.
        Assert.EndsWith("#Arial", source, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);

        foreach (var document in _documents)
        {
            FontRegistry.ForgetEmbedded(document);

            try
            {
                // WPF holds the file open once the family is registered, so
                // this often cannot be deleted until the process ends - which
                // is exactly why the app tidies these at launch instead.
                Directory.Delete(FontEmbedding.FolderFor(document), recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
