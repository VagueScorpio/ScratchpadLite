namespace ScratchpadLite.Core.Formats.Docx;

/// <summary>
/// Optional font embedding for DOCX (Section 7.4). Embeds a face only when
/// its embedding rights allow it, and obfuscates the bytes the way
/// ECMA-376 Part 1 section 17.8.1 describes: the first thirty-two bytes are
/// XORed with the font key GUID bytes. Reading reverses it. Fonts converted
/// from Type 1 are never embedded.
/// </summary>
/// <remarks>Phase 10.</remarks>
internal static class DocxFontEmbedder
{
    internal static byte[] Obfuscate(byte[] fontBytes, Guid fontKey) =>
        throw new NotImplementedException("Phase 10");

    internal static byte[] Deobfuscate(byte[] fontBytes, Guid fontKey) =>
        throw new NotImplementedException("Phase 10");
}
