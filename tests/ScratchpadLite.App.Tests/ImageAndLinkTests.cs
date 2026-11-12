using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using ScratchpadLite.App.Editor;
using ScratchpadLite.Core.Model;
using Xunit;
using Xunit.Abstractions;
using IrParagraph = ScratchpadLite.Core.Model.Paragraph;
using WpfParagraph = System.Windows.Documents.Paragraph;

namespace ScratchpadLite.App.Tests;

/// <summary>
/// Inserting pictures and links in the editor (Section 6.9).
/// </summary>
/// <remarks>
/// What matters about a picture is that the bytes which went in are the bytes
/// a save writes out: re-encoding on every save would degrade a photograph a
/// little at a time, so each test checks the bytes and not just that something
/// is there.
/// <para>
/// The link tests cover the rule about which addresses open without asking.
/// That rule is the security boundary of this feature - a document is untrusted
/// and a hyperlink in one is a line of text asking this program to run
/// something - so it is tested as a decision, separately from the message box
/// that acts on it.
/// </remarks>
[Collection(nameof(AppHost))]
public class ImageAndLinkTests(ITestOutputHelper output)
{
    private static readonly StyleSheet Styles = StyleSheet.Default;

    // ---------------------------------------------------------- images ------

    [Fact]
    public void AnInsertedPictureKeepsItsExactBytes()
    {
        var png = MinimalPng();

        var ir = InEditor(editor => ImageSupport.Insert(editor, png, "image/png", "a red dot"));

        var image = Assert.IsType<ImageRun>(
            Assert.Single(
                ir.Blocks.OfType<IrParagraph>().SelectMany(p => p.Inlines),
                i => i is ImageRun));

        Assert.Equal(png, image.Data);
        Assert.Equal("image/png", image.Mime);
        Assert.Equal("a red dot", image.Alt);
    }

    [Fact]
    public void APictureGoesWhereTheCaretIsNotAtTheEndOfTheRun()
    {
        // Inserting into Paragraph.Inlines would put it after the whole run
        // the caret is inside, so a picture dropped mid-sentence would jump to
        // the end of it.
        var ir = InEditor(editor =>
        {
            editor.CaretPosition.InsertTextInRun("beforeafter");

            var run = editor.Document.Blocks.OfType<WpfParagraph>().First().Inlines.OfType<Run>().First();

            editor.CaretPosition = run.ContentStart.GetPositionAtOffset("before".Length)!;

            ImageSupport.Insert(editor, MinimalPng(), "image/png");
        });

        var inlines = ir.Blocks.OfType<IrParagraph>().First().Inlines;
        var texts = inlines.OfType<TextRun>().Select(r => r.Text).ToList();

        output.WriteLine(string.Join(" / ", inlines.Select(i => i.GetType().Name)));

        Assert.Equal(1, inlines.ToList().FindIndex(i => i is ImageRun));
        Assert.Equal(["before", "after"], texts);
    }

    [Fact]
    public void APictureWiderThanThePageIsScaledToFit()
    {
        var ir = InEditor(editor =>
        {
            // The page is 8.5 inches wide with 1 inch margins, so 6.5 inches
            // of text: 468 points.
            editor.Document.PageWidth = 8.5 * 96;
            editor.Document.PagePadding = new Thickness(96);

            ImageSupport.Insert(editor, WidePng(), "image/png");
        });

        var image = Assert.IsType<ImageRun>(
            ir.Blocks.OfType<IrParagraph>().SelectMany(p => p.Inlines).First(i => i is ImageRun));

        output.WriteLine($"{image.WidthPt} x {image.HeightPt} pt");

        Assert.True(image.WidthPt <= 469, $"the picture is {image.WidthPt} pt wide, wider than the text column");

        // And the shape is kept: the source is 1000 x 100.
        Assert.Equal(image.WidthPt / 10, image.HeightPt, 1);
    }

    [Fact]
    public void TheSamePictureTwiceIsStoredOnce()
    {
        // Section 6.9: image bytes are stored once per document, keyed by
        // SHA-1. The same logo in forty places is one array, not forty.
        var distinct = AppHost.InFocusedEditor(editor =>
        {
            var png = MinimalPng();

            ImageSupport.Insert(editor, png, "image/png");
            ImageSupport.Insert(editor, png.ToArray(), "image/png");    // a copy, same bytes
            ImageSupport.Insert(editor, WidePng(), "image/png");

            return ImageSupport.DistinctImageCount(editor.Document);
        });

        Assert.Equal(2, distinct);
    }

