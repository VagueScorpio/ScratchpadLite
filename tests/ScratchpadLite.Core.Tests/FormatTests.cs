using System.Text;
using ScratchpadLite.Core.Formats;
using ScratchpadLite.Core.Model;
using Xunit;
using Xunit.Abstractions;

namespace ScratchpadLite.Core.Tests;

/// <summary>
/// The format registry (Section 7.1).
/// </summary>
public class FormatRegistryTests
{
    [Fact]
    public void BuildingTheOpenFilterLoadsNoFormatPackage()
    {
        // The Section 2.1 rule, as a test. Mentioning a format type anywhere
        // the registry touches while building a filter would make .NET load
        // that assembly, and the Open dialog would stall while the Open XML
        // SDK was loaded and JIT-compiled.
        var before = Loaded();

        var open = FormatRegistry.OpenFilter();
        var save = FormatRegistry.SaveFilter();
        var all = FormatRegistry.All;

        Assert.NotEmpty(open);
        Assert.NotEmpty(save);
        Assert.NotEmpty(all);

        var newlyLoaded = Loaded().Except(before).ToList();

        Assert.DoesNotContain("DocumentFormat.OpenXml", newlyLoaded);
        Assert.DoesNotContain("DocumentFormat.OpenXml.Framework", newlyLoaded);
        Assert.DoesNotContain("Markdig", newlyLoaded);
    }

    [Fact]
    public void TheOpenFilterStartsWithAllSupportedFiles()
    {
        var filter = FormatRegistry.OpenFilter();

        Assert.StartsWith("All supported files|", filter, StringComparison.Ordinal);
        Assert.Contains("*.docx", filter, StringComparison.Ordinal);
        Assert.Contains("*.odt", filter, StringComparison.Ordinal);
        Assert.Contains("*.rtf", filter, StringComparison.Ordinal);
        Assert.Contains("*.md", filter, StringComparison.Ordinal);
        Assert.Contains("*.txt", filter, StringComparison.Ordinal);
        Assert.EndsWith("|All files|*.*", filter, StringComparison.Ordinal);

        // A dialog filter is pairs of name and pattern, so the count of parts
        // is always even.
        Assert.Equal(0, filter.Split('|').Length % 2);
    }

