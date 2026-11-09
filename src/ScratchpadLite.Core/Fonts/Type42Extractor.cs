using System.Text;
using ScratchpadLite.Core.Formats;

namespace ScratchpadLite.Core.Fonts;

/// <summary>
/// Type 42 to sfnt (Section 6.6.3). A .t42 file is a PostScript wrapper
/// around a TrueType font: the sfnt bytes live in the hex strings of the
/// sfnts array and are reassembled in order.
/// </summary>
/// <remarks>
/// The one rule that is easy to miss, and which Adobe Technical Note #5012
/// spells out: a PostScript string cannot exceed 65535 bytes, so a font is cut
/// into pieces, and <b>each piece is padded to an even length</b>. The pad byte
/// is not part of the font. Reassembling without dropping those bytes gives a
/// font whose table directory is a byte or two out of step for every table
/// after the first split - which looks like a corrupt font rather than a
/// reassembly bug.
/// <para>
/// The pad only applies to a string that was split from a longer run, so a
/// string whose length is odd on purpose - the last piece - keeps its last
/// byte. What distinguishes them: only a string of the maximum chunk size has
/// been split.
/// </para>
/// </remarks>
public static class Type42Extractor
{
    public static byte[] ToSfnt(Stream t42)
    {
        ArgumentNullException.ThrowIfNull(t42);

        using var buffer = new MemoryStream();
        t42.CopyTo(buffer);

        var text = Encoding.Latin1.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);

        var sfntsAt = text.IndexOf("/sfnts", StringComparison.Ordinal);
        if (sfntsAt < 0)
        {
            throw new FontFormatException(
                "this PostScript font has no /sfnts array, so it holds no TrueType data");
        }

        var chunks = ReadHexStrings(text, sfntsAt + "/sfnts".Length);

        if (chunks.Count == 0)
        {
            throw new FontFormatException("the /sfnts array is empty");
        }

        Limits.CheckDecodedFontSize(chunks.Sum(c => (long)c.Length));

        // Both readings are tried, and validation picks the winner. The
        // specification says each string is padded to an even length, but it
        // does not record whether a pad byte was added - so a reader cannot
        // tell a padded 2048-byte string from an unpadded one. Guessing either
        // way corrupts half the fonts in existence: too eager and a real byte
        // is dropped from every chunk, too cautious and every table after the
        // first split is one byte out of step. Trying both and keeping the one
        // that produces a valid table directory needs no guess at all.
        FontFormatException? first = null;

        foreach (var stripPadding in new[] { false, true })
        {
            try
            {
                return Reassemble(chunks, stripPadding);
            }
            catch (FontFormatException ex)
            {
                first ??= ex;
            }
        }

        throw new FontFormatException(
            "the TrueType data in the /sfnts array did not reassemble into a valid font "
            + $"({first!.Message})", first);
    }

    private static byte[] Reassemble(List<byte[]> chunks, bool stripPadding)
    {
        using var joined = new MemoryStream();

        for (var i = 0; i < chunks.Count; i++)
        {
            var chunk = chunks[i];

            // Only a chunk that was split from a longer run can carry a pad
            // byte, and the last chunk never is.
            var padded = stripPadding
                         && i < chunks.Count - 1
                         && chunk.Length % 2 == 0
                         && chunk.Length > 1;

            joined.Write(chunk, 0, padded ? chunk.Length - 1 : chunk.Length);
        }

        var sfnt = joined.GetBuffer().AsSpan(0, (int)joined.Length);

        var (flavor, tables) = Sfnt.ReadTablesAt(sfnt, 0);

        // Reading the directory is not enough to tell the two conventions
        // apart. The header and the directory are in the first chunk, so they
        // are intact either way; guessing wrong only shifts everything after
        // the first split, and the offsets still land inside the file. So the
        // wrong reading parses happily and yields nonsense tables.
        //
        // What does distinguish them is reading the font's name: a name table
        // that is really the middle of some other table does not produce a
        // family name. That is the test, and it has to be the family name
        // rather than merely "FontInspector did not throw", because it does
        // not throw for a name table it cannot find anything in.
        if (FontInspector.Read(sfnt).Families.Count == 0)
        {
            throw new FontFormatException(
                "the reassembled font has no family name, so the pieces went together wrongly");
        }

        // Rebuilt rather than returned raw, so the checksums are right.
        return Sfnt.Build(flavor, tables);
    }

    /// <summary>
    /// Reads the hex strings of the array, in order, stopping at the end of
    /// the array definition.
    /// </summary>
    private static List<byte[]> ReadHexStrings(string text, int from)
    {
        var chunks = new List<byte[]>();
        var at = from;

        while (at < text.Length)
        {
            var open = text.IndexOf('<', at);

            if (open < 0)
            {
                break;
            }

            // "def" or "readonly def" ends the array; a '<' after that belongs
            // to something else in the file.
            var definition = text.IndexOf(" def", at, StringComparison.Ordinal);
            if (definition >= 0 && definition < open)
            {
                break;
            }

            var close = text.IndexOf('>', open + 1);
            if (close < 0)
            {
                throw new FontFormatException("a hex string in the /sfnts array is not closed");
            }

            var chunk = DecodeHex(text.AsSpan(open + 1, close - open - 1));

            if (chunk.Length > 0)
            {
                chunks.Add(chunk);
            }

            at = close + 1;
        }

        return chunks;
    }

    private static byte[] DecodeHex(ReadOnlySpan<char> text)
    {
        var bytes = new List<byte>(text.Length / 2);
        var high = -1;

        foreach (var c in text)
        {
            var digit = c switch
            {
                >= '0' and <= '9' => c - '0',
                >= 'a' and <= 'f' => c - 'a' + 10,
                >= 'A' and <= 'F' => c - 'A' + 10,
                // Whitespace inside a hex string is allowed and ignored;
                // anything else means the file is not what it claims.
                ' ' or '\t' or '\r' or '\n' or '\f' or '\0' => -1,
                _ => throw new FontFormatException($"'{c}' is not valid inside a hex string"),
            };

            if (digit < 0)
            {
                continue;
            }

            if (high < 0)
            {
                high = digit;
            }
            else
            {
                bytes.Add((byte)((high << 4) | digit));
                high = -1;
            }
        }

        // An odd number of digits means the last one is the high nibble, which
        // is what the PostScript language reference says.
        if (high >= 0)
        {
            bytes.Add((byte)(high << 4));
        }

        return bytes.ToArray();
    }
}
