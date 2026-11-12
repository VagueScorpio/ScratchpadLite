using System.IO;
using System.Printing;
using System.Windows;
using System.Windows.Documents;
using ScratchpadLite.Core.Model;
using ScratchpadLite.Core.Platform;

namespace ScratchpadLite.App.Services;

/// <summary>
/// Print and export to PDF (Section 6.17). Printing paginates the
/// FlowDocument for the chosen paper size and margins. PDF export goes
/// through the Microsoft Print to PDF printer that ships with Windows 10 and
/// 11, so nothing extra is installed (R1).
///
/// Both must come out in one column, which is the Phase 8 gate test.
/// </summary>
/// <remarks>
/// <para>
/// Nothing here touches the document on screen. A FlowDocument has one set of
/// page settings, and printing needs different ones - the printer's paper size
/// rather than the editor's page width - so printing the document itself would
/// mean reflowing what the user is looking at and then putting it back. The
/// copy goes through <c>DataFormats.XamlPackage</c>, which carries the images
/// with it; plain XAML would not.
/// </para>
/// <para>
/// The one line that matters most is <c>ColumnWidth = PageWidth</c>. A
/// FlowDocument lays itself out in as many columns as fit, and its default
/// column width is far narrower than a sheet of paper, so without that line
/// every printed page comes out as two columns of text - which looks like a
/// deliberate design choice and is the single most surprising thing WPF does
/// to a printed document.
/// </para>
/// </remarks>
internal static class Printing
{
    /// <summary>The printer Windows 10 and 11 ship with (Section 6.17).</summary>
    internal const string PdfPrinterName = "Microsoft Print to PDF";

    /// <summary>Points to device-independent pixels: 96 per inch against 72.</summary>
    private const double PointsToDip = 96.0 / 72.0;

    internal static string PdfPrinterMissingMessage =>
        $"\"{PdfPrinterName}\" is not installed, so there is nothing to export a PDF with.\n\n"
        + "It ships with Windows and can be switched back on in "
        + "Settings, then Apps, Optional features, More Windows features, "
        + "where it is listed as \"Microsoft Print to PDF\".\n\n"
        + "You can also use File, Print and choose any PDF printer you have.";

    /// <summary>
    /// Shows the print dialog and prints a copy of the document
    /// (Section 6.17).
    /// </summary>
    /// <returns>False when the dialog was cancelled or the print failed.</returns>
    internal static bool Print(FlowDocument document, string documentName, PageSetup page, Window? owner = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(page);

        var dialog = new System.Windows.Controls.PrintDialog
        {
            UserPageRangeEnabled = true,
        };

        ApplyTicket(dialog, page);

        if (dialog.ShowDialog() != true)
        {
            return false;
        }

        return Send(dialog, document, documentName, page);
    }

    /// <summary>
    /// Prints the same copy to Microsoft Print to PDF, which asks where to put
    /// the file itself (Section 6.17).
    /// </summary>
    /// <remarks>
    /// There is no target path here on purpose. The Print to PDF driver shows
    /// Windows' own Save As dialog, and WPF's print path has nowhere to pass a
    /// file name through to it - the only API that can is the Win32 one that
    /// XpsDocumentWriter does not expose. Asking the user for a path first and
    /// then having Windows ask again would be worse than letting Windows ask
    /// once.
    /// </remarks>
    /// <returns>
    /// False when the printer is missing, in which case the caller shows
    /// <see cref="PdfPrinterMissingMessage"/>.
    /// </returns>
    internal static bool ExportPdf(FlowDocument document, string documentName, PageSetup page)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(page);

        var queue = FindPdfPrinter();

        if (queue is null)
        {
            return false;
        }

        var dialog = new System.Windows.Controls.PrintDialog { PrintQueue = queue };

        ApplyTicket(dialog, page);

