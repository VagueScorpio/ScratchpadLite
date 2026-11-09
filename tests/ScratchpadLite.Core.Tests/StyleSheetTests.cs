using ScratchpadLite.Core.Model;
using Xunit;

namespace ScratchpadLite.Core.Tests;

/// <summary>
/// The style sheet (Section 6.5). These run without WPF, which is the point of
/// keeping the model in Core.
/// </summary>
public class StyleSheetTests
{
    [Fact]
    public void TheBuiltInSheetLoadsFromTheEmbeddedResource()
    {
        // If the EmbeddedResource or its LogicalName in the .csproj is wrong,
        // this is the test that says so rather than the app failing at startup.
        var styles = StyleSheet.Default;

        Assert.All(StyleSheet.StyleNames, name => Assert.True(
            styles.Contains(name), $"the built-in sheet is missing {name}"));
    }

    [Theory]
    [InlineData("Normal", 11, false)]
    [InlineData("Title", 26, false)]
    [InlineData("Subtitle", 15, false)]
    [InlineData("Heading 1", 20, true)]
    [InlineData("Heading 2", 16, true)]
    [InlineData("Heading 3", 14, true)]
    [InlineData("Heading 4", 12, true)]
    [InlineData("Heading 5", 11, true)]
    [InlineData("Heading 6", 11, true)]
    [InlineData("Code", 10, false)]
    public void StylesMatchTheBlueprintTable(string name, double sizePt, bool bold)
    {
        var style = StyleSheet.Default[name];

        Assert.Equal(sizePt, style.SizePt);
        Assert.Equal(bold, style.Bold);
    }

    [Fact]
    public void NormalCarriesTheDefaultFontAndSpacing()
    {
        var normal = StyleSheet.Default["Normal"];

        Assert.Equal("Calibri", normal.Family);
        Assert.Equal(8, normal.SpaceAfterPt);
        Assert.Equal(1.15, normal.LineSpacing);
    }

    [Fact]
    public void QuoteIsIndentedAndItalic()
    {
        var quote = StyleSheet.Default["Quote"];

        Assert.True(quote.Italic);
        Assert.Equal(36, quote.IndentLeftPt);
    }

    [Fact]
    public void CodeUsesAMonospaceFontAndShading()
    {
        var code = StyleSheet.Default["Code"];

        Assert.Equal("Consolas", code.Family);
        Assert.False(string.IsNullOrEmpty(code.Shading));
    }

    [Fact]
    public void HeadingsCarryOutlineLevelsForDocxAndMarkdown()
    {
        for (var level = 1; level <= 6; level++)
        {
            Assert.Equal(level, StyleSheet.Default[$"Heading {level}"].OutlineLevel);
        }

        Assert.Null(StyleSheet.Default["Normal"].OutlineLevel);
    }

    [Fact]
    public void AnUnknownStyleNameFallsBackToNormal()
    {
        // A .docx can carry any style name at all (Section 7.2), so this has to
        // be a fallback rather than an exception.
        Assert.Equal("Normal", StyleSheet.Default["Some Style From A Word Document"].Name);
        Assert.Equal("Normal", StyleSheet.Default[null].Name);
    }

    [Fact]
    public void AllListsTheKnownStylesInBlueprintOrder()
    {
        var names = StyleSheet.Default.All.Select(s => s.Name).Take(StyleSheet.StyleNames.Length);

        Assert.Equal(StyleSheet.StyleNames, names);
    }

    [Fact]
    public void WriteDefaultIfMissing_WritesOnceAndNeverOverwrites()
    {
        var dir = Directory.CreateTempSubdirectory("sp-styles-");
        try
        {
            var path = Path.Combine(dir.FullName, "styles.json");

            StyleSheet.WriteDefaultIfMissing(path);
            Assert.True(File.Exists(path));

            // The user is meant to edit this file, so a later run must not
            // quietly replace their edits.
            File.WriteAllText(path, """{ "styles": [ { "name": "Normal", "sizePt": 99 } ] }""");
            StyleSheet.WriteDefaultIfMissing(path);

            Assert.Equal(99, StyleSheet.LoadOrDefault(path)["Normal"].SizePt);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void LoadOrDefault_WithAMissingFile_UsesTheBuiltInSheet()
    {
        var path = Path.Combine(Path.GetTempPath(), $"no-such-{Guid.NewGuid():N}.json");

        Assert.Equal(11, StyleSheet.LoadOrDefault(path)["Normal"].SizePt);
    }

    [Fact]
    public void LoadOrDefault_WithADamagedFile_UsesTheBuiltInSheetRatherThanThrowing()
    {
        var dir = Directory.CreateTempSubdirectory("sp-styles-bad-");
        try
        {
            var path = Path.Combine(dir.FullName, "styles.json");
            File.WriteAllText(path, "{ not json at all");

            Assert.Equal(11, StyleSheet.LoadOrDefault(path)["Normal"].SizePt);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void LoadOrDefault_WithAUserEditedFile_KeepsTheUsersValues()
    {
        var dir = Directory.CreateTempSubdirectory("sp-styles-edited-");
        try
        {
            var path = Path.Combine(dir.FullName, "styles.json");
            File.WriteAllText(
                path,
                """
                {
                  "styles": [
                    { "name": "Normal", "family": "Georgia", "sizePt": 13, "lineSpacing": 1.5 },
                    { "name": "My Style", "sizePt": 18, "bold": true }
                  ]
                }
                """);

            var styles = StyleSheet.LoadOrDefault(path);

            Assert.Equal("Georgia", styles["Normal"].Family);
            Assert.Equal(13, styles["Normal"].SizePt);
            Assert.True(styles.Contains("My Style"));

            // A style the user invented shows up in the picker and the menu.
            Assert.Contains(styles.All, s => s.Name == "My Style");
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }
}

/// <summary>Unit conversions (Section 6.3 / 6.7).</summary>
public class UnitsTests
{
    [Fact]
    public void PointsAndDipsRoundTrip()
    {
        Assert.Equal(16, Units.PointsToDips(12), 6);
        Assert.Equal(12, Units.DipsToPoints(16), 6);
        Assert.Equal(10.5, Units.DipsToPoints(Units.PointsToDips(10.5)), 6);
    }

    [Fact]
    public void DocxUnitsConvert()
    {
        // Half-points, twips and EMU are what DOCX actually stores.
        Assert.Equal(11, Units.HalfPointsToPoints(22), 6);
        Assert.Equal(22, Units.PointsToHalfPoints(11), 6);
        Assert.Equal(72, Units.TwipsToPoints(1440), 6);
        Assert.Equal(1440, Units.PointsToTwips(72), 6);
        Assert.Equal(72, Units.EmuToPoints(914400), 6);
        Assert.Equal(914400, Units.PointsToEmu(72), 6);
    }
}
