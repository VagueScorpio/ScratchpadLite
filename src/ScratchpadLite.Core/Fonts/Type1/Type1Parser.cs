using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using ScratchpadLite.Core.Formats;

namespace ScratchpadLite.Core.Fonts.Type1;

/// <summary>
/// Reads an Adobe Type 1 font program: the PFB or PFA wrapper, the eexec
/// encryption, the font and Private dictionaries, and the encrypted
/// charstrings (Section 6.6.4, steps 1 and 2).
/// </summary>
/// <remarks>
/// A Type 1 font is a PostScript program, and it is encrypted twice. The outer
/// layer (eexec, key 55665) hides the private dictionary; the inner layer
/// (key 4330) hides each charstring and subroutine separately. Neither is
/// security - the keys are published in the Type 1 specification - they are
/// obfuscation from 1985.
/// <para>
/// What makes parsing awkward is that it is a program rather than a file
/// format: the tokens that delimit a charstring are themselves defined inside
/// the font, so <c>RD</c> may be spelled <c>-|</c>, and <c>ND</c> may be
/// <c>|-</c> or <c>noaccess def</c>. This reads the shapes that real fonts use
/// rather than trying to interpret PostScript.
/// </para>
/// </remarks>
public static class Type1Parser
{
    private const ushort EexecKey = 55665;
    private const ushort CharstringKey = 4330;
    private const ushort C1 = 52845;
    private const ushort C2 = 22719;

    /// <summary>Everything the converter needs out of a Type 1 font.</summary>
    public sealed record Font
    {
        public string? FontName { get; init; }

        public string? FamilyName { get; init; }

        public string? FullName { get; init; }

        public string? Weight { get; init; }

        public string? Notice { get; init; }

        public string? Version { get; init; }

        public double ItalicAngle { get; init; }

        public bool IsFixedPitch { get; init; }

        public double UnderlinePosition { get; init; } = -100;

        public double UnderlineThickness { get; init; } = 50;

        /// <summary>xMin, yMin, xMax, yMax in font units.</summary>
        public double[] FontBBox { get; init; } = [0, 0, 1000, 1000];

        /// <summary>Usually [0.001 0 0 0.001 0 0], so one unit per 1000 em.</summary>
        public double[] FontMatrix { get; init; } = [0.001, 0, 0, 0.001, 0, 0];

        /// <summary>Code to glyph name, for the font's own encoding.</summary>
        public Dictionary<int, string> Encoding { get; init; } = [];

        /// <summary>True when the font says "StandardEncoding" rather than listing one.</summary>
        public bool UsesStandardEncoding { get; init; }

        /// <summary>Glyph name to decrypted charstring.</summary>
        public Dictionary<string, byte[]> CharStrings { get; init; } = [];

        /// <summary>Decrypted subroutines, by index.</summary>
        public List<byte[]> Subrs { get; init; } = [];

        public double[] BlueValues { get; init; } = [];

        public double[] OtherBlues { get; init; } = [];

        public double StdHw { get; init; }

        public double StdVw { get; init; }

        /// <summary>True when the font is one of the variants that cannot be converted.</summary>
        public string? Unsupported { get; init; }
    }

    public static Font Parse(Stream input)
    {
        ArgumentNullException.ThrowIfNull(input);

        using var buffer = new MemoryStream();
        input.CopyTo(buffer);

        return Parse(buffer.GetBuffer().AsSpan(0, (int)buffer.Length));
    }