    [Fact]
    public void TheSaveFilterHasOnlyWritableFormats()
    {
        var filter = FormatRegistry.SaveFilter();

        Assert.Equal(0, filter.Split('|').Length % 2);
        Assert.DoesNotContain("All files", filter, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(".docx")]
    [InlineData(".DOCX")]
    [InlineData("docx")]
    [InlineData(".txt")]
    [InlineData(".odt")]
    [InlineData(".md")]
    [InlineData(".markdown")]
    public void AHandlerIsFoundForAnExtensionHoweverItIsSpelled(string extension) =>
        Assert.NotNull(FormatRegistry.For(extension));

    [Fact]
    public void RtfIsListedButHasNoCoreHandler()
    {
        // WPF's RTF converter only speaks FlowDocument, so the handler lives
        // in the App project. The registry still has to know the format exists,
        // for the dialog filters and the save warning (Section 7.1).
        var info = FormatRegistry.InfoFor(".rtf");

        Assert.NotNull(info);
        Assert.True(info!.HandledOnTheFlowDocument);
        Assert.True(info.CanRead);
        Assert.True(info.CanWrite);
        Assert.Null(FormatRegistry.For(".rtf"));

        // And it appears in both dialog filters.
        Assert.Contains("*.rtf", FormatRegistry.OpenFilter(), StringComparison.Ordinal);
        Assert.Contains("*.rtf", FormatRegistry.SaveFilter(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(".xyz")]
    [InlineData(".doc")]
    [InlineData("")]
    public void AnUnregisteredExtensionHasNoHandler(string extension) =>
        Assert.Null(FormatRegistry.For(extension));

    [Fact]
    public void TheOldWordFormatIsRecognisedAndRefused()
    {
        Assert.True(FormatRegistry.IsLegacyWord("report.doc"));
        Assert.True(FormatRegistry.IsLegacyWord(@"C:\somewhere\REPORT.DOC"));
        Assert.False(FormatRegistry.IsLegacyWord("report.docx"));
        Assert.Null(FormatRegistry.For(".doc"));
        Assert.Contains(".docx", FormatRegistry.LegacyWordMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void WhatWouldBeLostNamesOnlyTheThingsTheDocumentActuallyUses()
    {
        var used = Capabilities.CharFormat | Capabilities.Colors;
        var lost = FormatRegistry.WhatWouldBeLost(used, Capabilities.None);

        Assert.Equal(2, lost.Count);
        Assert.Contains("colours and highlighting", lost);

        // Nothing is lost saving to a format that supports what is used.
        Assert.Empty(FormatRegistry.WhatWouldBeLost(used, used));

        // And nothing is reported for a feature the document does not use.
        Assert.DoesNotContain(
            "tables",
            FormatRegistry.WhatWouldBeLost(used, Capabilities.None));
    }

    private static HashSet<string> Loaded() =>
        AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetName().Name ?? "")
            .ToHashSet(StringComparer.Ordinal);
}

/// <summary>
/// Which features a document uses, for the save warning (Section 7.1).
/// </summary>
public class IrFeaturesTests
{
    [Fact]
    public void APlainDocumentUsesNothing()
    {
        var document = new SpDocument(
            [new Paragraph { Inlines = [new TextRun("plain", new CharStyle())] }],
            new Dictionary<string, string>(),
            new PageSetup(),
            new HashSet<string>());

        Assert.Equal(Capabilities.None, IrFeatures.Used(document));
    }

    [Fact]
    public void EachFeatureIsNoticed()
    {
        var document = new SpDocument(
            [
                new Paragraph
                {
                    StyleName = "Heading 1",
                    Inlines =
                    [
                        new TextRun("b", new CharStyle { Bold = true }),
                        new TextRun("f", new CharStyle { Family = "Georgia" }),
                        new TextRun("c", new CharStyle { Color = "#FF0000" }),
                        new TextRun("l", new CharStyle { Link = "https://example.com" }),
                        new ImageRun([1, 2, 3], "image/png", 10, 10),
                    ],
                    List = new ListInfo(ListKind.Bullet, 0),
                },
                new Table([[new Cell([new Paragraph()])]], null),
            ],
            new Dictionary<string, string>(),
            new PageSetup("A4"),
            new HashSet<string>());

        var used = IrFeatures.Used(document);

        Assert.True(used.HasFlag(Capabilities.CharFormat));
        Assert.True(used.HasFlag(Capabilities.Fonts));
        Assert.True(used.HasFlag(Capabilities.Colors));
        Assert.True(used.HasFlag(Capabilities.Headings));
        Assert.True(used.HasFlag(Capabilities.Lists));
        Assert.True(used.HasFlag(Capabilities.Tables));
        Assert.True(used.HasFlag(Capabilities.Images));
        Assert.True(used.HasFlag(Capabilities.Links));
        Assert.True(used.HasFlag(Capabilities.PageSetup));
    }
}

/// <summary>
/// Plain text (Section 7.2).
/// </summary>
public class TxtFormatTests
{
    private static readonly TxtFormat Format = new();

    private static SpDocument Read(byte[] bytes, out IReadOnlyList<string> warnings)
    {
        using var input = new MemoryStream(bytes);
        var result = Format.Read(input, "test.txt", CancellationToken.None);

        warnings = result.Warnings;
        return result.Document;
    }

    private static string Write(SpDocument document)
    {
        using var output = new MemoryStream();
        Format.Write(document, output, "test.txt", CancellationToken.None);

        return Encoding.UTF8.GetString(output.ToArray());
    }

    [Fact]
    public void OneLineBecomesOneParagraph()
    {
        var document = Read("first\r\nsecond\r\nthird"u8.ToArray(), out _);

        Assert.Equal(3, document.Blocks.Count);
        Assert.Equal("second", ((TextRun)((Paragraph)document.Blocks[1]).Inlines[0]).Text);
    }

    [Theory]
    [InlineData("a\r\nb", "\r\n")]
    [InlineData("a\nb", "\n")]
    [InlineData("a\rb", "\r")]
    [InlineData("no endings at all", "\r\n")]
    public void TheLineEndingIsDetected(string text, string expected)
    {
        var document = Read(Encoding.UTF8.GetBytes(text), out _);

        Assert.Equal(expected, document.Meta[TxtFormat.LineEndingKey]);
    }

    [Fact]
    public void AFilesOwnLineEndingsAreKeptOnSave()
    {
        // Rewriting every line of a file because it was opened and saved makes
        // a mess of someone else's diff.
        var document = Read("a\nb\nc"u8.ToArray(), out _);

        Assert.Equal("a\nb\nc", Write(document));
    }

    [Fact]
    public void AUtf8BomIsRecognisedAndNotTreatedAsText()
    {
        var bytes = new List<byte> { 0xEF, 0xBB, 0xBF };
        bytes.AddRange("hello"u8);

        var document = Read(bytes.ToArray(), out _);

        Assert.Equal("hello", ((TextRun)((Paragraph)document.Blocks[0]).Inlines[0]).Text);
    }

    [Fact]
    public void Utf8WithoutABomIsReadAsUtf8()
    {
        var document = Read(Encoding.UTF8.GetBytes("caf\u00e9 na\u00efve \u4e2d\u6587"), out var warnings);

        Assert.Equal(
            "caf\u00e9 na\u00efve \u4e2d\u6587",
            ((TextRun)((Paragraph)document.Blocks[0]).Inlines[0]).Text);

        Assert.Empty(warnings);
    }

    [Fact]
    public void SomethingThatIsNotUtf8IsReadAsWindows1252AndSaidSo()
    {
        // 0xE9 alone is not valid UTF-8, and is e-acute in Windows-1252. This
        // is the case the whole encoding guess exists for.
        var bytes = new byte[] { (byte)'c', (byte)'a', (byte)'f', 0xE9 };

        var document = Read(bytes, out var warnings);

        Assert.Equal("caf\u00e9", ((TextRun)((Paragraph)document.Blocks[0]).Inlines[0]).Text);
        Assert.Single(warnings);
        Assert.Contains("Windows-1252", warnings[0], StringComparison.Ordinal);
    }

    [Fact]
    public void AFileEndingInANewlineDoesNotGainAParagraph()
    {
        var document = Read("one\r\ntwo\r\n"u8.ToArray(), out _);

        Assert.Equal(2, document.Blocks.Count);
    }

    [Fact]
    public void ListsGetAMarkerBecausePlainTextHasNoOtherWay()
    {
        var document = new SpDocument(
            [
                Para("first") with { List = new ListInfo(ListKind.Bullet, 0) },
                Para("one") with { List = new ListInfo(ListKind.Number, 0) },
                Para("two") with { List = new ListInfo(ListKind.Number, 0) },
                Para("nested") with { List = new ListInfo(ListKind.Number, 1) },
            ],
            new Dictionary<string, string>(),
            new PageSetup(),
            new HashSet<string>());

        var text = Write(document);

        Assert.Contains("\u2022 first", text, StringComparison.Ordinal);
        Assert.Contains("1. one", text, StringComparison.Ordinal);
        Assert.Contains("2. two", text, StringComparison.Ordinal);
        Assert.Contains("  1. nested", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ATableBecomesTabSeparatedRows()
    {
        var document = new SpDocument(
            [
                new Table(
                    [
                        [new Cell([Para("a")]), new Cell([Para("b")])],
                        [new Cell([Para("c")]), new Cell([Para("d")])],
                    ],
                    null),
            ],
            new Dictionary<string, string>(),
            new PageSetup(),
            new HashSet<string>());

        // Tab-separated, so it pastes into a spreadsheet as columns.
        Assert.Equal("a\tb\r\nc\td", Write(document));
    }

    [Fact]
    public void AnImageBecomesItsAltText()
    {
        var document = new SpDocument(
            [new Paragraph { Inlines = [new ImageRun([1], "image/png", 10, 10, "a chart")] }],
            new Dictionary<string, string>(),
            new PageSetup(),
            new HashSet<string>());

        Assert.Equal("[a chart]", Write(document));
    }

    [Fact]
    public void PreservedAllCapsIsWrittenAsCapitals()
    {
        // Plain text has no formatting, so the only way to carry "this was all
        // caps" is to write it in capitals.
        var document = new SpDocument(
            [
                new Paragraph
                {
                    Inlines = [new TextRun("shouting", new CharStyle { AllCapsPreserved = true })],
                },
            ],
            new Dictionary<string, string>(),
            new PageSetup(),
            new HashSet<string>());

        Assert.Equal("SHOUTING", Write(document));
    }

    [Fact]
    public void TextSurvivesARoundTrip()
    {
        var original = "First line\r\nSecond line\r\n\r\nFourth after a blank";

        var document = Read(Encoding.UTF8.GetBytes(original), out _);

        Assert.Equal(original, Write(document));
    }

    private static Paragraph Para(string text) => new()
    {
        Inlines = [new TextRun(text, new CharStyle())],
    };
}
