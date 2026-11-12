using System.IO;
using ScratchpadLite.App.Services;
using Xunit;

namespace ScratchpadLite.App.Tests;

/// <summary>
/// One running copy owns the window (Section 6.15): the names, the claim, and
/// the hand-off over the pipe.
/// </summary>
/// <remarks>
/// These run against the test runner's own program folder, so the names they
/// use are different from the ones a real Scratchpad Lite uses. That is the
/// point of hashing the folder into the name, and it also means running this
/// suite cannot reach into a copy the user has open.
/// </remarks>
[Collection(nameof(AppHost))]
public class SingleInstanceTests : IDisposable
{
    public void Dispose()
    {
        SingleInstance.Release();

        GC.SuppressFinalize(this);
    }

    // -------------------------------------------------------------- names --

    [Fact]
    public void TheSameFolderAlwaysGivesTheSameKey() =>
        Assert.Equal(
            SingleInstance.KeyFor(@"C:\Portable\Scratchpad"),
            SingleInstance.KeyFor(@"C:\Portable\Scratchpad"));

    [Fact]
    public void TwoFoldersGiveDifferentKeys() =>
        Assert.NotEqual(
            SingleInstance.KeyFor(@"C:\Portable\Scratchpad"),
            SingleInstance.KeyFor(@"D:\Scratchpad"));

    [Fact]
    public void TheKeyIgnoresCaseAndATrailingSlash()
    {
        // Windows paths are case-insensitive and a shortcut may or may not end
        // with a separator. Two launches of the same .exe must not end up
        // thinking they are different installations.
        var plain = SingleInstance.KeyFor(@"C:\Portable\Scratchpad");

        Assert.Equal(plain, SingleInstance.KeyFor(@"c:\portable\SCRATCHPAD"));
        Assert.Equal(plain, SingleInstance.KeyFor(@"C:\Portable\Scratchpad\"));
    }

    [Fact]
    public void TheMutexIsScopedToTheLogonSession() =>
        // Local, not Global: two users signed in at once each get a window.
        Assert.StartsWith(@"Local\ScratchpadLite-", SingleInstance.MutexName);

    [Fact]
    public void ThePipeNameCarriesTheInstallationKey() =>
        Assert.Contains(SingleInstance.KeyFor(AppContext.BaseDirectory), SingleInstance.PipeName);

    [Fact]
    public void ThePipeNameIsNotTheMutexName() =>
        // A named pipe is machine-wide where the mutex is per-session, so the
        // pipe carries the user as well and the two names differ.
        Assert.NotEqual(SingleInstance.MutexName, SingleInstance.PipeName);

    // -------------------------------------------------------------- claim --

    [Fact]
    public void TheFirstClaimSucceedsAndTheSecondDoesNot()
    {
        Assert.True(SingleInstance.Claim());

        // The second call stands in for a second launch. A mutex that already
        // exists is not created anew, whichever process asks.
        Assert.False(SingleInstance.Claim());
    }

    [Fact]
    public void AfterReleaseItCanBeClaimedAgain()
    {
        Assert.True(SingleInstance.Claim());

        SingleInstance.Release();

        Assert.True(SingleInstance.Claim());
    }

    // ----------------------------------------------------------- hand-off --

    [Fact]
    public async Task FilesReachTheRunningCopy()
    {
        var delivered = new TaskCompletionSource<IReadOnlyList<string>>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        // StartListening marshals onto Application.Current.Dispatcher, which is
        // the host's: without an Application there is nowhere to deliver to.
        AppHost.Run(() => SingleInstance.StartListening(files => delivered.TrySetResult(files)));

        Assert.True(SingleInstance.ForwardFiles([@"C:\work\one.docx", @"C:\work\two.md"]));

        // WaitAsync rather than Wait: xUnit refuses a blocking wait in a test,
        // and a timeout here throws with the method name, which is the
        // failure anyone reading it would want.
        var files = await delivered.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(2, files.Count);
        Assert.Equal(@"C:\work\one.docx", files[0]);
        Assert.Equal(@"C:\work\two.md", files[1]);
    }

    [Fact]
    public async Task AnEmptyHandOffStillArrives()
    {
        // A second launch with nothing on its command line is asking the first
        // window to come to the front, so the delivery has to happen even with
        // no files in it.
        var delivered = new TaskCompletionSource<IReadOnlyList<string>>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        AppHost.Run(() => SingleInstance.StartListening(files => delivered.TrySetResult(files)));

        Assert.True(SingleInstance.ForwardFiles([]));

        Assert.Empty(await delivered.Task.WaitAsync(TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public async Task ARelativePathIsMadeAbsoluteBeforeItIsSent()
    {
        // The two copies may have different working directories, so a relative
        // path would mean two different files.
        var delivered = new TaskCompletionSource<IReadOnlyList<string>>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        AppHost.Run(() => SingleInstance.StartListening(files => delivered.TrySetResult(files)));

        Assert.True(SingleInstance.ForwardFiles(["notes.txt"]));

        var sent = Assert.Single(await delivered.Task.WaitAsync(TimeSpan.FromSeconds(10)));

        Assert.True(Path.IsPathFullyQualified(sent));
        Assert.EndsWith("notes.txt", sent);
    }

    [Fact]
    public void WithNobodyListeningTheHandOffGivesUp()
    {
        // No listener started in this test, so this is a second launch finding
        // a mutex but no window - which ends with it showing one of its own
        // rather than exiting silently.
        var clock = System.Diagnostics.Stopwatch.StartNew();

        Assert.False(SingleInstance.ForwardFiles([@"C:\work\nowhere.docx"]));

        Assert.True(
            clock.Elapsed < TimeSpan.FromSeconds(20),
            $"giving up took {clock.Elapsed.TotalSeconds:F1} s; Section 6.15 asks for about three");
    }

    [Fact]
    public void StartListeningTwiceIsHarmless() =>
        AppHost.Run(() =>
        {
            SingleInstance.StartListening(_ => { });
            SingleInstance.StartListening(_ => { });
        });
}