        return Send(dialog, document, documentName, page);
    }

    /// <summary>The Print to PDF queue, or null when it is not installed.</summary>
    internal static PrintQueue? FindPdfPrinter()
    {
        try
        {
            using var server = new LocalPrintServer();

            foreach (var queue in server.GetPrintQueues(
                         [EnumeratedPrintQueueTypes.Local, EnumeratedPrintQueueTypes.Connections]))
            {
                if (string.Equals(queue.Name, PdfPrinterName, StringComparison.OrdinalIgnoreCase))
                {
                    return queue;
                }

                queue.Dispose();
            }
        }
        catch (Exception ex) when (ex is PrintQueueException or PrintServerException or PrintSystemException)
        {
            Log.Warn($"the printer list could not be read ({ex.Message})");
        }

        return null;
    }

    private static bool Send(
        System.Windows.Controls.PrintDialog dialog,
        FlowDocument document,
        string documentName,
        PageSetup page)
    {
        try
        {
            var copy = Paginate(
                document, page, dialog.PrintableAreaWidth, dialog.PrintableAreaHeight);

            dialog.PrintDocument(
                ((IDocumentPaginatorSource)copy).DocumentPaginator,
                documentName);

            Log.Info($"printed {documentName} to {dialog.PrintQueue?.Name ?? "the chosen printer"}");

            return true;
        }
        catch (Exception ex) when (ex is PrintSystemException or PrintQueueException or IOException)
        {
            Log.Error($"printing {documentName} failed", ex);

            throw new PrintFailed(
                $"{documentName} could not be printed: {ex.Message}", ex);
        }
    }

    /// <summary>Why a print could not be done, in words meant for the user.</summary>
    internal sealed class PrintFailed(string message, Exception? inner = null)
        : Exception(message, inner);

    /// <summary>
    /// A copy of the document laid out for one printed page
    /// (Section 6.17).
    /// </summary>
    /// <param name="source">The document on screen. Left untouched.</param>
    /// <param name="page">The paper and margins the document carries.</param>
    /// <param name="printableWidth">The printer's printable width, in DIPs.</param>
    /// <param name="printableHeight">The printer's printable height, in DIPs.</param>
    internal static FlowDocument Paginate(
        FlowDocument source,
        PageSetup page,
        double printableWidth,
        double printableHeight)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(page);

        var copy = Clone(source);

        copy.PageWidth = printableWidth > 0 ? printableWidth : WidthOf(page);
        copy.PageHeight = printableHeight > 0 ? printableHeight : HeightOf(page);

        copy.PagePadding = new Thickness(
            page.MarginLeftPt * PointsToDip,
            page.MarginTopPt * PointsToDip,
            page.MarginRightPt * PointsToDip,
            page.MarginBottomPt * PointsToDip);

        // One column, not two. See the remarks on this class.
        copy.ColumnWidth = copy.PageWidth;
        copy.ColumnGap = 0;

        // The editor draws the page at a zoom; paper has none.
        copy.IsOptimalParagraphEnabled = false;

        return copy;
    }

    /// <summary>
    /// A deep copy of a FlowDocument, images and all.
    /// </summary>
    /// <remarks>
    /// Through <c>DataFormats.XamlPackage</c> rather than <c>DataFormats.Xaml</c>:
    /// the package form carries the images as parts beside the markup, while
    /// plain XAML leaves a reference behind and the copy prints with the
    /// pictures missing.
    /// </remarks>
    internal static FlowDocument Clone(FlowDocument source)
    {
        ArgumentNullException.ThrowIfNull(source);

        using var buffer = new MemoryStream();

        new TextRange(source.ContentStart, source.ContentEnd)
            .Save(buffer, DataFormats.XamlPackage);

        buffer.Position = 0;

        var copy = new FlowDocument
        {
            FontFamily = source.FontFamily,
            FontSize = source.FontSize,
            Foreground = System.Windows.Media.Brushes.Black,
            Background = System.Windows.Media.Brushes.White,
        };

        new TextRange(copy.ContentStart, copy.ContentEnd)
            .Load(buffer, DataFormats.XamlPackage);

        return copy;
    }

    /// <summary>
    /// Puts the document's paper size and orientation on the print ticket, so
    /// the dialog opens on what the document says rather than on whatever the
    /// printer was last used for (Section 6.17).
    /// </summary>
    private static void ApplyTicket(System.Windows.Controls.PrintDialog dialog, PageSetup page)
    {
        try
        {
            var ticket = dialog.PrintTicket ?? new PrintTicket();

            ticket.PageMediaSize = new PageMediaSize(MediaFor(page.Paper));
            ticket.PageOrientation = page.Landscape
                ? PageOrientation.Landscape
                : PageOrientation.Portrait;

            dialog.PrintTicket = ticket;
        }
        catch (Exception ex) when (ex is PrintSystemException or NotSupportedException)
        {
            // A printer that will not take the ticket still prints; it just
            // uses its own default paper, which is better than not printing.
            Log.Warn($"the print ticket was not accepted ({ex.Message})");
        }
    }

    internal static PageMediaSizeName MediaFor(string paper) => paper switch
    {
        "Legal" => PageMediaSizeName.NorthAmericaLegal,
        "A4" => PageMediaSizeName.ISOA4,
        "A5" => PageMediaSizeName.ISOA5,
        _ => PageMediaSizeName.NorthAmericaLetter,
    };

    /// <summary>The paper sizes Section 6.17 offers, in points.</summary>
    private static readonly Dictionary<string, (double WidthPt, double HeightPt)> Papers = new()
    {
        ["Letter"] = (612, 792),
        ["Legal"] = (612, 1008),
        ["A4"] = (595.3, 841.9),
        ["A5"] = (419.5, 595.3),
    };

    /// <summary>The paper names the Page Setup dialog lists.</summary>
    internal static IReadOnlyList<string> PaperNames { get; } = [..Papers.Keys];

    internal static double WidthOf(PageSetup page)
    {
        ArgumentNullException.ThrowIfNull(page);

        var size = SizeOf(page.Paper);

        return (page.Landscape ? size.HeightPt : size.WidthPt) * PointsToDip;
    }

    internal static double HeightOf(PageSetup page)
    {
        ArgumentNullException.ThrowIfNull(page);

        var size = SizeOf(page.Paper);

        return (page.Landscape ? size.WidthPt : size.HeightPt) * PointsToDip;
    }

    private static (double WidthPt, double HeightPt) SizeOf(string paper) =>
        Papers.TryGetValue(paper, out var size) ? size : Papers["Letter"];
}
