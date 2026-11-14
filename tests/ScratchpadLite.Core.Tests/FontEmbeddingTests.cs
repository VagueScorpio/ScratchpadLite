using ScratchpadLite.Core.Fonts;
using Xunit;

namespace ScratchpadLite.Core.Tests;

/// <summary>
/// Which fonts may travel with a document, and why the others may not
/// (Section 7.4).
/// </summary>
/// <remarks>
/// The rules here are somebody else's: the foundry's fsType, and the fact
/// that a converted Type 1 font is this program's own work rather than
/// anything that was licensed. So the tests are about obeying them exactly,
/// including the cases where a font contradicts itself.
/// </remarks>
public class FontEmbeddingTests
{
    private static FontIndex.Entry Entry(
        string family,
        ushort fsType = 0,
        FontFileKind kind = FontFileKind.Direct,
        string face = "Regular",
        string? cached = null) =>
        new(
            SourcePath: $@"C:\fonts\{family}-{face}.ttf",
            Length: 1024,
            ModifiedUtc: DateTime.UnixEpoch,
            Sha1: family + face,
            CachedFile: cached ?? $"{family}-{face}.ttf",
            Families: [family],
            FailureReason: null)
        {
            Kind = kind,
            Status = kind == FontFileKind.Type1 ? FontStatus.Converted : FontStatus.Loaded,
            Faces = [face],
            FsType = fsType,
        };

    private static FontIndex IndexOf(params FontIndex.Entry[] entries)
    {
        var index = new FontIndex();

        foreach (var entry in entries)
        {
            index.Put(entry);
        }

        return index;
    }

    /// <summary>A believable TrueType header, so the extension check has something real to read.</summary>
    private static byte[] Font(string tag = "\0\u0001\0\0")
    {
        var bytes = new byte[256];

        for (var i = 0; i < tag.Length; i++)
        {
            bytes[i] = (byte)tag[i];
        }

        return bytes;
    }

    // ------------------------------------------------------------ rights ----

    [Theory]
    [InlineData(0x0000, true)]   // Installable
    [InlineData(0x0008, true)]   // Editable
    [InlineData(0x0002, false)]  // Restricted
    [InlineData(0x0004, false)]  // Preview and print
    [InlineData(0x0200, false)]  // Bitmap embedding only
    [InlineData(0x0208, false)]  // Editable, but bitmaps only
    [InlineData(0x000A, false)]  // Restricted and Editable: the stricter wins
    [InlineData(0x0100, true)]   // No subsetting, which this program does not do anyway
    public void TheFontsOwnRightsDecide(int fsType, bool allowed) =>
        Assert.Equal(allowed, FontEmbedding.RightsAllow((ushort)fsType));

    [Fact]
    public void ARefusalSaysWhyInWordsAUserCanActOn()
    {
        Assert.Contains("licence does not allow", FontEmbedding.RightsPhrase(0x0002), StringComparison.Ordinal);
        Assert.Contains("preview and printing", FontEmbedding.RightsPhrase(0x0004), StringComparison.Ordinal);
        Assert.Contains("bitmap", FontEmbedding.RightsPhrase(0x0200), StringComparison.Ordinal);
    }

    // ------------------------------------------------------------- plan -----

    [Fact]
    public void AnInstallableFolderFontTravels()
    {
        var index = IndexOf(Entry("Nimbus Roman"));

        var plan = FontEmbedding.Plan(["Nimbus Roman"], index, _ => Font());

        var font = Assert.Single(plan.Fonts);
        Assert.Equal("Nimbus Roman", font.Family);
        Assert.Equal(FontFace.Regular, font.Face);
        Assert.Empty(plan.Left);
    }

    [Fact]
    public void AWindowsFontIsNeitherEmbeddedNorComplainedAbout()
    {
        // Not in the font folder, so not in the index. Every PC that opens
        // the document has it already, and embedding Microsoft's fonts would
        // be a licence breach as well as a waste.
        var plan = FontEmbedding.Plan(["Calibri"], IndexOf(), _ => Font());

        Assert.Empty(plan.Fonts);
        Assert.Empty(plan.Left);
    }

