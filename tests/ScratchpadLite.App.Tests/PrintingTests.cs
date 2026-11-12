using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using ScratchpadLite.App.Services;
using ScratchpadLite.Core.Model;
using Xunit;

namespace ScratchpadLite.App.Tests;

/// <summary>
/// Print and PDF (Section 6.17). The Phase 8 gate is that both come out in one
/// column, which is what most of this file is about.
/// </summary>
/// <remarks>
/// The one-column check does not read <c>ColumnWidth</c> back - that would only
/// prove the assignment happened. It lays the document out, asks the paginator
/// for a page, and looks at where on that page WPF actually put the text. Two
/// columns put half the lines in the right-hand half of the sheet, and nothing
/// else does.
/// </remarks>
[Collection(nameof(AppHost))]
public class PrintingTests
{
    private const double LetterWidthDip = 612 * 96.0 / 72.0;
    private const double LetterHeightDip = 792 * 96.0 / 72.0;

    /// <summary>A document long enough to need more than one page.</summary>
    private static FlowDocument LongDocument(int paragraphs = 120)
    {
        var document = new FlowDocument
        {
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 16,
        };

        for (var i = 0; i < paragraphs; i++)
        {
            document.Blocks.Add(new System.Windows.Documents.Paragraph(
                new Run($"Paragraph {i}. The quick brown fox jumps over the lazy dog, repeatedly and at length.")));
        }

        return document;
    }

    /// <summary>
    /// Where every line of text starts on one laid-out page.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A FlowDocument never says how many columns it used, so the only way to
    /// find out is to look at where the text ended up. A laid-out page is a
    /// visual tree whose text is not elements but drawing instructions: each
    /// line is a <see cref="GlyphRunDrawing"/> in a visual's
    /// <see cref="DrawingGroup"/>, carrying the baseline origin it was placed
    /// at. The first version of this looked for <c>Glyphs</c> elements and
    /// found none, which is why it is worth writing down.
    /// </para>
    /// <para>
    /// Only offsets are accumulated, not transforms. A printed FlowDocument
    /// page is laid out with plain translations - no zoom, no rotation - and a
    /// full transform walk would be code with nothing to exercise it.
    /// </para>
    /// </remarks>
    private static List<double> TextStartsOnPage(FlowDocument document, int page)
    {
        var paginator = ((IDocumentPaginatorSource)document).DocumentPaginator;

        paginator.ComputePageCount();

        using var laidOut = paginator.GetPage(page);

        var starts = new List<double>();

        CollectFromVisual(laidOut.Visual, new Vector(0, 0), starts);

        return starts;
    }

