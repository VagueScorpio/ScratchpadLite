namespace ScratchpadLite.Core.Formats.Docx;

/// <summary>
/// Optional font embedding for DOCX (Section 7.4). Embeds a face only when
/// its embedding rights allow it, and obfuscates the bytes the way
/// ECMA-376 Part 1 section 17.8.1 describes: the first thirty-two bytes are
/// XORed with the font key GUID bytes. Reading reverses it. Fonts converted
/// from Type 1 are never embedded.
/// </summary>
/// <remarks>
/// <para>The obfuscation is not encryption and is not meant to be. It stops a
/// font being lifted out of a document by renaming the part, which is what
/// the licence of a commercial font usually asks for, and nothing more.</para>
///
/// <para>The byte order of the key is the whole of the difficulty, and the
/// written sources disagree about it. ECMA-376 says to "reverse the order of
/// the bytes in the GUID (i.e. Big Endian ordering)", which can be read three
/// ways, and the implementations on the web split between two of them. So it
/// was not read off a page: it was measured against Microsoft's own
/// implementation, <c>XpsFont.ObfuscateFontData</c>, which ships in
/// ReachFramework and obfuscates fonts for XPS by the same rule. The key is
/// the GUID's big-endian bytes reversed - for
/// <c>AABBCCDD-EEFF-0011-2233-445566778899</c> that is
/// <c>99 88 77 66 55 44 33 22 11 00 FF EE DD CC BB AA</c> - applied twice,
/// to bytes 0 to 15 and again to bytes 16 to 31. A test in the App suite
/// holds this implementation against that one, because Core cannot reference
/// WPF and an agreement nobody checks is a coincidence waiting to break.</para>
/// </remarks>
internal static class DocxFontEmbedder
{
    /// <summary>How many bytes at the front of the file are scrambled.</summary>
    internal const int ObfuscatedPrefix = 32;

    /// <summary>
    /// The sixteen key bytes for a font key: the GUID's big-endian bytes,
    /// reversed.
    /// </summary>
    internal static byte[] KeyBytes(Guid fontKey)
    {
        var key = fontKey.ToByteArray(bigEndian: true);
        Array.Reverse(key);

        return key;
    }

    /// <summary>
    /// Scrambles a copy of the font's first thirty-two bytes. XOR is its own
    /// inverse, so this and <see cref="Deobfuscate"/> are the same operation;
    /// they are two names because the call sites read better for it.
    /// </summary>
    internal static byte[] Obfuscate(byte[] fontBytes, Guid fontKey)
    {
        ArgumentNullException.ThrowIfNull(fontBytes);

        var copy = (byte[])fontBytes.Clone();
        var key = KeyBytes(fontKey);

        for (var i = 0; i < ObfuscatedPrefix && i < copy.Length; i++)
        {
            copy[i] ^= key[i % key.Length];
        }

        return copy;
    }

    internal static byte[] Deobfuscate(byte[] fontBytes, Guid fontKey) =>
        Obfuscate(fontBytes, fontKey);
}
