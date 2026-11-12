using System.Buffers.Binary;
using ScratchpadLite.Core.Fonts;
using ScratchpadLite.Core.Formats;
using Xunit;

namespace ScratchpadLite.Core.Tests;

/// <summary>
/// What <see cref="EotDecoder"/> does with a file that is not the font it
/// claims to be (Section 10.1: "corrupt files throw cleanly").
/// </summary>
/// <remarks>
/// <para>The EOT round trip is tested elsewhere, against real .eot files
/// dropped into tests/Fixtures/Fonts, and those tests step aside when the
/// folder is empty. The refusals need no fixture at all: every one of them
/// is decided by the eighty-two byte header, which a test can write out in
/// full. So these run everywhere, including on a clean clone, which is where
/// a font that reads past the end of a buffer would otherwise go
/// unnoticed.</para>
///
/// <para>Why it matters more than most input validation: an .eot is a file
/// from a web page. Section 7.5 treats an opened document as untrusted, and
/// a downloaded font is the same thing.</para>
/// </remarks>
public class EotDecoderTests
{
    /// <summary>
    /// The smallest header the decoder will look at: 82 bytes, with the magic
    /// number in the right place and a size that matches the data.
    /// </summary>
    private static byte[] Header(uint fontDataSize = 16, uint flags = 0, int total = 98)
    {
        var data = new byte[total];

        BinaryPrimitives.WriteUInt32LittleEndian(data, (uint)total);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), fontDataSize);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(8), 0x00020001);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(12), flags);
        // 0x504C, "LP" little-endian, at offset 34: the EOT magic number.
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(34), 0x504C);

        return data;
    }

    private static FontFormatException Refuses(byte[] data)
    {
        using var stream = new MemoryStream(data);

        return Assert.Throws<FontFormatException>(() => EotDecoder.ToSfnt(stream));
    }

    [Fact]
    public void AFileTooShortToHoldAHeaderIsRefused()
    {
        var problem = Refuses(new byte[40]);

        Assert.Contains("too short", problem.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AnEmptyFileIsRefused() => Refuses([]);

    [Fact]
    public void AFileWithTheWrongMagicNumberIsRefused()
    {
        var data = Header();
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(34), 0x1234);

        var problem = Refuses(data);

        Assert.Contains("magic number", problem.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ACompressedEotSaysWhatToLookForInstead()
    {
        // Section 13's second known limitation. MicroType Express is a format
        // of its own and unpacking it is out of scope, so the message has to
        // send the user somewhere rather than just failing.
        var problem = Refuses(Header(flags: 0x00000004));

        Assert.Contains("MicroType Express", problem.Message, StringComparison.Ordinal);
        Assert.Contains(".woff", problem.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AHeaderClaimingMoreFontThanTheFileHoldsIsRefused()
    {
        // The one that would read past the end of the buffer.
        var problem = Refuses(Header(fontDataSize: 500_000));

        Assert.Contains("cannot hold", problem.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AHeaderClaimingNoFontAtAllIsRefused() => Refuses(Header(fontDataSize: 0));

    [Fact]
    public void AHeaderThatFitsButHoldsRubbishIsRefusedRatherThanProducingAFont()
    {
        // The header is believable and the bytes where the font should be are
        // not a font. It must come back as a refusal, not as an SFNT built
        // out of nonsense.
        Assert.ThrowsAny<Exception>(() =>
        {
            using var stream = new MemoryStream(Header(fontDataSize: 16));

            return EotDecoder.ToSfnt(stream);
        });
    }

    [Fact]
    public void AHugeClaimIsRefusedBeforeAnythingIsAllocated()
    {
        // Limits.CheckDecodedFontSize is what stops a header claiming four
        // gigabytes from being believed. The file here is 98 bytes.
        var data = Header(fontDataSize: uint.MaxValue);

        using var stream = new MemoryStream(data);

        Assert.ThrowsAny<Exception>(() => EotDecoder.ToSfnt(stream));
    }
}
