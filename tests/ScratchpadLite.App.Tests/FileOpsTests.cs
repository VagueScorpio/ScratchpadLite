using System.IO;
using System.Text;
using ScratchpadLite.App.Services;
using Xunit;

namespace ScratchpadLite.App.Tests;

/// <summary>
/// Safe saving (Section 6.14).
/// </summary>
public class FileOpsTests : IDisposable
{
    private readonly DirectoryInfo _folder = Directory.CreateTempSubdirectory("sp-fileops-");

    public void Dispose()
    {
        try
        {
            _folder.Delete(recursive: true);
        }
        catch (IOException)
        {
        }

        GC.SuppressFinalize(this);
    }

    private string Path(string name) => System.IO.Path.Combine(_folder.FullName, name);

    private static void Write(Stream stream, string text) =>
        stream.Write(Encoding.UTF8.GetBytes(text));

    [Fact]
    public void ANewFileIsWritten()
    {
        var path = Path("new.txt");

        FileOps.AtomicWrite(path, s => Write(s, "hello"), keepBak: false);

        Assert.Equal("hello", File.ReadAllText(path));
    }

    [Fact]
    public void AnExistingFileIsReplaced()
    {
        var path = Path("existing.txt");
        File.WriteAllText(path, "old");

        FileOps.AtomicWrite(path, s => Write(s, "new"), keepBak: false);

        Assert.Equal("new", File.ReadAllText(path));
        Assert.False(File.Exists(path + ".bak"));
    }

    [Fact]
    public void TheOldVersionIsKeptWhenAskedFor()
    {
        var path = Path("backed-up.txt");
        File.WriteAllText(path, "old");

        FileOps.AtomicWrite(path, s => Write(s, "new"), keepBak: true);

        Assert.Equal("new", File.ReadAllText(path));
        Assert.Equal("old", File.ReadAllText(path + ".bak"));
    }

    [Fact]
    public void AFailedWriteLeavesTheOriginalAloneAndNoRubbishBehind()
    {
        // The whole point of writing to a temporary file first. A save that
        // throws part way through must not damage what was already there.
        var path = Path("safe.txt");
        File.WriteAllText(path, "original");

        Assert.Throws<FileOps.SaveException>(() => FileOps.AtomicWrite(
            path,
            s =>
            {
                Write(s, "half a document");
                throw new InvalidOperationException("the handler gave up");
            },
            keepBak: false));

        Assert.Equal("original", File.ReadAllText(path));

        // And no temporary file left in the folder.
        Assert.Equal(["safe.txt"], Directory.GetFiles(_folder.FullName).Select(System.IO.Path.GetFileName));
    }

    [Fact]
    public void TheTemporaryFileIsInTheSameFolderAsTheTarget()
    {
        // Not the system temp folder: a move within one volume is atomic and a
        // move across volumes is a copy, which can be interrupted.
        var path = Path("same-folder.txt");
        var seen = new List<string>();

        FileOps.AtomicWrite(
            path,
            _ => seen.AddRange(Directory.GetFiles(_folder.FullName)),
            keepBak: false);

        Assert.Single(seen);
        Assert.StartsWith(_folder.FullName, seen[0], StringComparison.Ordinal);
        Assert.EndsWith(".tmp", seen[0], StringComparison.Ordinal);
    }

    [Fact]
    public void AReadOnlyFileGivesAnExplanationRatherThanAnException()
    {
        var path = Path("readonly.txt");
        File.WriteAllText(path, "old");
        File.SetAttributes(path, FileAttributes.ReadOnly);

        try
        {
            var ex = Assert.Throws<FileOps.SaveException>(
                () => FileOps.AtomicWrite(path, s => Write(s, "new"), keepBak: false));

            Assert.Contains("read-only", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("readonly.txt", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            File.SetAttributes(path, FileAttributes.Normal);
        }
    }

    [Fact]
    public void AFileOpenInAnotherProgramSaysSo()
    {
        var path = Path("locked.txt");
        File.WriteAllText(path, "old");

        // FileShare.None is what Word does to a document it has open.
        using var holder = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var ex = Assert.Throws<FileOps.SaveException>(
            () => FileOps.AtomicWrite(path, s => Write(s, "new"), keepBak: false));

        Assert.Contains("another program", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AChangeOnDiskIsNoticed()
    {
        var path = Path("watched.txt");
        File.WriteAllText(path, "as opened");

        var stamp = FileOps.Stamp.Of(path);
        Assert.NotNull(stamp);
        Assert.False(FileOps.ChangedOnDisk(path, stamp));

        // Something else wrote to it.
        File.WriteAllText(path, "changed by someone else");
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(5));

        Assert.True(FileOps.ChangedOnDisk(path, stamp));
    }

    [Fact]
    public void AFileThatDidNotExistWhenOpenedIsNotAChange()
    {
        var path = Path("not-yet.txt");

        Assert.Null(FileOps.Stamp.Of(path));
        Assert.False(FileOps.ChangedOnDisk(path, null));
    }
}

/// <summary>
/// The recent files list (Section 5.2).
/// </summary>
[Collection(nameof(AppHost))]
public class RecentFilesTests
{
    [Fact]
    public void AFileIsRememberedAndMovesToTheFrontWhenUsedAgain()
    {
        Core.Platform.AppPaths.Initialize();

        var first = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"sp-recent-{Guid.NewGuid():N}.txt");
        var second = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"sp-recent-{Guid.NewGuid():N}.txt");

        RecentFiles.Add(first);
        RecentFiles.Add(second);

        var list = RecentFiles.Load(10);

        Assert.Equal(second, list[0], StringComparer.OrdinalIgnoreCase);
        Assert.Contains(first, list, StringComparer.OrdinalIgnoreCase);

        // Using the first again moves it up rather than adding it twice.
        RecentFiles.Add(first);
        var after = RecentFiles.Load(10);

        Assert.Equal(first, after[0], StringComparer.OrdinalIgnoreCase);
        Assert.Single(after, p => string.Equals(p, first, StringComparison.OrdinalIgnoreCase));

        RecentFiles.Remove(first);
        RecentFiles.Remove(second);
    }
}
