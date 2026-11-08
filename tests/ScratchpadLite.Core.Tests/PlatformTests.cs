using ScratchpadLite.Core.Platform;
using Xunit;

namespace ScratchpadLite.Core.Tests;

/// <summary>
/// Phase 0 tests: the command line and the settings file. The AppPaths portable
/// and read-only-fallback cases (Section 10.1) need a writable and a read-only
/// temp folder, so they live in <see cref="AppPathsTests"/>.
/// </summary>
public class CommandLineTests
{
    [Fact]
    public void NoArguments_GivesEmptyOptions()
    {
        var options = CommandLineOptions.Parse([]);

        Assert.Empty(options.Files);
        Assert.False(options.MeasureStartup);
        Assert.False(options.Debug);
        Assert.Null(options.FontDirOverride);
    }

    [Fact]
    public void FilePaths_AreKeptInOrder()
    {
        var options = CommandLineOptions.Parse([@"C:\one.docx", @"C:\two.md"]);

        Assert.Equal([@"C:\one.docx", @"C:\two.md"], options.Files);
    }

    [Fact]
    public void Flags_AreRecognised_AndNotTreatedAsFiles()
    {
        var options = CommandLineOptions.Parse(["--measure-startup", "--debug", @"C:\one.docx"]);

        Assert.True(options.MeasureStartup);
        Assert.True(options.Debug);
        Assert.Equal([@"C:\one.docx"], options.Files);
    }

    [Fact]
    public void FontsFlag_TakesTheFollowingValue()
    {
        var options = CommandLineOptions.Parse(["--fonts", @"C:\test-fonts", @"C:\one.docx"]);

        Assert.Equal(@"C:\test-fonts", options.FontDirOverride);
        Assert.Equal([@"C:\one.docx"], options.Files);
    }

    [Fact]
    public void FontsFlag_WithNoValue_IsIgnoredRatherThanCrashing()
    {
        var options = CommandLineOptions.Parse(["--fonts"]);

        Assert.Null(options.FontDirOverride);
        Assert.Empty(options.Files);
    }
}

public class SettingsTests
{
    [Fact]
    public void Defaults_MatchTheBlueprint()
    {
        var settings = new Settings();

        // Section 6.13.2: AutoSave writes the real file, so it starts off.
        Assert.False(settings.AutoSaveEnabled);
        Assert.Equal(ThemeChoice.FollowWindows, settings.Theme);
        Assert.Equal("Calibri", settings.DefaultFontFamily);
        Assert.Equal(11, settings.DefaultFontSizePt);
        Assert.Equal(10, settings.RecentFilesCount);
        Assert.True(settings.PreviewFontNamesInOwnFont);
    }

    [Fact]
    public void SaveThenLoad_KeepsEveryValue()
    {
        var dir = Directory.CreateTempSubdirectory("sp-settings-");
        try
        {
            var path = Path.Combine(dir.FullName, "settings.json");
            var original = new Settings
            {
                Theme = ThemeChoice.Dark,
                AutoSaveEnabled = true,
                AutoSaveIntervalSeconds = 120,
                DefaultFontFamily = "Cambria",
                DefaultFontSizePt = 10.5,
                Zoom = 1.25,
            };

            original.Save(path);
            var loaded = Settings.Load(path);

            Assert.Equal(ThemeChoice.Dark, loaded.Theme);
            Assert.True(loaded.AutoSaveEnabled);
            Assert.Equal(120, loaded.AutoSaveIntervalSeconds);
            Assert.Equal("Cambria", loaded.DefaultFontFamily);
            Assert.Equal(10.5, loaded.DefaultFontSizePt);
            Assert.Equal(1.25, loaded.Zoom);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void MissingFile_GivesDefaults()
    {
        var settings = Settings.Load(Path.Combine(Path.GetTempPath(), $"no-such-{Guid.NewGuid():N}.json"));

        // Records compare their list and dictionary properties by reference
        // (Section 6.3), so the scalar defaults are what this checks.
        Assert.Equal(new Settings().Theme, settings.Theme);
        Assert.Equal(new Settings().DefaultFontSizePt, settings.DefaultFontSizePt);
        Assert.False(settings.AutoSaveEnabled);
    }

    [Fact]
    public void DamagedFile_GivesDefaults_RatherThanThrowing()
    {
        var dir = Directory.CreateTempSubdirectory("sp-settings-bad-");
        try
        {
            var path = Path.Combine(dir.FullName, "settings.json");
            File.WriteAllText(path, "{ this is not json");

            var settings = Settings.Load(path);

            Assert.Equal(ThemeChoice.FollowWindows, settings.Theme);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }
}

/// <summary>
/// Section 10.1: portable path, and the read-only fallback.
/// </summary>
public class AppPathsTests
{
    [Fact]
    public void ProgramDir_IsTheFolderHoldingTheAssembly()
    {
        // In a single-file app Assembly.Location is empty, which is exactly why
        // AppPaths uses AppContext.BaseDirectory (Section 6.1).
        Assert.False(string.IsNullOrEmpty(AppPaths.ProgramDir));
        Assert.True(Directory.Exists(AppPaths.ProgramDir));
    }

    [Fact]
    public void Initialize_CreatesTheDataSubfolders()
    {
        AppPaths.Initialize();

        Assert.True(Directory.Exists(AppPaths.FontCacheDir));
        Assert.True(Directory.Exists(AppPaths.RecoveryDir));
        Assert.True(Directory.Exists(AppPaths.LogDir));
    }

    [Fact]
    public void OverrideFontDir_ResolvesARelativePathAgainstTheProgramFolder()
    {
        AppPaths.OverrideFontDir("test-fonts");

        Assert.Equal(Path.Combine(AppPaths.ProgramDir, "test-fonts"), AppPaths.FontDir.TrimEnd('\\'));
    }
}