    private static void CollectFromVisual(Visual visual, Vector at, List<double> starts)
    {
        var offset = VisualTreeHelper.GetOffset(visual);
        var here = at + offset;

        if (VisualTreeHelper.GetDrawing(visual) is { } drawing)
        {
            CollectFromDrawing(drawing, here, starts);
        }

        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(visual); i++)
        {
            if (VisualTreeHelper.GetChild(visual, i) is Visual child)
            {
                CollectFromVisual(child, here, starts);
            }
        }
    }

    private static void CollectFromDrawing(Drawing drawing, Vector at, List<double> starts)
    {
        switch (drawing)
        {
            case DrawingGroup group:
                var inner = at;

                if (group.Transform is { } transform)
                {
                    var moved = transform.Value.Transform(new Point(at.X, at.Y));
                    inner = new Vector(moved.X, moved.Y);
                }

                foreach (var child in group.Children)
                {
                    CollectFromDrawing(child, inner, starts);
                }

                break;

            case GlyphRunDrawing { GlyphRun: { } run }:
                starts.Add(at.X + run.BaselineOrigin.X);

                break;
        }
    }

    // ----------------------------------------------- the gate: one column --

    [Fact]
    public void APaginatedDocumentComesOutInOneColumn() =>
        AppHost.Run(() =>
        {
            var copy = Printing.Paginate(
                LongDocument(), new PageSetup(), LetterWidthDip, LetterHeightDip);

            var starts = TextStartsOnPage(copy, 0);

            Assert.NotEmpty(starts);

            var middle = copy.PageWidth / 2;

            Assert.All(
                starts,
                x => Assert.True(
                    x < middle,
                    $"a line of text started at {x:F0}, past the middle of a {copy.PageWidth:F0} wide page, "
                    + "which is where the second column of a two-column layout begins"));
        });

    /// <summary>
    /// The control test: WPF really does lay a FlowDocument out in columns.
    /// </summary>
    /// <remarks>
    /// Without this, the test above proves nothing - a document that was never
    /// going to use two columns passes it either way. This one sets a narrow
    /// column width and shows text appearing in the right-hand half of the
    /// sheet, which is what <c>ColumnWidth = PageWidth</c> is there to prevent.
    /// </remarks>
    [Fact]
    public void WpfReallyDoesUseColumnsWhenAColumnWidthIsSet() =>
        AppHost.Run(() =>
        {
            var columned = LongDocument();

            columned.PageWidth = LetterWidthDip;
            columned.PageHeight = LetterHeightDip;
            columned.PagePadding = new Thickness(48);
            columned.ColumnWidth = 240;

            Assert.Contains(TextStartsOnPage(columned, 0), x => x > columned.PageWidth / 2);
        });

    [Fact]
    public void PaginateOverridesAColumnWidthTheDocumentCameWith() =>
        AppHost.Run(() =>
        {
            var columned = LongDocument();

            columned.ColumnWidth = 240;

            var copy = Printing.Paginate(columned, new PageSetup(), LetterWidthDip, LetterHeightDip);

            Assert.All(
                TextStartsOnPage(copy, 0),
                x => Assert.True(x < copy.PageWidth / 2, $"a line started at {x:F0}, in the second column"));
        });

    [Fact]
    public void AFreshFlowDocumentHasNoColumnWidthAtAll() =>
        AppHost.Run(() =>
        {
            // Worth pinning down, because Section 6.17 says printing comes out
            // in two columns unless ColumnWidth is set, and on .NET 10 a
            // FlowDocument's ColumnWidth starts as NaN, which is one column.
            // So the line in Paginate is insurance against a document that
            // arrives carrying a column width - which the test above shows is a
            // real thing - rather than a cure for a default. If this ever
            // changes, the one-column gate test above is what catches it.
            Assert.True(double.IsNaN(new FlowDocument().ColumnWidth));
        });

    [Fact]
    public void ALongDocumentStillPaginatesIntoSeveralPages() =>
        AppHost.Run(() =>
        {
            var copy = Printing.Paginate(
                LongDocument(), new PageSetup(), LetterWidthDip, LetterHeightDip);

            var paginator = ((IDocumentPaginatorSource)copy).DocumentPaginator;

            paginator.ComputePageCount();

            Assert.True(
                paginator.PageCount > 1,
                "one column on one sheet of Letter cannot hold 120 paragraphs, so something is wrong with the layout");
        });

    // -------------------------------------------------------- the settings --

    [Fact]
    public void PaginateUsesThePrintersPrintableArea() =>
        AppHost.Run(() =>
        {
            var copy = Printing.Paginate(LongDocument(4), new PageSetup(), 700, 900);

            Assert.Equal(700, copy.PageWidth);
            Assert.Equal(900, copy.PageHeight);
        });

    [Fact]
    public void PaginateFallsBackToThePaperSizeWhenThePrinterSaysNothing() =>
        AppHost.Run(() =>
        {
            var copy = Printing.Paginate(LongDocument(4), new PageSetup("A4"), 0, 0);

            Assert.Equal(Printing.WidthOf(new PageSetup("A4")), copy.PageWidth, 1);
            Assert.Equal(Printing.HeightOf(new PageSetup("A4")), copy.PageHeight, 1);
        });

    [Fact]
    public void TheMarginsBecomeThePagePadding() =>
        AppHost.Run(() =>
        {
            var page = new PageSetup(
                MarginTopPt: 36, MarginBottomPt: 54, MarginLeftPt: 72, MarginRightPt: 18);

            var copy = Printing.Paginate(LongDocument(4), page, LetterWidthDip, LetterHeightDip);

            // Points to device-independent pixels: 96 per inch against 72.
            Assert.Equal(48, copy.PagePadding.Top, 1);
            Assert.Equal(72, copy.PagePadding.Bottom, 1);
            Assert.Equal(96, copy.PagePadding.Left, 1);
            Assert.Equal(24, copy.PagePadding.Right, 1);
        });

    // ------------------------------------------------------------ the copy --

    [Fact]
    public void TheTextSurvivesTheCopy() =>
        AppHost.Run(() =>
        {
            var source = new FlowDocument(
                new System.Windows.Documents.Paragraph(new Run("words that have to be printed")));

            var copy = Printing.Clone(source);

            Assert.Contains(
                "words that have to be printed",
                new TextRange(copy.ContentStart, copy.ContentEnd).Text);
        });

    [Fact]
    public void FormattingSurvivesTheCopy() =>
        AppHost.Run(() =>
        {
            var bold = new Run("emphatic") { FontWeight = FontWeights.Bold };

            var copy = Printing.Clone(
                new FlowDocument(new System.Windows.Documents.Paragraph(bold)));

            var run = copy.Blocks
                .OfType<System.Windows.Documents.Paragraph>()
                .SelectMany(p => p.Inlines)
                .OfType<Run>()
                .Single();

            Assert.Equal(FontWeights.Bold, run.FontWeight);
        });

    [Fact]
    public void APictureSurvivesTheCopy() =>
        AppHost.Run(() =>
        {
            // The reason the copy goes through XamlPackage rather than Xaml:
            // the package form carries the image, plain markup leaves a
            // reference behind and the copy prints with a hole in it.
            var pixel = new System.Windows.Media.Imaging.WriteableBitmap(
                4, 4, 96, 96, PixelFormats.Bgra32, null);

            var image = new System.Windows.Controls.Image
            {
                Source = pixel,
                Width = 32,
                Height = 32,
            };

            var paragraph = new System.Windows.Documents.Paragraph(new InlineUIContainer(image));

            var copy = Printing.Clone(new FlowDocument(paragraph));

            var images = copy.Blocks
                .OfType<System.Windows.Documents.Paragraph>()
                .SelectMany(p => p.Inlines)
                .OfType<InlineUIContainer>()
                .Select(c => c.Child)
                .OfType<System.Windows.Controls.Image>()
                .ToList();

            Assert.Single(images);
            Assert.NotNull(images[0].Source);
        });

    [Fact]
    public void TheDocumentOnScreenIsNotTouched() =>
        AppHost.Run(() =>
        {
            var source = LongDocument(4);

            source.PageWidth = 500;
            source.ColumnWidth = 123;
            source.PagePadding = new Thickness(7);

            Printing.Paginate(source, new PageSetup(), LetterWidthDip, LetterHeightDip);

            // Printing must not reflow what the user is looking at.
            Assert.Equal(500, source.PageWidth);
            Assert.Equal(123, source.ColumnWidth);
            Assert.Equal(new Thickness(7), source.PagePadding);
        });

    [Fact]
    public void ThePrintedCopyIsBlackOnWhiteWhateverTheThemeIs() =>
        AppHost.Run(() =>
        {
            var dark = new FlowDocument(new System.Windows.Documents.Paragraph(new Run("night")))
            {
                Background = Brushes.Black,
                Foreground = Brushes.White,
            };

            var copy = Printing.Clone(dark);

            // Dark Paper is a screen setting. Printing a dark page would use a
            // cartridge of ink to make something harder to read.
            Assert.Equal(Brushes.White, copy.Background);
        });

    // ------------------------------------------------------------- papers --

    [Fact]
    public void TheFourPapersSection617AsksForAreOffered()
    {
        Assert.Equal(4, Printing.PaperNames.Count);
        Assert.Contains("Letter", Printing.PaperNames);
        Assert.Contains("Legal", Printing.PaperNames);
        Assert.Contains("A4", Printing.PaperNames);
        Assert.Contains("A5", Printing.PaperNames);
    }

    [Fact]
    public void EachPaperNameMapsToAWindowsMediaSize()
    {
        Assert.Equal(System.Printing.PageMediaSizeName.NorthAmericaLetter, Printing.MediaFor("Letter"));
        Assert.Equal(System.Printing.PageMediaSizeName.NorthAmericaLegal, Printing.MediaFor("Legal"));
        Assert.Equal(System.Printing.PageMediaSizeName.ISOA4, Printing.MediaFor("A4"));
        Assert.Equal(System.Printing.PageMediaSizeName.ISOA5, Printing.MediaFor("A5"));
    }

    [Fact]
    public void APaperNobodyHasHeardOfIsTreatedAsLetter()
    {
        // A .docx from another program can name any paper at all, and refusing
        // to print it would be worse than printing it on Letter.
        Assert.Equal(System.Printing.PageMediaSizeName.NorthAmericaLetter, Printing.MediaFor("Quarto"));
        Assert.Equal(
            Printing.WidthOf(new PageSetup("Letter")),
            Printing.WidthOf(new PageSetup("Quarto")));
    }

    [Fact]
    public void LegalIsTallerThanLetterAndA5IsSmallerThanA4()
    {
        Assert.True(Printing.HeightOf(new PageSetup("Legal")) > Printing.HeightOf(new PageSetup("Letter")));
        Assert.True(Printing.WidthOf(new PageSetup("A5")) < Printing.WidthOf(new PageSetup("A4")));
    }

    [Fact]
    public void LandscapeSwapsTheTwoSides()
    {
        var portrait = new PageSetup("A4");
        var landscape = new PageSetup("A4", Landscape: true);

        Assert.Equal(Printing.WidthOf(portrait), Printing.HeightOf(landscape), 1);
        Assert.Equal(Printing.HeightOf(portrait), Printing.WidthOf(landscape), 1);
    }

    [Fact]
    public void LetterIsEightAndAHalfInchesWide() =>
        Assert.Equal(8.5 * 96, Printing.WidthOf(new PageSetup("Letter")), 1);

    // ----------------------------------------------------------- the printer --

    [Fact]
    public void ThePdfPrinterIsLookedForByItsWindowsName()
    {
        // Whether this PC has it is not this test's business - it can be turned
        // off in Windows Features. What matters is that looking does not throw
        // and that anything found is the right queue.
        var queue = Printing.FindPdfPrinter();

        if (queue is not null)
        {
            Assert.Equal(Printing.PdfPrinterName, queue.Name);
            queue.Dispose();
        }
    }

    [Fact]
    public void TheMissingPrinterMessageSaysHowToGetItBack()
    {
        var message = Printing.PdfPrinterMissingMessage;

        Assert.Contains("Microsoft Print to PDF", message);
        Assert.Contains("Windows features", message, StringComparison.OrdinalIgnoreCase);
    }
}
