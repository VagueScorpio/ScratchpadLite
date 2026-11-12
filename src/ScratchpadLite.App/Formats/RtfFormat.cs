using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Documents;
using ScratchpadLite.App.Bridge;
using ScratchpadLite.Core.Model;
using ScratchpadLite.Core.Platform;
using WpfParagraph = System.Windows.Documents.Paragraph;

namespace ScratchpadLite.App.Formats;

/// <summary>
/// RTF through the converter built into WPF (Section 7.2). Loads and saves
/// a TextRange with the RTF data format, on the UI thread. After loading,
/// the walker still runs so there is IR for recovery and the save warning.
///
/// The built-in converter does not keep paragraph style names, so headings
/// are guessed after loading: a paragraph whose size and weight match a
/// style sheet heading exactly gets that style name. Saving writes headings
/// out as directly formatted text, which is what the fidelity matrix
/// records.
/// </summary>
/// <remarks>
/// The one format that does not go through the IR, and the reason the
/// <see cref="IFlowFormatHandler"/> interface exists. WPF already has an RTF
/// converter, and it is a good deal better than anything worth hand-writing
/// for this project - but it only speaks FlowDocument, so RTF cannot be read
/// or written on a background thread and cannot live in Core.
/// <para>
/// What it costs: RTF has paragraph styles, and WPF's converter does not keep
/// them, so a heading arrives as 20-point bold text with no name. Rather than
/// lose headings entirely, a paragraph whose size and weight match a style
/// sheet heading exactly is given that style - which is a guess, and is
/// recorded as one in the fidelity matrix.
/// </para>
/// </remarks>
internal sealed class RtfFormat : IFlowFormatHandler
{
    private readonly StyleSheet _styles;

    internal RtfFormat(StyleSheet styles) => _styles = styles;

    public string Name => "Rich Text Format";

    public IReadOnlyList<string> Extensions => [".rtf"];

    /// <summary>
    /// What survives an RTF save. Headings are formatting only, which is why
    /// <see cref="ScratchpadLite.Core.Formats.Capabilities.Headings"/> is not
    /// in the set.
    /// </summary>
    internal static Core.Formats.Capabilities Capabilities =>
        Core.Formats.Capabilities.CharFormat | Core.Formats.Capabilities.Underline
        | Core.Formats.Capabilities.Fonts | Core.Formats.Capabilities.Colors
        | Core.Formats.Capabilities.Lists | Core.Formats.Capabilities.Tables
        | Core.Formats.Capabilities.Images | Core.Formats.Capabilities.Links;

    public void Load(FlowDocument document, Stream input)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(input);

        CheckItLooksLikeRtf(input);

        var range = new TextRange(document.ContentStart, document.ContentEnd);

        try
        {
            range.Load(input, DataFormats.Rtf);
        }
        catch (ArgumentException ex)
        {
            // What WPF's converter throws for data it cannot read at all.
            throw new InvalidDataException("this file is not valid RTF, or is damaged.", ex);
        }

        GuessStyleNames(document);
    }

    public void Save(FlowDocument document, Stream output)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(output);

        var range = new TextRange(document.ContentStart, document.ContentEnd);

        range.Save(output, DataFormats.Rtf);
    }

    /// <summary>
    /// Refuses a file that is not RTF before WPF's converter sees it.
    /// </summary>
    /// <remarks>
    /// The converter is tolerant to a fault: handed a JPEG, or a text file, or
    /// a .docx renamed to .rtf, it does not fail - it reads the bytes as text
    /// and hands back a document full of rubbish, which the user then has
    /// every chance of saving over the original. Every RTF file begins with
    /// <c>{\rtf</c>, so the check is cheap and certain.
    /// </remarks>
    private static void CheckItLooksLikeRtf(Stream input)
    {
        if (!input.CanSeek)
        {
            return;     // nothing to check against without rewinding
        }

        var start = input.Position;
        var header = new byte[5];
        var read = input.Read(header);

        input.Position = start;

        if (read < 5 || Encoding.ASCII.GetString(header) != @"{\rtf")
        {
            throw new InvalidDataException(
                "this file does not begin with {\\rtf, so it is not a Rich Text file. "
                + "It may be a different format with the wrong name.");
        }
    }

    /// <summary>
    /// Gives paragraphs their style names back by matching their formatting
    /// (Section 7.2).
    /// </summary>
    /// <remarks>
    /// Matched on size and weight together, and only on an exact size match, so
    /// ordinary bold text is not promoted to a heading. Where two styles would
    /// match - Heading 5 and Heading 6 are both 11 pt bold in the default
    /// sheet - the first in the sheet's order wins, which is the shallower
    /// heading and the more likely one.
    /// </remarks>
    private void GuessStyleNames(FlowDocument document)
    {
        var guessed = 0;

        foreach (var paragraph in document.Blocks.OfType<WpfParagraph>())
        {
            var sizePt = Units.DipsToPoints(paragraph.FontSize);
            var bold = paragraph.FontWeight.ToOpenTypeWeight() >= FontWeights.Bold.ToOpenTypeWeight();
            var italic = paragraph.FontStyle != FontStyles.Normal;

            // The runs carry the formatting when the paragraph itself does
            // not, which is what WPF's converter usually produces.
            if (paragraph.Inlines.FirstOrDefault() is Run first)
            {
                sizePt = Units.DipsToPoints(first.FontSize);
                bold = first.FontWeight.ToOpenTypeWeight() >= FontWeights.Bold.ToOpenTypeWeight();
                italic = first.FontStyle != FontStyles.Normal;
            }

            if (Match(sizePt, bold, italic) is { } name)
            {
                SpProps.SetStyleName(paragraph, name);
                guessed++;
            }
            else
            {
                SpProps.SetStyleName(paragraph, StyleSheet.NormalStyleName);
            }
        }

        if (guessed > 0)
        {
            Log.Info($"RTF: {guessed} paragraphs were matched to a style by their formatting");
        }
    }

    private string? Match(double sizePt, bool bold, bool italic)
    {
        var normal = _styles[StyleSheet.NormalStyleName];

        // Text at the body size is body text, whatever else it is.
        if (Math.Abs(sizePt - normal.SizePt) < 0.01 && !bold)
        {
            return null;
        }

        foreach (var name in StyleSheet.StyleNames)
        {
            if (name == StyleSheet.NormalStyleName)
            {
                continue;
            }

            var style = _styles[name];

            if (Math.Abs(style.SizePt - sizePt) < 0.01
                && style.Bold == bold
                && style.Italic == italic)
            {
                return name;
            }
        }

        return null;
    }

    /// <summary>
    /// Reads an RTF file into IR as well, for the save warning and recovery
    /// (Section 7.2).
    /// </summary>
    internal SpDocument ReadToIr(Stream input)
    {
        var document = new FlowDocument();

        Load(document, input);

        return FlowWalker.Walk(document, _styles);
    }
}
