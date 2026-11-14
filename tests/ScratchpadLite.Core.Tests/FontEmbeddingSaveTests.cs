using ScratchpadLite.Core.Fonts;
using ScratchpadLite.Core.Platform;
using Xunit;

namespace ScratchpadLite.Core.Tests;

/// <summary>
/// What a save puts inside the file (Section 7.4), and the housekeeping of
/// the fonts an open document has unpacked.
/// </summary>
public class FontEmbeddingSaveTests : IDisposable
{
    private static FontIndex.Entry Entry(string family, ushort fsType = 0, string face = "Regular") =>
        new(
            SourcePath: $@"C:\fonts\{family}.ttf",
            Length: 1024,
            ModifiedUtc: DateTime.UnixEpoch,
            Sha1: family,
            CachedFile: $"{family}.ttf",
            Families: [family],
            FailureReason: null)
        {
            Kind = FontFileKind.Direct,
            Status = FontStatus.Loaded,
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

    private static byte[] Font(byte seed = 7)
    {
        var bytes = new byte[128];
        bytes[1] = 0x01;

        for (var i = 4; i < bytes.Length; i++)
        {
            bytes[i] = (byte)((i * seed) % 251);
        }

        return bytes;
    }

    private static IReadOnlySet<string> Using(params string[] families) =>
        new HashSet<string>(families, StringComparer.OrdinalIgnoreCase);

    // --------------------------------------------------------- for save ----

    [Fact]
    public void WithTheSettingOffADocumentKeepsTheFontsItArrivedWith()
    {
        // Stripping them out would be this program deciding that someone's
        // document should stop looking the way it looks everywhere else.
        var carried = new[] { new EmbeddedFont("Nimbus Roman", FontFace.Regular, Font()) };

        var plan = FontEmbedding.ForSave(
            carried, Using("Nimbus Roman"), addFromFontFolder: false, IndexOf(), _ => null);

        Assert.Single(plan.Fonts);
        Assert.Empty(plan.Left);
    }

    [Fact]
    public void WithTheSettingOffNothingNewIsAdded()
    {
        var plan = FontEmbedding.ForSave(
            [], Using("Nimbus Roman"), addFromFontFolder: false,
            IndexOf(Entry("Nimbus Roman")), _ => Font());

        Assert.Empty(plan.Fonts);
    }

    [Fact]
    public void WithTheSettingOnAFolderFontIsAdded()
    {
        var plan = FontEmbedding.ForSave(
            [], Using("Nimbus Roman"), addFromFontFolder: true,
            IndexOf(Entry("Nimbus Roman")), _ => Font());

        Assert.Single(plan.Fonts);
    }

    [Fact]
    public void AFontForTextThatHasGoneDoesNotTravel()
    {
        // The document used to use it and does not any more. Carrying it
        // would be weight in the file and nothing else.
        var carried = new[] { new EmbeddedFont("Nimbus Roman", FontFace.Regular, Font()) };

        var plan = FontEmbedding.ForSave(
            carried, Using("Calibri"), addFromFontFolder: true, IndexOf(), _ => null);

        Assert.Empty(plan.Fonts);
    }

    [Fact]
    public void AFontTheDocumentCarriesIsNotAddedTwiceFromTheFolder()
    {
        var carried = new[] { new EmbeddedFont("Nimbus Roman", FontFace.Regular, Font(seed: 3)) };

        var plan = FontEmbedding.ForSave(
            carried, Using("Nimbus Roman"), addFromFontFolder: true,
            IndexOf(Entry("Nimbus Roman")), _ => Font(seed: 5));

        var font = Assert.Single(plan.Fonts);

        // The one that was already in the document wins: it is the font the
        // document was written with, and this PC's copy under the same name
        // may be a different font entirely.
        Assert.Equal(Font(seed: 3), font.Bytes);
    }

    [Fact]
    public void ACarriedFontSilencesTheComplaintAboutTheLocalCopy()
    {
        // The document carries a font the folder's own copy would refuse to
        // embed. The document is still carrying it, so saying it is not would
        // be false.
        var carried = new[] { new EmbeddedFont("Expensive Sans", FontFace.Regular, Font()) };

        var plan = FontEmbedding.ForSave(
            carried, Using("Expensive Sans"), addFromFontFolder: true,
            IndexOf(Entry("Expensive Sans", fsType: 0x0002)), _ => Font());

        Assert.Single(plan.Fonts);
        Assert.Empty(plan.Left);
    }

    [Fact]
    public void ARefusedFontIsReportedWhenNothingElseCarriesIt()
    {
        var plan = FontEmbedding.ForSave(
            [], Using("Expensive Sans"), addFromFontFolder: true,
            IndexOf(Entry("Expensive Sans", fsType: 0x0002)), _ => Font());

        Assert.Empty(plan.Fonts);
        Assert.Equal("Expensive Sans", Assert.Single(plan.Left).Family);
    }

    // ------------------------------------------------------ unpacking ------

    [Fact]
    public void UnpackingWritesOneFilePerFaceWhereTheDocumentCanFindThem()
    {
        AppPaths.Initialize();

        var document = Guid.NewGuid();

        var written = FontEmbedding.Unpack(
            [
                new EmbeddedFont("Nimbus Roman", FontFace.Regular, Font(seed: 3)),
                new EmbeddedFont("Nimbus Roman", FontFace.Bold, Font(seed: 5)),
            ],
            document);

        try
        {
            Assert.Equal(2, written.Count);

            foreach (var (font, path) in written)
            {
                Assert.True(File.Exists(path), $"{path} should have been written");
                Assert.Equal(font.Bytes, File.ReadAllBytes(path));
                Assert.StartsWith(
                    FontEmbedding.FolderFor(document), path, StringComparison.OrdinalIgnoreCase);
            }
        }
        finally
        {
            Directory.Delete(FontEmbedding.FolderFor(document), recursive: true);
        }
    }

    [Fact]
    public void TidyingRemovesTheFoldersOfDocumentsThatAreNotOpen()
    {
        AppPaths.Initialize();

        var open = Guid.NewGuid();
        var closed = Guid.NewGuid();

        FontEmbedding.Unpack([new EmbeddedFont("A", FontFace.Regular, Font())], open);
        FontEmbedding.Unpack([new EmbeddedFont("B", FontFace.Regular, Font())], closed);

        try
        {
            FontEmbedding.TidyUnpacked([open]);

            Assert.True(
                Directory.Exists(FontEmbedding.FolderFor(open)),
                "an open document's fonts must stay where WPF is reading them");

            Assert.False(
                Directory.Exists(FontEmbedding.FolderFor(closed)),
                "a folder from a run that has ended is nobody's");
        }
        finally
        {
            if (Directory.Exists(FontEmbedding.FolderFor(open)))
            {
                Directory.Delete(FontEmbedding.FolderFor(open), recursive: true);
            }
        }
    }

    [Fact]
    public void ARealPreparedFontIsFoundAndReadBackWhole()
    {
        // The join between the two halves: the index says which cache file a
        // family's font is in, and the embedder has to be able to read that
        // file and get a font out of it. Each half is tested elsewhere; this
        // is the only test that puts a real font through both.
        AppPaths.Initialize();

        var source = FontFixtures.AnyWindowsFont();

        if (source is null)
        {
            return;
        }

        var folder = Directory.CreateTempSubdirectory("sp-embed-");

        try
        {
            var original = File.ReadAllBytes(source);
            File.WriteAllBytes(Path.Combine(folder.FullName, "Plain.ttf"), original);

            var index = new FontIndex();
            FontPreparer.CheckFolder(folder.FullName, index, null, null, CancellationToken.None);

            var entry = Assert.Single(index.Usable());
            var family = entry.Families[0];

            var plan = FontEmbedding.Plan([family], index, FontCache.TryRead);

            if (!FontEmbedding.RightsAllow(entry.FsType))
            {
                // Most of Windows' own fonts refuse embedding, which is the
                // right answer and not a failure of this code.
                Assert.Empty(plan.Fonts);
                Assert.Equal(family, Assert.Single(plan.Left).Family);

                return;
            }

            var font = Assert.Single(plan.Fonts);

            Assert.Equal(family, font.Family);
            Assert.Equal(original.Length, font.Bytes.Length);
            Assert.Equal(FontInspector.Read(original).Families, FontInspector.Read(font.Bytes).Families);
        }
        finally
        {
            try
            {
                folder.Delete(recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    [Fact]
    public void TidyingAnEmptyCacheIsNotAnError()
    {
        AppPaths.Initialize();

        Assert.Equal(0, FontEmbedding.TidyUnpacked([Guid.NewGuid()]));
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);

        var root = Path.Combine(AppPaths.FontCacheDir, FontCache.EmbeddedFolderName);

        if (Directory.Exists(root) && Directory.GetDirectories(root).Length == 0)
        {
            Directory.Delete(root);
        }
    }
}
