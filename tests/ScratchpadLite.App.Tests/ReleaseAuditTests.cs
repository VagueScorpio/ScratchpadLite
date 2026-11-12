using System.IO;
using System.Xml.Linq;
using Xunit;

namespace ScratchpadLite.App.Tests;

/// <summary>
/// The release checks that can be made by a test rather than by eye
/// (Section 10.2, and Section 11.1's project settings).
/// </summary>
/// <remarks>
/// <para>These read the project's own files. Most of what Section 10.2 asks
/// for needs a person and a screen - whether anything looks blurry, whether a
/// printed page has the right margins - but a handful of its items are
/// settings that either are in the build or are not, and those have no
/// business being checked by hand once per release when they can be checked
/// on every run.</para>
///
/// <para>Display scaling is the clearest example. Whether the window looks
/// right at 200% is a question for a person, but whether the manifest claims
/// Per-Monitor V2 is not: without that claim Windows scales the window as a
/// bitmap and every font in it goes soft, which is the "nothing is blurry"
/// item in the checklist failing for one missing line.</para>
/// </remarks>
public class ReleaseAuditTests
{
    private static readonly XNamespace V1 = "urn:schemas-microsoft-com:asm.v1";
    private static readonly XNamespace V3 = "urn:schemas-microsoft-com:asm.v3";
    private static readonly XNamespace Compat = "urn:schemas-microsoft-com:compatibility.v1";
    private static readonly XNamespace Settings2016 = "http://schemas.microsoft.com/SMI/2016/WindowsSettings";
    private static readonly XNamespace Settings2005 = "http://schemas.microsoft.com/SMI/2005/WindowsSettings";
    private static readonly XNamespace Settings2019 = "http://schemas.microsoft.com/SMI/2019/WindowsSettings";

