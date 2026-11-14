using System.Text;
using ScratchpadLite.Core.Model;
using ScratchpadLite.Core.Platform;

namespace ScratchpadLite.Core.Formats;

/// <summary>
/// Plain text (Section 7.2). Reads a BOM, then tries strict UTF-8, then
/// Windows-1252. Writes UTF-8 without a BOM and CRLF line endings by
/// default. Tables become tab-separated rows; list items get a bullet or
/// number prefix.
/// </summary>
/// <remarks>
/// The encoding guess is the whole of the difficulty. A .txt file carries no
/// statement of its encoding unless it has a BOM, and most do not, so it has
/// to be deduced - and getting it wrong is immediately visible as mojibake.
/// The order is the one that cannot go wrong twice: a BOM is definitive; a
/// file that decodes as strict UTF-8 almost certainly is UTF-8, because
/// arbitrary bytes fail UTF-8's continuation rules very quickly; and anything
/// else is treated as Windows-1252, which is what Notepad wrote for twenty
/// years and which never fails, since every byte means something in it.
/// </remarks>
public sealed class TxtFormat : IFormatHandler
{
    /// <summary>Windows-1252, or Latin-1 if the code pages are unavailable.</summary>
    private static readonly Lazy<Encoding> Ansi = new(() =>
    {
        try
        {
            // .NET Core dropped the legacy code pages, and plenty of .txt
            // files are not UTF-8 (Section 7.2).
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            return Encoding.GetEncoding(1252);
        }
        catch (Exception ex) when (ex is NotSupportedException or ArgumentException)
        {
            Log.Warn("Windows-1252 is unavailable; falling back to Latin-1 for plain text");

            return Encoding.Latin1;
        }
    });

    public string Name => "Plain Text";

    public IReadOnlyList<string> Extensions => [".txt"];

    public bool CanRead => true;

    public bool CanWrite => true;

    /// <summary>
    /// Nothing survives a save as plain text, which is what the save warning
    /// exists to say.
    /// </summary>
    public Capabilities Capabilities => Capabilities.None;

    /// <summary>What the file's own line endings were, so a save can keep them.</summary>
    public const string LineEndingKey = "txt.lineEnding";

    /// <summary>What encoding the file was read as, so a save can keep it.</summary>
    public const string EncodingKey = "txt.encoding";

    public ReadResult Read(Stream input, string sourcePath, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(input);

        using var buffer = new MemoryStream();
        input.CopyTo(buffer);

        var bytes = buffer.GetBuffer().AsSpan(0, (int)buffer.Length);
        var (text, encoding) = Decode(bytes);
        var ending = DetectLineEnding(text);

        var warnings = new List<string>();
        var blocks = new List<Block>();

        // Every line is a paragraph, and Split handles all three endings so a
        // file with mixed endings - which happens - still reads correctly.
        foreach (var line in text.ReplaceLineEndings("\n").Split('\n'))
        {
            ct.ThrowIfCancellationRequested();

            blocks.Add(new Paragraph
            {
                Inlines = line.Length == 0 ? [] : [new TextRun(line, new CharStyle())],
            });
        }

        // A file ending in a newline would otherwise gain an empty paragraph
        // that was never in it.
        if (blocks.Count > 1 && blocks[^1] is Paragraph { Inlines.Count: 0 })
        {
            blocks.RemoveAt(blocks.Count - 1);
        }

        if (encoding == Ansi.Value)
        {
            warnings.Add(
                "This file is not UTF-8, so it was read as Windows-1252. "
                + "If any characters look wrong, that is why.");
        }

        var meta = new Dictionary<string, string>
        {
            [LineEndingKey] = ending,
            [EncodingKey] = encoding.WebName,
        };

        return new ReadResult(
            new SpDocument(blocks, meta, new PageSetup(), new HashSet<string>()),
            warnings);
    }

    public void Write(
        SpDocument doc,
        Stream output,
        string targetPath,
        CancellationToken ct,
        WriteOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentNullException.ThrowIfNull(output);

        // The file's own line endings are kept when it had any, so saving a
        // file does not rewrite every line of it for someone else's diff.
        var ending = doc.Meta.TryGetValue(LineEndingKey, out var stored) && stored.Length > 0
            ? stored
            : "\r\n";

        var withBom = doc.Meta.TryGetValue(EncodingKey, out var name)
                      && name.Equals("utf-8-bom", StringComparison.OrdinalIgnoreCase);

        using var writer = new StreamWriter(
            output,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: withBom),
            leaveOpen: true)
        {
            NewLine = ending,
        };