    public static Font Parse(ReadOnlySpan<byte> file)
    {
        var (clear, encrypted) = Unwrap(file);

        var clearText = Encoding.Latin1.GetString(clear);

        if (Rejection(clearText) is { } reason)
        {
            return new Font { Unsupported = reason };
        }

        var privateText = Encoding.Latin1.GetString(Decrypt(encrypted, EexecKey, 4));
        var lenIv = ReadInt(privateText, "/lenIV") ?? 4;

        var font = new Font
        {
            FontName = ReadName(clearText, "/FontName"),
            FamilyName = ReadString(clearText, "/FamilyName"),
            FullName = ReadString(clearText, "/FullName"),
            Weight = ReadString(clearText, "/Weight"),
            Notice = ReadString(clearText, "/Notice"),
            Version = ReadString(clearText, "/version"),
            ItalicAngle = ReadDouble(clearText, "/ItalicAngle") ?? 0,
            IsFixedPitch = ReadBool(clearText, "/isFixedPitch"),
            UnderlinePosition = ReadDouble(clearText, "/UnderlinePosition") ?? -100,
            UnderlineThickness = ReadDouble(clearText, "/UnderlineThickness") ?? 50,
            FontBBox = ReadNumbers(clearText, "/FontBBox", 4) ?? [0, 0, 1000, 1000],
            FontMatrix = ReadNumbers(clearText, "/FontMatrix", 6) ?? [0.001, 0, 0, 0.001, 0, 0],
            UsesStandardEncoding = clearText.Contains("/Encoding StandardEncoding", StringComparison.Ordinal),
            BlueValues = ReadNumbers(privateText, "/BlueValues", -1) ?? [],
            OtherBlues = ReadNumbers(privateText, "/OtherBlues", -1) ?? [],
            StdHw = ReadNumbers(privateText, "/StdHW", 1)?.FirstOrDefault() ?? 0,
            StdVw = ReadNumbers(privateText, "/StdVW", 1)?.FirstOrDefault() ?? 0,
        };

        if (!font.UsesStandardEncoding)
        {
            ReadEncoding(clearText, font.Encoding);
        }

        var privateBytes = Decrypt(encrypted, EexecKey, 4);

        ReadSubrs(privateBytes, privateText, lenIv, font.Subrs);
        ReadCharStrings(privateBytes, privateText, lenIv, font.CharStrings);

        if (font.CharStrings.Count == 0)
        {
            throw new FontFormatException("the font's CharStrings dictionary could not be read");
        }

        return font;
    }

    /// <summary>
    /// The variants Section 6.6.3 says to report rather than convert.
    /// </summary>
    private static string? Rejection(string clearText)
    {
        if (clearText.Contains("/WeightVector", StringComparison.Ordinal)
            || clearText.Contains("/BlendDesignPositions", StringComparison.Ordinal)
            || clearText.Contains("/BlendAxisTypes", StringComparison.Ordinal))
        {
            return "This is a Multiple Master font, which cannot be converted. "
                   + "Look for a single-instance .pfb or an .otf version.";
        }

        if (clearText.Contains("/CIDFontType", StringComparison.Ordinal)
            || clearText.Contains("/CIDInit", StringComparison.Ordinal))
        {
            return "This is a CID-keyed font, which cannot be converted. Look for an .otf version.";
        }

        return null;
    }

    /// <summary>
    /// Splits the file into its clear-text part and its encrypted part,
    /// handling both the PFB and the PFA wrapper.
    /// </summary>
    private static (byte[] Clear, byte[] Encrypted) Unwrap(ReadOnlySpan<byte> file)
    {
        if (file.Length > 1 && file[0] == 0x80)
        {
            return UnwrapPfb(file);
        }

        // PFA: clear text up to and including "eexec", then the encrypted part
        // as hex.
        var text = Encoding.Latin1.GetString(file);
        var eexec = text.IndexOf("eexec", StringComparison.Ordinal);

        if (eexec < 0)
        {
            throw new FontFormatException("this file has no eexec section, so it is not a Type 1 font");
        }

        var clear = Encoding.Latin1.GetBytes(text[..(eexec + 5)]);
        var rest = text.AsSpan(eexec + 5);

        return (clear, DecodeHexOrBinary(rest));
    }

    private static (byte[] Clear, byte[] Encrypted) UnwrapPfb(ReadOnlySpan<byte> file)
    {
        var clear = new MemoryStream();
        var binary = new MemoryStream();
        var at = 0;

        while (at + 2 <= file.Length && file[at] == 0x80)
        {
            var type = file[at + 1];

            if (type == 0x03)
            {
                break;
            }

            if (at + 6 > file.Length)
            {
                throw new FontFormatException("a PFB segment header runs past the end of the file");
            }

            // PFB segment lengths are little-endian, unlike everything else in
            // a font file.
            var length = BinaryPrimitives.ReadUInt32LittleEndian(file[(at + 2)..]);
            at += 6;

            if (length > (uint)(file.Length - at))
            {
                throw new FontFormatException(
                    $"a PFB segment claims {length} bytes but the file has {file.Length - at} left");
            }

            var body = file.Slice(at, (int)length);

            switch (type)
            {
                case 0x01:
                    // The trailing segment of zeros and "cleartomark" is ASCII
                    // too, and must not land in the clear-text dictionary.
                    if (binary.Length == 0)
                    {
                        clear.Write(body);
                    }

                    break;

                case 0x02:
                    binary.Write(body);
                    break;

                default:
                    throw new FontFormatException($"a PFB segment has unknown type {type}");
            }

            at += (int)length;
        }

        if (binary.Length == 0)
        {
            throw new FontFormatException("this PFB file holds no binary segment, so it has no glyphs");
        }

        return (clear.ToArray(), binary.ToArray());
    }