    [Fact]
    public void ARestrictedFontIsLeftBehindWithItsReason()
    {
        var index = IndexOf(Entry("Expensive Sans", fsType: 0x0002));

        var plan = FontEmbedding.Plan(["Expensive Sans"], index, _ => Font());

        Assert.Empty(plan.Fonts);
        var left = Assert.Single(plan.Left);
        Assert.Equal("Expensive Sans", left.Family);
        Assert.Contains("licence", left.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void AConvertedType1FontIsNeverEmbedded()
    {
        // Section 6.6.4: what would travel is this program's conversion, not
        // the file the foundry licensed - and the conversion's own fsType is
        // set to preview-and-print for that reason.
        var index = IndexOf(Entry("Old Metal", fsType: 0x0000, kind: FontFileKind.Type1));

        var plan = FontEmbedding.Plan(["Old Metal"], index, _ => Font());

        Assert.Empty(plan.Fonts);
        Assert.Contains("Type 1", Assert.Single(plan.Left).Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryFaceOfAFamilyTravelsTogether()
    {
        var index = IndexOf(
            Entry("Nimbus Roman", face: "Regular"),
            Entry("Nimbus Roman", face: "Bold"),
            Entry("Nimbus Roman", face: "Italic"),
            Entry("Nimbus Roman", face: "Bold Italic"));

        var plan = FontEmbedding.Plan(["Nimbus Roman"], index, _ => Font());

        Assert.Equal(4, plan.Fonts.Count);
        Assert.Equal(
            [FontFace.Regular, FontFace.Bold, FontFace.Italic, FontFace.BoldItalic],
            plan.Fonts.Select(f => f.Face).Order().ToArray());
    }

    [Fact]
    public void TwoFilesClaimingOneFaceGiveOneFont()
    {
        var index = IndexOf(
            Entry("Nimbus Roman", face: "Regular", cached: "first.ttf"),
            Entry("Nimbus Roman", face: "Regular", cached: "second.ttf"));

        var plan = FontEmbedding.Plan(["Nimbus Roman"], index, _ => Font());

        Assert.Single(plan.Fonts);
    }

    [Fact]
    public void AFamilyThatGetsOneFaceThroughIsNotAlsoReportedAsLeftBehind()
    {
        // The regular face is fine and the bold one is restricted. The
        // document carries the family, so a warning saying it does not would
        // be worse than no warning at all.
        var index = IndexOf(
            Entry("Mixed", face: "Regular"),
            Entry("Mixed", face: "Bold", fsType: 0x0002));

        var plan = FontEmbedding.Plan(["Mixed"], index, _ => Font());

        Assert.Single(plan.Fonts);
        Assert.Empty(plan.Left);
    }

    [Fact]
    public void AFontWhoseCachedCopyHasGoneIsReportedRatherThanDroppedQuietly()
    {
        var plan = FontEmbedding.Plan(["Nimbus Roman"], IndexOf(Entry("Nimbus Roman")), _ => null);

        Assert.Empty(plan.Fonts);
        Assert.Contains("could not be read", Assert.Single(plan.Left).Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void AFamilyAskedForTwiceIsConsideredOnce()
    {
        var plan = FontEmbedding.Plan(
            ["Nimbus Roman", "nimbus roman"], IndexOf(Entry("Nimbus Roman")), _ => Font());

        Assert.Single(plan.Fonts);
    }

    // ------------------------------------------------------------- faces ----

    [Theory]
    [InlineData("Regular", FontFace.Regular)]
    [InlineData("", FontFace.Regular)]
    [InlineData("Book", FontFace.Regular)]
    [InlineData("Bold", FontFace.Bold)]
    [InlineData("Black", FontFace.Bold)]
    [InlineData("Italic", FontFace.Italic)]
    [InlineData("Oblique", FontFace.Italic)]
    [InlineData("Bold Italic", FontFace.BoldItalic)]
    [InlineData("BoldOblique", FontFace.BoldItalic)]
    [InlineData("bold italic", FontFace.BoldItalic)]
    public void ASubfamilyNameBecomesOneOfTheFourFaces(string subfamily, FontFace expected) =>
        Assert.Equal(expected, FontEmbedding.FaceOf(subfamily));

    [Fact]
    public void TheExtensionFollowsTheOutlinesInTheFile()
    {
        Assert.Equal(".otf", FontEmbedding.ExtensionFor(Font("OTTO")));
        Assert.Equal(".ttf", FontEmbedding.ExtensionFor(Font()));
        Assert.Equal(".ttf", FontEmbedding.ExtensionFor([]));
    }

    [Fact]
    public void AFontsFileNameIsSafeOnAnyFilesystem()
    {
        var font = new EmbeddedFont("Nimbus Roman No9 L/Bad:Name", FontFace.BoldItalic, [])
        {
            Extension = ".otf",
        };

        Assert.StartsWith("Nimbus-Roman-No9-LBadName-BoldItalic-", font.FileName, StringComparison.Ordinal);
        Assert.EndsWith(".otf", font.FileName, StringComparison.Ordinal);
    }

    [Fact]
    public void TwoFamiliesThatStripDownToTheSameNameGetDifferentFiles()
    {
        // The reason the name carries a fingerprint. Both of these strip to
        // "Foo-Bar", and inside a package the second would then point at the
        // first one's file - a document using both would show one font twice.
        var first = new EmbeddedFont("Foo Bar", FontFace.Regular, []);
        var second = new EmbeddedFont("Foo-Bar", FontFace.Regular, []);

        Assert.NotEqual(first.FileName, second.FileName);
    }

    [Fact]
    public void AFilesNameDoesNotChangeBetweenRuns()
    {
        // A name in a file written today has to find the same file when the
        // document is opened tomorrow, so the fingerprint cannot come from
        // string.GetHashCode, which is seeded per process.
        Assert.Equal(
            new EmbeddedFont("Nimbus Roman", FontFace.Bold, []).FileName,
            new EmbeddedFont("Nimbus Roman", FontFace.Bold, []).FileName);

        Assert.Equal(
            "Nimbus-Roman-Bold-ec2a.ttf",
            new EmbeddedFont("Nimbus Roman", FontFace.Bold, []).FileName);
    }

    [Fact]
    public void AFamilyOfNothingButPunctuationStillGetsAName()
    {
        var font = new EmbeddedFont(":::", FontFace.Regular, []);

        Assert.Equal("font-Regular-1585.ttf", font.FileName);
    }
}
