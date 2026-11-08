namespace ScratchpadLite.Core.Fonts.Type1;

/// <summary>
/// Type 1 charstrings to Type 2 charstrings (Section 6.6.4). Flex and hint
/// replacement subroutine calls are flattened, seac composites are expanded
/// into components, and sbw and hsbw side bearings become the Type 2
/// nominal width. Hinting is not carried over, which is the known
/// limitation in Section 13.
/// </summary>
/// <remarks>Phase 3.</remarks>
public static class CharStringConverter
{
    public static byte[] Convert(byte[] type1CharString, Type1Parser.Font font) =>
        throw new NotImplementedException("Phase 3");
}