    /// <summary>
    /// Reads the encrypted part of a PFA, which is hex, tolerating a file that
    /// is actually binary.
    /// </summary>
    /// <summary>
    /// Reads the encrypted part of a PFA, which is hex, tolerating a file that
    /// is actually binary.
    /// </summary>
    /// <remarks>
    /// Which it is has to be decided up front, from the first few characters,
    /// and then stuck to. Deciding as it goes does not work: a Type 1 program
    /// ends with 512 zeros and the word <c>cleartomark</c>, and those zeros are
    /// perfectly good hex digits. A decoder that keeps going until something is
    /// not hex swallows 256 bytes of padding into the encrypted data and only
    /// then discovers it was wrong - at which point the private dictionary is
    /// unreadable and the font looks corrupt.
    /// </remarks>
    private static byte[] DecodeHexOrBinary(ReadOnlySpan<char> rest)
    {
        if (!LooksLikeHex(rest))
        {
            return Encoding.Latin1.GetBytes(rest.ToString().TrimStart('\r', '\n', ' ', '\t'));
        }

        var bytes = new List<byte>(rest.Length / 2);
        var high = -1;

        foreach (var c in rest)
        {
            var digit = c switch
            {
                >= '0' and <= '9' => c - '0',
                >= 'a' and <= 'f' => c - 'a' + 10,
                >= 'A' and <= 'F' => c - 'A' + 10,
                _ => -1,
            };

            if (digit < 0)
            {
                if (char.IsWhiteSpace(c))
                {
                    continue;
                }

                // The end of the hex: whatever follows is the cleartomark
                // trailer, not font data.
                break;
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

        return bytes.ToArray();
    }

    /// <summary>
    /// Whether the encrypted section is hex, from its first four non-blank
    /// characters.
    /// </summary>
    /// <remarks>
    /// Four is enough: the first bytes of the encrypted section are random
    /// padding, so in a binary file they are almost never four hex characters
    /// in a row - and the files that matter here, the URW fonts, put raw binary
    /// straight after eexec with no wrapper at all.
    /// </remarks>
    private static bool LooksLikeHex(ReadOnlySpan<char> rest)
    {
        var seen = 0;

        foreach (var c in rest)
        {
            if (char.IsWhiteSpace(c))
            {
                continue;
            }

            var isHex = c is (>= '0' and <= '9') or (>= 'a' and <= 'f') or (>= 'A' and <= 'F');

            if (!isHex)
            {
                return false;
            }

            if (++seen == 4)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The eexec and charstring decryption, which is the same algorithm with
    /// different keys (Section 6.6.4, step 1).
    /// </summary>
    public static byte[] Decrypt(ReadOnlySpan<byte> cipher, ushort key, int skip)
    {
        if (cipher.Length <= skip)
        {
            return [];
        }

        var r = key;
        var plain = new byte[cipher.Length];

        for (var i = 0; i < cipher.Length; i++)
        {
            var c = cipher[i];
            plain[i] = (byte)(c ^ (r >> 8));
            r = (ushort)(((c + r) * C1) + C2);
        }

        // The first few plaintext bytes are random padding, there to make the
        // key harder to guess. lenIV says how many.
        return plain.AsSpan(skip).ToArray();
    }

    /// <summary>Decrypts one charstring with the inner key.</summary>
    public static byte[] DecryptCharString(ReadOnlySpan<byte> cipher, int lenIv) =>
        lenIv < 0 ? cipher.ToArray() : Decrypt(cipher, CharstringKey, lenIv);

    /// <summary>
    /// Reads <c>/Subrs N array dup i L RD ... NP</c>.
    /// </summary>
    private static void ReadSubrs(byte[] privateBytes, string privateText, int lenIv, List<byte[]> subrs)
    {
        var subrsAt = privateText.IndexOf("/Subrs", StringComparison.Ordinal);

        if (subrsAt < 0)
        {
            return;
        }

        var count = ReadInt(privateText[subrsAt..], "/Subrs") ?? 0;

        if (count is <= 0 or > 65536)
        {
            return;
        }

        for (var i = 0; i < count; i++)
        {
            subrs.Add([]);
        }

        var at = subrsAt;

        for (var found = 0; found < count; found++)
        {
            var dup = privateText.IndexOf("dup ", at, StringComparison.Ordinal);

            if (dup < 0)
            {
                break;
            }

            at = dup + 4;

            if (!TryReadBinaryEntry(privateBytes, privateText, at, out var index, out var data))
            {
                continue;
            }

            at = data.End;

            if (index >= 0 && index < subrs.Count)
            {
                subrs[index] = DecryptCharString(privateBytes.AsSpan(data.Start, data.Length), lenIv);
            }
        }
    }

    /// <summary>
    /// Reads <c>/CharStrings N dict dup begin /name L RD ... ND</c>.
    /// </summary>
    private static void ReadCharStrings(
        byte[] privateBytes,
        string privateText,
        int lenIv,
        Dictionary<string, byte[]> charStrings)
    {
        var start = privateText.IndexOf("/CharStrings", StringComparison.Ordinal);

        if (start < 0)
        {
            return;
        }

        var at = privateText.IndexOf("begin", start, StringComparison.Ordinal);
        at = at < 0 ? start + "/CharStrings".Length : at + "begin".Length;

        while (at < privateText.Length)
        {
            var slash = privateText.IndexOf('/', at);

            if (slash < 0)
            {
                break;
            }

            var nameEnd = slash + 1;
            while (nameEnd < privateText.Length && !IsDelimiter(privateText[nameEnd]))
            {
                nameEnd++;
            }

            var name = privateText[(slash + 1)..nameEnd];

            if (name.Length == 0)
            {
                at = slash + 1;
                continue;
            }

            if (!TryReadBinaryEntry(privateBytes, privateText, nameEnd, out _, out var data))
            {
                // "end" after the last glyph, or a key that is not a glyph.
                if (name is "end" || privateText.AsSpan(slash).StartsWith("/Private"))
                {
                    break;
                }

                at = nameEnd;
                continue;
            }

            charStrings[name] = DecryptCharString(privateBytes.AsSpan(data.Start, data.Length), lenIv);
            at = data.End;
        }
    }

    private readonly record struct Binary(int Start, int Length, int End);

    /// <summary>
    /// Reads "&lt;int&gt; &lt;length&gt; RD &lt;length bytes&gt;" at
    /// <paramref name="at"/>.
    /// </summary>
    /// <remarks>
    /// The token before the bytes is whatever the font defined it as - RD, -|,
    /// or something else entirely - so what is looked for is the shape: one or
    /// two integers, a token, exactly one space, then binary. Byte offsets and
    /// character offsets are the same here because the private part is read as
    /// Latin-1, one byte per character, which is the only reason this works.
    /// </remarks>
    private static bool TryReadBinaryEntry(
        byte[] privateBytes,
        string text,
        int at,
        out int index,
        out Binary data)
    {
        index = -1;
        data = default;

        var numbers = new List<int>(2);
        var i = at;

        while (i < text.Length && numbers.Count < 2)
        {
            while (i < text.Length && text[i] is ' ' or '\r' or '\n' or '\t')
            {
                i++;
            }

            var numberStart = i;
            while (i < text.Length && text[i] is >= '0' and <= '9')
            {
                i++;
            }

            if (i == numberStart)
            {
                break;
            }

            if (!int.TryParse(text.AsSpan(numberStart, i - numberStart), out var value))
            {
                return false;
            }

            numbers.Add(value);
        }

        if (numbers.Count == 0)
        {
            return false;
        }

        while (i < text.Length && text[i] == ' ')
        {
            i++;
        }

        // The RD token: anything that is not a space, up to the single space
        // that precedes the binary.
        var tokenStart = i;
        while (i < text.Length && text[i] != ' ')
        {
            i++;
        }

        if (i >= text.Length || i == tokenStart)
        {
            return false;
        }

        var length = numbers[^1];
        var binaryStart = i + 1;

        if (length < 0 || binaryStart > privateBytes.Length - length)
        {
            return false;
        }

        if (numbers.Count == 2)
        {
            index = numbers[0];
        }

        data = new Binary(binaryStart, length, binaryStart + length);
        return true;
    }

    /// <summary>Reads <c>dup &lt;code&gt; /&lt;name&gt; put</c> entries.</summary>
    private static void ReadEncoding(string clearText, Dictionary<int, string> encoding)
    {
        var at = clearText.IndexOf("/Encoding", StringComparison.Ordinal);

        if (at < 0)
        {
            return;
        }

        // "readonly def" or "def" ends the encoding array.
        var end = clearText.IndexOf(" def", at, StringComparison.Ordinal);
        var limit = end < 0 ? clearText.Length : end;

        while (at < limit)
        {
            var dup = clearText.IndexOf("dup ", at, StringComparison.Ordinal);

            if (dup < 0 || dup >= limit)
            {
                break;
            }

            at = dup + 4;

            var slash = clearText.IndexOf('/', at);
            if (slash < 0 || slash >= limit)
            {
                break;
            }

            if (!int.TryParse(
                    clearText.AsSpan(at, slash - at).Trim(),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var code))
            {
                continue;
            }

            var nameEnd = slash + 1;
            while (nameEnd < limit && !IsDelimiter(clearText[nameEnd]))
            {
                nameEnd++;
            }

            if (code is >= 0 and < 256)
            {
                encoding[code] = clearText[(slash + 1)..nameEnd];
            }

            at = nameEnd;
        }
    }

    private static bool IsDelimiter(char c) =>
        c is ' ' or '\t' or '\r' or '\n' or '\f' or '\0' or '/' or '(' or ')' or '[' or ']' or '{' or '}' or '<' or '>';

    private static string? ReadName(string text, string key)
    {
        var at = text.IndexOf(key, StringComparison.Ordinal);

        if (at < 0)
        {
            return null;
        }

        at += key.Length;

        var slash = text.IndexOf('/', at);
        if (slash < 0 || slash > at + 8)
        {
            return null;
        }

        var end = slash + 1;
        while (end < text.Length && !IsDelimiter(text[end]))
        {
            end++;
        }

        return text[(slash + 1)..end];
    }

    private static string? ReadString(string text, string key)
    {
        var at = text.IndexOf(key, StringComparison.Ordinal);

        if (at < 0)
        {
            return null;
        }

        var open = text.IndexOf('(', at);

        if (open < 0 || open > at + key.Length + 4)
        {
            return null;
        }

        // Parentheses nest in PostScript strings.
        var depth = 1;
        var i = open + 1;
        var value = new StringBuilder();

        while (i < text.Length && depth > 0)
        {
            var c = text[i];

            if (c == '\\' && i + 1 < text.Length)
            {
                value.Append(text[i + 1]);
                i += 2;
                continue;
            }

            switch (c)
            {
                case '(':
                    depth++;
                    value.Append(c);
                    break;
                case ')':
                    depth--;
                    if (depth > 0)
                    {
                        value.Append(c);
                    }

                    break;
                default:
                    value.Append(c);
                    break;
            }

            i++;
        }

        var result = value.ToString().Trim();
        return result.Length == 0 ? null : result;
    }

    private static int? ReadInt(string text, string key) =>
        ReadNumbers(text, key, 1) is { Length: > 0 } values ? (int)values[0] : null;

    private static double? ReadDouble(string text, string key) =>
        ReadNumbers(text, key, 1) is { Length: > 0 } values ? values[0] : null;

    private static bool ReadBool(string text, string key)
    {
        var at = text.IndexOf(key, StringComparison.Ordinal);

        return at >= 0
               && text.AsSpan(at + key.Length, Math.Min(12, text.Length - at - key.Length))
                   .Contains("true", StringComparison.Ordinal);
    }

    /// <summary>
    /// Reads the numbers after a key, optionally inside brackets or braces.
    /// A <paramref name="count"/> of -1 means "as many as there are".
    /// </summary>
    private static double[]? ReadNumbers(string text, string key, int count)
    {
        var at = text.IndexOf(key, StringComparison.Ordinal);

        if (at < 0)
        {
            return null;
        }

        at += key.Length;

        var numbers = new List<double>(count > 0 ? count : 8);
        var i = at;
        var sawBracket = false;

        while (i < text.Length && (count < 0 || numbers.Count < count))
        {
            var c = text[i];

            if (c is '[' or '{')
            {
                sawBracket = true;
                i++;
                continue;
            }

            if (c is ']' or '}')
            {
                break;
            }

            if (c is ' ' or '\t' or '\r' or '\n')
            {
                i++;
                continue;
            }

            if (c is '-' or '.' or (>= '0' and <= '9'))
            {
                var start = i;
                i++;

                while (i < text.Length && (text[i] is '.' or 'e' or 'E' or '-' or '+' or (>= '0' and <= '9')))
                {
                    i++;
                }

                if (double.TryParse(
                        text.AsSpan(start, i - start),
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out var value))
                {
                    numbers.Add(value);
                }

                continue;
            }

            // A token rather than a number: the end of the values, unless the
            // numbers are inside brackets and this is the "def" after them.
            if (!sawBracket || numbers.Count > 0)
            {
                break;
            }

            i++;
        }

        return numbers.Count == 0 ? null : numbers.ToArray();
    }
}