    /// <summary>
    /// The repository root, found by walking up to the solution file. Null if
    /// the tests are running somewhere the source is not, in which case these
    /// tests have nothing to say.
    /// </summary>
    private static string? RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "ScratchpadLite.slnx")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        return null;
    }

    private static XDocument? Manifest()
    {
        if (RepoRoot() is not { } root)
        {
            return null;
        }

        var path = Path.Combine(root, "src", "ScratchpadLite.App", "app.manifest");

        return File.Exists(path) ? XDocument.Load(path) : null;
    }

    private static string? AppProject()
    {
        if (RepoRoot() is not { } root)
        {
            return null;
        }

        var path = Path.Combine(root, "src", "ScratchpadLite.App", "ScratchpadLite.App.csproj");

        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    [Fact]
    public void TheManifestAsksForPerMonitorDpiAwareness()
    {
        if (Manifest() is not { } manifest)
        {
            return;
        }

        var windowsSettings = manifest.Root!
            .Element(V3 + "application")?
            .Element(V3 + "windowsSettings");

        Assert.NotNull(windowsSettings);

        // Both spellings: the 2016 one is what Windows 10 and 11 read, and the
        // 2005 one is the fallback that keeps an older build of Windows from
        // treating the app as unaware and stretching it.
        Assert.Equal("PerMonitorV2", windowsSettings!.Element(Settings2016 + "dpiAwareness")?.Value);
        Assert.Equal("true/pm", windowsSettings.Element(Settings2005 + "dpiAware")?.Value);
    }

    [Fact]
    public void TheManifestAsksForLongPathsAndUtf8()
    {
        if (Manifest() is not { } manifest)
        {
            return;
        }

        var windowsSettings = manifest.Root!
            .Element(V3 + "application")?
            .Element(V3 + "windowsSettings");

        Assert.Equal("true", windowsSettings?.Element(Settings2016 + "longPathAware")?.Value);
        Assert.Equal("UTF-8", windowsSettings?.Element(Settings2019 + "activeCodePage")?.Value);
    }

    [Fact]
    public void TheManifestNeverAsksForElevation()
    {
        if (Manifest() is not { } manifest)
        {
            return;
        }

        var level = manifest.Descendants()
            .FirstOrDefault(e => e.Name.LocalName == "requestedExecutionLevel")?
            .Attribute("level")?.Value;

        // A portable app that asked to be run as administrator would put a
        // prompt in front of every launch and write its data somewhere else.
        Assert.Equal("asInvoker", level);
    }

    [Fact]
    public void TheManifestSaysItSupportsWindows10And11()
    {
        if (Manifest() is not { } manifest)
        {
            return;
        }

        var ids = manifest.Root!
            .Element(Compat + "compatibility")?
            .Element(Compat + "application")?
            .Elements(Compat + "supportedOS")
            .Select(e => e.Attribute("Id")?.Value)
            .ToList();

        Assert.Contains("{8e0f7a12-bfb3-4fe8-b9a5-48fd50a15a9a}", ids!);
    }

    [Fact]
    public void TheManifestIsTheOneTheBuildEmbeds()
    {
        // The checks above are worth nothing if the project does not use this
        // file.
        if (AppProject() is not { } project)
        {
            return;
        }

        Assert.Contains("<ApplicationManifest>app.manifest</ApplicationManifest>", project, StringComparison.Ordinal);
    }

    [Theory]

    // Section 11.1. Each of these was a decision, and each would be easy to
    // lose in a merge without anything failing until somebody tried to run
    // the .exe on a PC with no .NET on it.
    [InlineData("<SelfContained>true</SelfContained>")]
    [InlineData("<PublishSingleFile>true</PublishSingleFile>")]
    [InlineData("<PublishReadyToRun>true</PublishReadyToRun>")]
    [InlineData("<EnableCompressionInSingleFile>false</EnableCompressionInSingleFile>")]
    [InlineData("<PublishTrimmed>false</PublishTrimmed>")]
    [InlineData("<InvariantGlobalization>false</InvariantGlobalization>")]
    [InlineData("<RuntimeIdentifier>win-x64</RuntimeIdentifier>")]
    public void ThePublishSettingsAreStillTheMeasuredOnes(string setting)
    {
        if (AppProject() is not { } project)
        {
            return;
        }

        Assert.Contains(setting, project.Replace(" ", "", StringComparison.Ordinal), StringComparison.Ordinal);
    }

    [Fact]
    public void TheVersionLivesInExactlyOnePlace()
    {
        // Section 0: one place only, Directory.Build.props. A second
        // <Version> anywhere would mean the About box and the zip could
        // disagree.
        if (RepoRoot() is not { } root)
        {
            return;
        }

        var projects = Directory.GetFiles(root, "*.csproj", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(p => File.ReadAllText(p).Contains("<Version>", StringComparison.Ordinal))
            .ToList();

        Assert.Empty(projects);

        var props = File.ReadAllText(Path.Combine(root, "Directory.Build.props"));

        Assert.Contains("<Version>", props, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryDocumentTheReleaseShipsIsThere()
    {
        // Section 14's deliverables, as far as a test can see them: the files
        // build.ps1 copies into the zip.
        if (RepoRoot() is not { } root)
        {
            return;
        }

        foreach (var name in new[]
                 {
                     "README.md", "USER_GUIDE.md", "CHANGELOG.txt",
                     "THIRD_PARTY_LICENSES.txt", "FONT_LICENSES.txt", "QA_CHECKLIST.md",
                 })
        {
            Assert.True(File.Exists(Path.Combine(root, name)), $"{name} is missing");
        }
    }

    [Fact]
    public void TheUserGuideStatesEveryKnownLimitation()
    {
        // Section 13: "state these in USER_GUIDE.md". The list is long enough
        // that one going missing in an edit would not be noticed.
        if (RepoRoot() is not { } root)
        {
            return;
        }

        var guide = File.ReadAllText(Path.Combine(root, "USER_GUIDE.md"));

        foreach (var subject in new[]
                 {
                     ".doc", "bitmap font", "hinting", "background", "Variable font",
                     "until you restart", "embedded", "letter spacing", "lose formatting",
                     "WPF's own converter", "page-layout", "book-length", "SmartScreen",
                 })
        {
            Assert.True(
                guide.Contains(subject, StringComparison.OrdinalIgnoreCase),
                $"USER_GUIDE.md says nothing about \"{subject}\"");
        }
    }
}
