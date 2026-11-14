using System.Windows.Xps.Packaging;
using ScratchpadLite.Core.Formats.Docx;
using Xunit;

namespace ScratchpadLite.App.Tests;

/// <summary>
/// Holds this project's font obfuscation against Microsoft's own
/// (Section 7.4).
/// </summary>
/// <remarks>
/// <para>ECMA-376 says to "reverse the order of the bytes in the GUID (i.e.
/// Big Endian ordering)", which can be read three ways, and the
/// implementations published on the web split between two of them. Reading
/// the sentence harder was not going to settle it, so it was settled by
/// measurement: <c>XpsFont.ObfuscateFontData</c> is Microsoft's
/// implementation of the same obfuscation for XPS, it ships in
/// ReachFramework with the Windows Desktop runtime, and it is the thing
/// Word's own reader has to agree with.</para>
///
/// <para>This test lives in the App suite because Core targets plain
/// net10.0 and cannot reference WPF - which is the rule that makes the
/// format code testable without a window, and worth keeping even when it
/// costs a test its natural home. Core's own suite checks the key bytes
/// against the answer this test established, so a change breaks something
/// in both places.</para>
///
/// <para>If this ever fails after a Windows update, the thing to doubt is
/// this project's implementation, not Microsoft's.</para>
/// </remarks>
public class FontObfuscationTests
{
    private static byte[] Sample(int length = 64)
    {
        var bytes = new byte[length];

        for (var i = 0; i < length; i++)
        {
            bytes[i] = (byte)((i * 37) % 253);
        }

        return bytes;
    }

    [Theory]
    [InlineData("AABBCCDD-EEFF-0011-2233-445566778899")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF")]
    [InlineData("12345678-9ABC-DEF0-1234-56789ABCDEF0")]
    public void ThisProjectScramblesAFontExactlyAsMicrosoftDoes(string guid)
    {
        var key = Guid.Parse(guid);

        var theirs = Sample();
        XpsFont.ObfuscateFontData(theirs, key);

        var ours = DocxFontEmbedder.Obfuscate(Sample(), key);

        Assert.Equal(theirs, ours);
    }

    [Fact]
    public void AFileTooShortToBeAFontIsWhereTheTwoPartCompany()
    {
        // Microsoft's version walks off the end of anything shorter than the
        // thirty-two bytes it scrambles - it is written for fonts that are
        // fonts. This one is also used on bytes that arrived inside somebody
        // else's document (Section 7.5), where "it cannot be that short" is
        // not a thing that can be assumed, so it stops at the end of the
        // array and lets the reader's own check refuse the file.
        var key = Guid.NewGuid();

        Assert.Throws<IndexOutOfRangeException>(() => XpsFont.ObfuscateFontData(Sample(20), key));

        var ours = DocxFontEmbedder.Obfuscate(Sample(20), key);

        Assert.Equal(20, ours.Length);
        Assert.Equal(Sample(20), DocxFontEmbedder.Deobfuscate(ours, key));
    }

    [Fact]
    public void MicrosoftsOwnScramblingIsUndoneByThisProjects()
    {
        // The direction that matters for reading someone else's document.
        var key = Guid.NewGuid();
        var theirs = Sample(128);

        XpsFont.ObfuscateFontData(theirs, key);

        Assert.Equal(Sample(128), DocxFontEmbedder.Deobfuscate(theirs, key));
    }
}