        var lines = new List<string>();
        Flatten(doc.Blocks, lines, ct);

        for (var i = 0; i < lines.Count; i++)
        {
            if (i > 0)
            {
                writer.Write(ending);
            }

            writer.Write(lines[i]);
        }

        writer.Flush();
    }

    /// <summary>
    /// Turns blocks into lines of text, with the conventions from
    /// Section 7.2.
    /// </summary>
    private static void Flatten(IReadOnlyList<Block> blocks, List<string> lines, CancellationToken ct)
    {
        var numbers = new Dictionary<int, int>();

        foreach (var block in blocks)
        {
            ct.ThrowIfCancellationRequested();

            switch (block)
            {
                case Paragraph paragraph:
                {
                    var text = Flatten(paragraph.Inlines);

                    if (paragraph.List is { } list)
                    {
                        var indent = new string(' ', list.Level * 2);

                        if (list.Kind == ListKind.Bullet)
                        {
                            lines.Add($"{indent}• {text}");
                        }
                        else
                        {
                            // Numbered items are counted here, because plain
                            // text has no way to say "numbered list" - the
                            // number has to be in the text.
                            var next = numbers.TryGetValue(list.Level, out var n)
                                ? n + 1
                                : Math.Max(1, list.Start);

                            numbers[list.Level] = next;
                            lines.Add($"{indent}{next}. {text}");
                        }

                        break;
                    }

                    numbers.Clear();
                    lines.Add(text);
                    break;
                }

                case Table table:
                    numbers.Clear();

                    foreach (var row in table.Rows)
                    {
                        // Tab-separated, which is what pastes back into a
                        // spreadsheet as columns.
                        lines.Add(string.Join(
                            '\t',
                            row.Select(cell => Flatten(cell.Blocks).Replace('\t', ' '))));
                    }

                    break;

                case HorizontalRule:
                    numbers.Clear();
                    lines.Add(new string('-', 40));
                    break;
            }
        }
    }

    private static string Flatten(IReadOnlyList<Block> blocks)
    {
        var lines = new List<string>();
        Flatten(blocks, lines, CancellationToken.None);

        return string.Join(" ", lines);
    }

    private static string Flatten(IReadOnlyList<Inline> inlines)
    {
        var text = new StringBuilder();

        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case TextRun run:
                    // All caps that was preserved rather than displayed is
                    // written as what it means, since plain text has no other
                    // way to carry it.
                    text.Append(run.Style.AllCapsPreserved
                        ? run.Text.ToUpperInvariant()
                        : run.Text);
                    break;

                case LineBreak:
                    text.Append(' ');
                    break;

                case ImageRun image:
                    text.Append(image.Alt.Length > 0 ? $"[{image.Alt}]" : "[image]");
                    break;
            }
        }

        return text.ToString();
    }

    /// <summary>
    /// Decodes the bytes, and says which encoding it settled on.
    /// </summary>
    internal static (string Text, Encoding Encoding) Decode(ReadOnlySpan<byte> bytes)
    {
        // A BOM is the only definitive statement a text file makes about
        // itself, so it wins outright.
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return (Encoding.UTF8.GetString(bytes[3..]), Encoding.UTF8);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            return (Encoding.Unicode.GetString(bytes[2..]), Encoding.Unicode);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            return (Encoding.BigEndianUnicode.GetString(bytes[2..]), Encoding.BigEndianUnicode);
        }

        // Strict UTF-8: the decoder throws rather than substituting, which is
        // what makes this a test instead of a guess. Arbitrary bytes fail
        // UTF-8's continuation rules almost immediately.
        try
        {
            var strict = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

            return (strict.GetString(bytes), Encoding.UTF8);
        }
        catch (DecoderFallbackException)
        {
            return (Ansi.Value.GetString(bytes), Ansi.Value);
        }
    }

    /// <summary>The line ending the text mostly uses.</summary>
    internal static string DetectLineEnding(string text)
    {
        var crlf = 0;
        var lf = 0;
        var cr = 0;

        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\r')
            {
                if (i + 1 < text.Length && text[i + 1] == '\n')
                {
                    crlf++;
                    i++;
                }
                else
                {
                    cr++;
                }
            }
            else if (text[i] == '\n')
            {
                lf++;
            }
        }

        if (crlf == 0 && lf == 0 && cr == 0)
        {
            return "\r\n";
        }

        if (crlf >= lf && crlf >= cr)
        {
            return "\r\n";
        }

        return lf >= cr ? "\n" : "\r";
    }
}