    [Fact]
    public void TwoCopiesOfOnePictureShareTheirBytes()
    {
        var shared = AppHost.InFocusedEditor(editor =>
        {
            var png = MinimalPng();

            ImageSupport.Insert(editor, png, "image/png");
            ImageSupport.Insert(editor, png.ToArray(), "image/png");

            var payloads = editor.Document.Blocks.OfType<WpfParagraph>()
                .SelectMany(p => p.Inlines)
                .OfType<InlineUIContainer>()
                .Select(c => (c.Child as Image)?.Tag)
                .OfType<Bridge.SpProps.ImagePayload>()
                .ToList();

            return payloads.Count == 2 && ReferenceEquals(payloads[0].Data, payloads[1].Data);
        });

        Assert.True(shared, "the two copies did not share one byte array");
    }

    [Fact]
    public void ThePictureAtTheCaretIsFound()
    {
        var found = AppHost.InFocusedEditor(editor =>
        {
            ImageSupport.Insert(editor, MinimalPng(), "image/png");

            // The caret ends up just after the picture, which is where a click
            // leaves it too.
            return ImageSupport.ImageAt(editor) is not null;
        });

        Assert.True(found);
    }

    [Fact]
    public void SettingThePropertiesChangesTheSizeAndTheAltText()
    {
        var ir = InEditor(editor =>
        {
            ImageSupport.Insert(editor, MinimalPng(), "image/png", "first");

            var image = ImageSupport.ImageAt(editor)!;

            ImageSupport.SetProperties(editor, image, widthPt: 120, heightPt: 60, alt: "second");
        });

        var picture = Assert.IsType<ImageRun>(
            ir.Blocks.OfType<IrParagraph>().SelectMany(p => p.Inlines).First(i => i is ImageRun));

        Assert.Equal(120, picture.WidthPt, 1);
        Assert.Equal(60, picture.HeightPt, 1);
        Assert.Equal("second", picture.Alt);
    }

    [Fact]
    public void TheNaturalSizeIsThePicturesOwn()
    {
        var size = AppHost.InFocusedEditor(editor =>
        {
            ImageSupport.Insert(editor, MinimalPng(), "image/png");
            ImageSupport.SetProperties(editor, ImageSupport.ImageAt(editor)!, 500, 500, "");

            return ImageSupport.NaturalSize(ImageSupport.ImageAt(editor)!);
        });

        // The fixture is one pixel square, which at 96 dpi is 0.75 pt.
        Assert.Equal(0.75, size.WidthPt, 2);
        Assert.Equal(0.75, size.HeightPt, 2);
    }

