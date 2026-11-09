using System.Buffers.Binary;
using ScratchpadLite.Core.Formats;

namespace ScratchpadLite.Core.Fonts;

/// <summary>
/// Embedded OpenType to sfnt (Section 6.6.3). Uncompressed and XOR-masked
/// files are recovered by stripping the EOT header; MicroType Express
/// compressed files are unsupported and reported with the reason
/// (Section 13).
/// </summary>
/// <remarks>
/// EOT was Internet Explorer's web font format, and it is a header bolted on
/// to a whole sfnt rather than a transformation of one: strip the header and
/// the original font is underneath. Two things make it fiddly.
/// <list type="bullet">
///   <item>
///     The header is <b>little-endian</b>, alone among font formats, because it
///     came from Windows rather than from the sfnt world.
///   </item>
///   <item>
///     Its length is variable: a run of counted, UTF-16 strings for the family
///     and style names, then two more fields that only exist from version
///     0x00020001 onwards. The only reliable way through is to walk it.
///   </item>
/// </list>
/// <para>
/// A file whose <c>Flags</c> say MicroType Express cannot be recovered: that is
/// a real compression format owned by Monotype, with no free implementation.
/// Those are refused by name rather than half-decoded.
/// </para>
/// </remarks>
public static class EotDecoder
{
    /// <summary>The magic number at offset 34, little-endian.</summary>
    internal const ushort Magic = 0x504C;

    /// <summary>Flags bit meaning the font data is MicroType Express compressed.</summary>
    private const uint CompressedFlag = 0x00000004;

    /// <summary>Flags bit meaning the font data is XOR-masked with 0x50.</summary>
    private const uint XorMaskedFlag = 0x00000002;

    internal const string CompressedNotSupported =
        "This EOT font is MicroType Express compressed, which cannot be unpacked. "
        + "Look for a .ttf, .otf or .woff version.";

    public static byte[] ToSfnt(Stream eot)
    {
        ArgumentNullException.ThrowIfNull(eot);

        using var buffer = new MemoryStream();
        eot.CopyTo(buffer);
        var data = buffer.GetBuffer().AsSpan(0, (int)buffer.Length);

        if (data.Length < 82)
        {
            throw new FontFormatException("the file is too short to be an EOT font");
        }

        var eotSize = BinaryPrimitives.ReadUInt32LittleEndian(data);
        var fontDataSize = BinaryPrimitives.ReadUInt32LittleEndian(data[4..]);
        var version = BinaryPrimitives.ReadUInt32LittleEndian(data[8..]);
        var flags = BinaryPrimitives.ReadUInt32LittleEndian(data[12..]);

        if (BinaryPrimitives.ReadUInt16LittleEndian(data[34..]) != Magic)
        {
            throw new FontFormatException("this is not an EOT font (the magic number is wrong)");
        }

        if ((flags & CompressedFlag) != 0)
        {
            throw new FontFormatException(CompressedNotSupported);
        }

        if (fontDataSize == 0 || fontDataSize > (uint)data.Length)
        {
            throw new FontFormatException(
                $"the EOT header says the font is {fontDataSize} bytes, which the file cannot hold");
        }

        Limits.CheckDecodedFontSize(fontDataSize);

        var fontStart = FindFontStart(data, version, eotSize, fontDataSize);

        if (fontDataSize > (uint)(data.Length - fontStart))
        {
            throw new FontFormatException("the EOT font data runs past the end of the file");
        }

        var sfnt = data.Slice(fontStart, (int)fontDataSize).ToArray();

        if ((flags & XorMaskedFlag) != 0)
        {
            // The whole point of the mask was to stop a browser-downloaded font
            // being double-clicked and installed. It is one byte, not security.
            for (var i = 0; i < sfnt.Length; i++)
            {
                sfnt[i] ^= 0x50;
            }
        }

        // Rebuilt rather than handed back as-is, so the checksums and the
        // directory are known-good whatever the header said.
        var (flavor, tables) = Sfnt.ReadTablesAt(sfnt, 0);

        return Sfnt.Build(flavor, tables);
    }

    /// <summary>
    /// Works out where the font data starts by walking the variable-length part
    /// of the header.
    /// </summary>
    /// <remarks>
    /// EOTSize minus FontDataSize gives the answer directly when the header is
    /// well formed, and that is tried first. Walking the strings is the
    /// fallback, because files written by old tools get EOTSize wrong often
    /// enough to matter, and because the result can be checked: whatever offset
    /// is chosen, the bytes there have to start with a recognisable sfnt
    /// signature.
    /// </remarks>
    private static int FindFontStart(ReadOnlySpan<byte> data, uint version, uint eotSize, uint fontDataSize)
    {
        if (eotSize >= fontDataSize && eotSize <= (uint)data.Length)
        {
            var implied = (int)(eotSize - fontDataSize);

            if (implied >= 82 && LooksLikeFont(data, implied, fontDataSize))
            {
                return implied;
            }
        }

        var at = 82;

        // FamilyName, StyleName, VersionName, FullName: each a UInt16 byte count
        // then that many bytes of UTF-16, then a UInt16 padding field.
        for (var i = 0; i < 4 && !PastEnd(data, at, 2); i++)
        {
            at = SkipString(data, at);
        }

        if (version >= 0x00020001)
        {
            // RootString, and before it a padding field and a UInt16 count.
            if (!PastEnd(data, at, 2))
            {
                at = SkipString(data, at);
            }

            // RootStringCheckSum and EUDCCodePage, then one more string.
            at += 8;

            if (!PastEnd(data, at, 2))
            {
                at = SkipString(data, at);
            }
        }

        if (at >= 82 && LooksLikeFont(data, at, fontDataSize))
        {
            return at;
        }

        // Last resort: the font has to be at the end of the file.
        var fromEnd = data.Length - (int)fontDataSize;

        if (fromEnd >= 82 && LooksLikeFont(data, fromEnd, fontDataSize))
        {
            return fromEnd;
        }

        throw new FontFormatException(
            "the EOT header does not say where the font data starts, and it could not be found");
    }

    private static int SkipString(ReadOnlySpan<byte> data, int at)
    {
        var length = BinaryPrimitives.ReadUInt16LittleEndian(data[at..]);
        at += 2 + length;

        // The padding field between one string and the next.
        return at + 2;
    }

    private static bool PastEnd(ReadOnlySpan<byte> data, int at, int need) =>
        at < 0 || at > data.Length - need;

    /// <summary>
    /// Whether an sfnt signature is at this offset - masked or not, since the
    /// mask is applied after the offset is chosen.
    /// </summary>
    private static bool LooksLikeFont(ReadOnlySpan<byte> data, int at, uint fontDataSize)
    {
        if (at < 0 || at > data.Length - 4 || fontDataSize > (uint)(data.Length - at))
        {
            return false;
        }

        var tag = BinaryPrimitives.ReadUInt32BigEndian(data[at..]);

        if (IsFontTag(tag))
        {
            return true;
        }

        // The same four bytes with the 0x50 mask on.
        Span<byte> unmasked = stackalloc byte[4];
        for (var i = 0; i < 4; i++)
        {
            unmasked[i] = (byte)(data[at + i] ^ 0x50);
        }

        return IsFontTag(BinaryPrimitives.ReadUInt32BigEndian(unmasked));
    }

    private static bool IsFontTag(uint tag) => tag is
        Sfnt.TrueTypeFlavor or Sfnt.CffFlavor or Sfnt.CollectionTag or 0x74727565 or 0x00020000;
}
