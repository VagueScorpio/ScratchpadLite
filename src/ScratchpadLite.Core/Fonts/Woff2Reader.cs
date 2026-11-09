using System.Buffers.Binary;

namespace ScratchpadLite.Core.Fonts;

/// <summary>
/// A forward-only reader over one of the WOFF 2.0 streams, with the bounds
/// check in one place instead of at every call site.
/// </summary>
/// <remarks>
/// WOFF2 decoding reads seven separate streams in step with each other, and
/// each read has to be checked: the lengths come from the file, so a truncated
/// or hostile font would otherwise read past the end of a buffer
/// (Section 6.6.7). A struct over a span keeps that free of allocation.
/// </remarks>
internal ref struct Woff2Reader
{
    private readonly ReadOnlySpan<byte> _data;
    private readonly string _name;
    private int _position;

    internal Woff2Reader(ReadOnlySpan<byte> data, string name)
    {
        _data = data;
        _name = name;
        _position = 0;
    }

    internal int Position => _position;

    internal int Remaining => _data.Length - _position;

    internal bool AtEnd => _position >= _data.Length;

    internal byte ReadByte()
    {
        Need(1);
        return _data[_position++];
    }

    internal ushort ReadUInt16()
    {
        Need(2);
        var value = BinaryPrimitives.ReadUInt16BigEndian(_data[_position..]);
        _position += 2;
        return value;
    }

    internal short ReadInt16() => (short)ReadUInt16();

    internal uint ReadUInt32()
    {
        Need(4);
        var value = BinaryPrimitives.ReadUInt32BigEndian(_data[_position..]);
        _position += 4;
        return value;
    }

    internal ReadOnlySpan<byte> ReadBytes(int count)
    {
        Need(count);
        var slice = _data.Slice(_position, count);
        _position += count;
        return slice;
    }

    /// <summary>
    /// A window on bytes already read, for the one case that needs it: a
    /// composite glyph's components are parsed to find where they end and then
    /// copied through unchanged.
    /// </summary>
    internal ReadOnlySpan<byte> Slice(int start, int length)
    {
        if (start < 0 || length < 0 || start + length > _data.Length)
        {
            throw new FontFormatException(
                $"{_name}: a slice of {length} bytes at {start} is outside the stream");
        }

        return _data.Slice(start, length);
    }

    /// <summary>
    /// The variable-length integer the table directory uses: seven bits per
    /// byte, high bit meaning "another byte follows".
    /// </summary>
    /// <remarks>
    /// The two rejections matter. A leading 0x80 encodes a value with a
    /// redundant zero byte, and the spec forbids it so that one number has
    /// exactly one encoding; more than five bytes cannot fit in 32 bits. Both
    /// are how a crafted file gets a decoder to disagree with itself about a
    /// length.
    /// </remarks>
    internal uint ReadUIntBase128()
    {
        uint result = 0;

        for (var i = 0; i < 5; i++)
        {
            var b = ReadByte();

            if (i == 0 && b == 0x80)
            {
                throw new FontFormatException($"{_name}: a UIntBase128 with a leading zero byte");
            }

            if ((result & 0xFE000000) != 0)
            {
                throw new FontFormatException($"{_name}: a UIntBase128 too large for 32 bits");
            }

            result = (result << 7) | (uint)(b & 0x7F);

            if ((b & 0x80) == 0)
            {
                return result;
            }
        }

        throw new FontFormatException($"{_name}: a UIntBase128 longer than five bytes");
    }

    /// <summary>
    /// The other variable-length integer, used for point counts and
    /// instruction lengths: one byte for values under 253, and three escape
    /// codes for the rest.
    /// </summary>
    internal ushort Read255UInt16()
    {
        // Easy to get the wrong way round, and it only shows up on glyphs with
        // 253 points or more, so most fonts decode either way: 255 adds 253,
        // and 254 adds twice that. Swapping them was tried and three more test
        // fonts failed, which is how the pair above is known to be right.
        const byte wordCode = 253;
        const byte oneMoreByteCode1 = 255;
        const byte oneMoreByteCode2 = 254;
        const int lowestUCode = 253;

        var code = ReadByte();

        return code switch
        {
            wordCode => ReadUInt16(),
            oneMoreByteCode1 => (ushort)(ReadByte() + lowestUCode),
            oneMoreByteCode2 => (ushort)(ReadByte() + (lowestUCode * 2)),
            _ => code,
        };
    }

    private void Need(int count)
    {
        if (count < 0 || count > _data.Length - _position)
        {
            throw new FontFormatException(
                $"{_name}: a read of {count} bytes at {_position} runs past the end of the "
                + $"{_data.Length}-byte stream");
        }
    }
}