    [Fact]
    public void SomethingThatIsNotAPictureIsRefusedWithAReadableMessage()
    {
        var error = AppHost.InFocusedEditor(editor => Record.Exception(() =>
            ImageSupport.Insert(editor, "this is not an image"u8.ToArray(), "image/png")));

        var complaint = Assert.IsType<ImageSupport.ImageException>(error);

        output.WriteLine(complaint.Message);

        Assert.Contains("picture", complaint.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void APictureFileThatIsNotThereIsRefusedWithAReadableMessage()
    {
        var error = AppHost.InFocusedEditor(editor => Record.Exception(() =>
            ImageSupport.Insert(
                editor,
                Path.Combine(Path.GetTempPath(), "no-such-picture-" + Guid.NewGuid().ToString("N") + ".png"))));

        Assert.IsType<ImageSupport.ImageException>(error);
    }

    [Fact]
    public void AnOversizePictureFileIsRefusedBeforeItIsRead()
    {
        var path = Path.Combine(Path.GetTempPath(), "sp-big-" + Guid.NewGuid().ToString("N") + ".png");

        try
        {
            // A sparse-ish file: written once, never decoded, because the size
            // check comes first.
            using (var file = File.Create(path))
            {
                file.SetLength(ImageSupport.MaxImageBytes + 1);
            }

            var error = AppHost.InFocusedEditor(editor => Record.Exception(() => ImageSupport.Insert(editor, path)));

            var complaint = Assert.IsType<ImageSupport.ImageException>(error);

            output.WriteLine(complaint.Message);

            Assert.Contains("too", complaint.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("a.png", "image/png")]
    [InlineData("a.PNG", "image/png")]
    [InlineData("a.jpg", "image/jpeg")]
    [InlineData("a.jpeg", "image/jpeg")]
    [InlineData("a.gif", "image/gif")]
    [InlineData("a.bmp", "image/bmp")]
    [InlineData("a.tif", "image/tiff")]
    [InlineData("a.tiff", "image/tiff")]
    [InlineData("a.webp", "image/webp")]
    public void EveryOfferedKindHasAMimeType(string name, string expected) =>
        Assert.Equal(expected, ImageSupport.MimeFor(name));

    // ----------------------------------------------------------- links ------

    [Fact]
    public void ALinkOnASelectionKeepsTheSelectedText()
    {
        var ir = InEditor(editor =>
        {
            editor.CaretPosition.InsertTextInRun("click here");
            editor.Selection.Select(editor.Document.ContentStart, editor.Document.ContentEnd);

            LinkSupport.InsertOrEdit(editor, "https://example.com/", null);
        });

        var run = ir.Blocks.OfType<IrParagraph>()
            .SelectMany(p => p.Inlines)
            .OfType<TextRun>()
            .First(r => r.Style.Link is not null);

        Assert.Contains("click here", run.Text, StringComparison.Ordinal);
        Assert.Equal(
            IrNormalizer.NormalizeLink("https://example.com/"),
            run.Style.Link);
    }

    [Fact]
    public void ALinkOnASelectionKeepsItsFormatting()
    {
        // Linking a bold word should not unbolden it.
        var ir = InEditor(editor =>
        {
            editor.CaretPosition.InsertTextInRun("bold");
            editor.Selection.Select(editor.Document.ContentStart, editor.Document.ContentEnd);
            FormatCommands.ToggleBold(editor);

            editor.Selection.Select(editor.Document.ContentStart, editor.Document.ContentEnd);
            LinkSupport.InsertOrEdit(editor, "https://example.com/", null);
        });

        var run = ir.Blocks.OfType<IrParagraph>()
            .SelectMany(p => p.Inlines)
            .OfType<TextRun>()
            .First(r => r.Style.Link is not null);

        Assert.True(run.Style.Bold, "the link lost the bold the text already had");
    }

    [Fact]
    public void ALinkWithNoSelectionInsertsItsDisplayText()
    {
        var ir = InEditor(editor =>
            LinkSupport.InsertOrEdit(editor, "https://example.com/", "the site"));

        var run = ir.Blocks.OfType<IrParagraph>()
            .SelectMany(p => p.Inlines)
            .OfType<TextRun>()
            .First(r => r.Style.Link is not null);

        Assert.Equal("the site", run.Text);
    }

    [Fact]
    public void ALinkWithNoTextShowsItsAddress()
    {
        var ir = InEditor(editor => LinkSupport.InsertOrEdit(editor, "https://example.com/", null));

        var run = ir.Blocks.OfType<IrParagraph>()
            .SelectMany(p => p.Inlines)
            .OfType<TextRun>()
            .First(r => r.Style.Link is not null);

        Assert.Equal("https://example.com/", run.Text);
    }

    [Fact]
    public void EditingALinkChangesItsTargetAndText()
    {
        var ir = InEditor(editor =>
        {
            LinkSupport.InsertOrEdit(editor, "https://first.example/", "first");

            // The caret is left inside the link, which is what Ctrl+K sees.
            LinkSupport.InsertOrEdit(editor, "https://second.example/", "second");
        });

        var runs = ir.Blocks.OfType<IrParagraph>()
            .SelectMany(p => p.Inlines)
            .OfType<TextRun>()
            .Where(r => r.Style.Link is not null)
            .ToList();

        var run = Assert.Single(runs);

        Assert.Equal("second", run.Text);
        Assert.Contains("second.example", run.Style.Link!, StringComparison.Ordinal);
    }

    [Fact]
    public void RemovingALinkKeepsItsText()
    {
        var ir = InEditor(editor =>
        {
            LinkSupport.InsertOrEdit(editor, "https://example.com/", "keep me");
            LinkSupport.Remove(editor);
        });

        var runs = ir.Blocks.OfType<IrParagraph>().SelectMany(p => p.Inlines).OfType<TextRun>().ToList();

        Assert.Contains(runs, r => r.Text.Contains("keep me", StringComparison.Ordinal));
        Assert.All(runs, r => Assert.Null(r.Style.Link));
    }

    [Fact]
    public void TheLinkAtTheCaretIsFound()
    {
        var found = AppHost.InFocusedEditor(editor =>
        {
            LinkSupport.InsertOrEdit(editor, "https://example.com/", "here");

            return LinkSupport.Current(editor);
        });

        Assert.Equal("https://example.com/", found.Url);
        Assert.Equal("here", found.Text);
    }

    [Fact]
    public void AnAddressKeptAsWrittenSurvivesEvenWhenUriWouldNotParseIt()
    {
        // A file can carry a target Uri rejects, and dropping it would lose
        // part of the document.
        const string odd = "weird scheme::not a uri";

        var ir = InEditor(editor => LinkSupport.InsertOrEdit(editor, odd, "odd"));

        var run = ir.Blocks.OfType<IrParagraph>()
            .SelectMany(p => p.Inlines)
            .OfType<TextRun>()
            .First(r => r.Text == "odd");

        Assert.NotNull(run.Style.Link);
    }

    // --- the rule about what may be opened ----------------------------------

    [Theory]
    [InlineData("https://example.com/")]
    [InlineData("HTTPS://EXAMPLE.COM/")]
    [InlineData("http://example.com/")]
    [InlineData("mailto:someone@example.com")]
    public void AWebOrMailAddressOpensWithoutAsking(string target) =>
        Assert.Equal(LinkSupport.Verdict.OpenDirectly, LinkSupport.Judge(target));

    [Theory]
    [InlineData(@"file:///C:/Windows/System32/cmd.exe")]
    [InlineData("ftp://example.com/thing")]
    [InlineData("ms-settings:windowsupdate")]
    [InlineData("javascript:alert(1)")]
    [InlineData("vbscript:msgbox(1)")]
    [InlineData("search-ms:query=passwords")]
    [InlineData("shell:startup")]
    [InlineData("data:text/html,<script>alert(1)</script>")]
    public void EveryOtherSchemeIsAskedAboutFirst(string target)
    {
        // The whole point of the rule: a document is untrusted, and these are
        // the addresses that can start another program.
        Assert.Equal(LinkSupport.Verdict.AskFirst, LinkSupport.Judge(target));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("not an address at all")]
    [InlineData("/just/a/path")]
    public void SomethingThatIsNotAnAddressIsNotOpened(string? target) =>
        Assert.Equal(LinkSupport.Verdict.Unusable, LinkSupport.Judge(target));

    // --------------------------------------------------------- helpers ------

    private static SpDocument InEditor(Action<RichTextBox> edit) =>
        AppHost.InFocusedEditor(editor =>
        {
            edit(editor);

            return Bridge.FlowWalker.Walk(editor.Document, Styles);
        });

    /// <summary>A one-pixel PNG.</summary>
    private static byte[] MinimalPng() =>
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
        0x08, 0x02, 0x00, 0x00, 0x00, 0x90, 0x77, 0x53,
        0xDE, 0x00, 0x00, 0x00, 0x0C, 0x49, 0x44, 0x41,
        0x54, 0x08, 0xD7, 0x63, 0xF8, 0xCF, 0xC0, 0x00,
        0x00, 0x03, 0x01, 0x01, 0x00, 0x18, 0xDD, 0x8D,
        0xB0, 0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E,
        0x44, 0xAE, 0x42, 0x60, 0x82,
    ];

    /// <summary>
    /// A 1000 x 100 PNG, encoded here rather than kept as a fixture so the
    /// scale-to-fit test does not depend on a file.
    /// </summary>
    private static byte[] WidePng()
    {
        var bitmap = new System.Windows.Media.Imaging.WriteableBitmap(
            1000, 100, 96, 96, System.Windows.Media.PixelFormats.Bgr32, null);

        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));

        using var stream = new MemoryStream();
        encoder.Save(stream);

        return stream.ToArray();
    }
}
