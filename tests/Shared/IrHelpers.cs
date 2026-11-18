using ScratchpadLite.Core.Model;

namespace ScratchpadLite.Tests.Shared;

/// <summary>
/// The document-building helpers both test projects need.
/// </summary>
/// <remarks>
/// <para>
/// These were copy-pasted into eight test files — <c>MinimalPng</c> into seven
/// of them — which meant a fixture could be corrected in one file and stay
/// wrong in six. Compiled into both test projects through a
/// <c>&lt;Compile Include&gt;</c> rather than put in a shared project, because
/// a third project for five helpers would cost a build target and a package
/// restore to save nothing.
/// </para>
/// <para>
/// Only <see cref="ScratchpadLite.Core.Model"/> types appear here, and that is
/// a hard constraint rather than a preference: <c>ScratchpadLite.Core.Tests</c>
/// targets plain <c>net10.0</c> so that the compiler enforces the Section 4.2
/// rule that Core never sees WPF. Anything touching a <c>RichTextBox</c>
/// belongs in the App test project, which is why the editor helpers
/// (<c>InEditor</c>, <c>CellText</c>, <c>NewParagraph</c>) are not here.
/// </para>
/// <para>
/// Consumers write <c>using static ScratchpadLite.Tests.Shared.Ir;</c> so the
/// call sites read exactly as they did when each file had its own copy.
/// </para>
/// </remarks>
internal static class Ir
{
    /// <summary>A document of these blocks, with no metadata and a default page.</summary>
    internal static SpDocument Document(params Block[] blocks) =>
        new(blocks, new Dictionary<string, string>(), new PageSetup(), new HashSet<string>());

    /// <summary>One paragraph of one run.</summary>
    internal static Paragraph Text(string text, CharStyle? style = null, string styleName = "Normal") =>
        new()
        {
            StyleName = styleName,
            Inlines = [new TextRun(text, style ?? new CharStyle())],
        };

    /// <summary>One paragraph of one run, with bold as a flag rather than a style.</summary>
    internal static Paragraph Para(string text, string styleName = "Normal", bool bold = false) => new()
    {
        StyleName = styleName,
        Inlines = [new TextRun(text, new CharStyle { Bold = bold })],
    };

    /// <summary>The first run's text in a cell, or empty.</summary>
    internal static string FirstText(Cell cell) =>
        cell.Blocks.OfType<Paragraph>()
            .SelectMany(p => p.Inlines)
            .OfType<TextRun>()
            .Select(r => r.Text)
            .FirstOrDefault() ?? "";

    /// <summary>
    /// The smallest valid PNG: one red pixel, 69 bytes. Written out by hand so
    /// the tests need no fixture file.
    /// </summary>
    /// <remarks>
    /// Real bytes rather than a stub, because the image paths put them through
    /// WPF's decoder and through each format's own image part — a fake header
    /// would be rejected somewhere and the test would be measuring the
    /// rejection.
    /// <para>
    /// "Red" is checked, not inherited: the seven copies this replaced
    /// disagreed, one calling it red and another saying only "a one-pixel
    /// PNG". Decoded, it is 1×1 Bgr24 with raw bytes <c>00 00 FF</c>.
    /// </para>
    /// </remarks>
    internal static byte[] MinimalPng() =>
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
        0x08, 0x02, 0x00, 0x00, 0x00, 0x90, 0x77, 0x53,
        0xDE, 0x00, 0x00, 0x00, 0x0C, 0x49, 0x44, 0x41,
        0x54, 0x08, 0xD7, 0x63, 0xF8, 0xCF, 0xC0, 0x00,
        0x00, 0x03, 0x01, 0x01, 0x00, 0x18, 0xDD, 0x8D,
        0xB0, 0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E,
        0x44, 0xAE, 0x42, 0x60, 0x82,
    ];
}
