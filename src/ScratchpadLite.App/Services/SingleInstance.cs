using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using ScratchpadLite.Core.Platform;

namespace ScratchpadLite.App.Services;

/// <summary>
/// One running copy owns the window (Section 6.15). A second launch finds
/// the first through a named mutex, sends it the file paths over a named
/// pipe, and exits.
///
/// This is the very first thing that happens at startup (Section 4.3 step
/// 1), so it has to be cheap: a mutex and, only when another copy exists, a
/// short pipe write.
/// </summary>
/// <remarks>
/// <para>
/// Everything here fails open. A mutex that cannot be created, a pipe that
/// cannot be reached, an identity that cannot be read: each of them ends with
/// this copy showing its own window. The failure of a convenience must never
/// be an application that starts and silently exits, because from the outside
/// that is indistinguishable from a program that is broken.
/// </para>
/// <para>
/// Both names carry a hash of the program folder, so a copy on a USB stick and
/// a copy in Documents are separate applications as far as this is concerned -
/// which is what a portable app needs. The pipe name also carries the user's
/// SID and the pipe itself is created with an ACL for that user alone: a named
/// pipe is visible to the whole machine, and without those two things another
/// signed-in user could hand this process a list of files to open.
/// </para>
/// </remarks>
internal static class SingleInstance
{
    private const string Prefix = "ScratchpadLite";

    /// <summary>How long a second copy keeps trying to reach the first.</summary>
    /// <remarks>
    /// Section 6.15. The first copy starts its listener on a background thread
    /// after the first frame, so a second launch a moment later can arrive
    /// before there is anything to connect to. Three seconds is long enough to
    /// cover that and short enough that a user who double-clicked twice does
    /// not sit looking at nothing.
    /// </remarks>
    private static readonly TimeSpan ForwardTimeout = TimeSpan.FromSeconds(3);

    /// <summary>Held for the life of the process by the copy that owns the window.</summary>
    private static Mutex? _mutex;

    private static Thread? _listener;
    private static CancellationTokenSource? _stop;

    /// <summary>
    /// Eight bytes of a hash of the program folder, as hex. Enough to separate
    /// two installations; it is not a secret and is not used as one.
    /// </summary>
    internal static string KeyFor(string programDir)
    {
        var normalized = programDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .ToLowerInvariant();

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)), 0, 8);
    }

    private static readonly Lazy<string> Key = new(() => KeyFor(AppPaths.ProgramDir));

    /// <summary>
    /// <c>Local\</c>, not <c>Global\</c>: the scope is one logon session, so two
    /// users signed in at once each get their own window rather than one of
    /// them being told a copy is already running.
    /// </summary>
    internal static string MutexName => $@"Local\{Prefix}-{Key.Value}";

    /// <summary>
    /// The pipe, per installation and per user. Built on demand, because
    /// reading the current identity costs more than the startup path should
    /// spend on something only a second copy needs.
    /// </summary>
    internal static string PipeName => $"{Prefix}-{Key.Value}-{UserKey.Value}";

    private static readonly Lazy<string> UserKey = new(() =>
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();

            return identity.User?.Value ?? "anonymous";
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
        {
            Log.Warn($"the current user could not be read, so the pipe name is shared ({ex.GetType().Name})");

            return "anonymous";
        }
    });

    /// <summary>True when this process is the first copy and should show a window.</summary>
    internal static bool Claim()
    {
        try
        {
            var mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);

            if (!createdNew)
            {
                // Another copy owns it. Let go of this handle at once rather
                // than at exit: it is of no use to a process that is about to
                // leave, and assigning it to the field would throw away the
                // handle a successful earlier claim is holding.
                mutex.Dispose();

                return false;
            }

            _mutex?.Dispose();
            _mutex = mutex;

            return true;
        }
        catch (Exception ex)
        {
            // An unopenable mutex is a reason to show a window, not a reason to
            // refuse to start.
            Log.Warn($"the single-instance mutex could not be claimed ({ex.GetType().Name}); starting anyway");

            return true;
        }
    }

    /// <summary>Hands the file paths to the running copy, then the caller exits.</summary>
    /// <returns>
    /// True when the running copy took them. False means nothing was delivered,
    /// and the caller has to decide whether to show a window after all.
    /// </returns>
    /// <remarks>
    /// Called with no files too: a second launch with nothing to open is still
    /// a request to bring the first window to the front.
    /// </remarks>
    internal static bool ForwardFiles(IReadOnlyList<string> files)
    {
        ArgumentNullException.ThrowIfNull(files);

        var deadline = DateTime.UtcNow + ForwardTimeout;

        while (true)
        {
            try
            {
                using var client = new NamedPipeClientStream(
                    ".", PipeName, PipeDirection.Out, PipeOptions.None);

                client.Connect(timeout: 250);

                using var writer = new StreamWriter(client, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
                {
                    AutoFlush = true,
                };

                foreach (var file in files)
                {
                    writer.WriteLine(Full(file));
                }

                // A blank line ends the batch, so the reader does not have to
                // wait for the stream to close to know it has everything.
                writer.WriteLine();

                Log.Info($"handed {files.Count} file(s) to the running copy");

                return true;
            }
            catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException)
            {
                if (DateTime.UtcNow >= deadline)
                {
                    Log.Warn($"the running copy could not be reached ({ex.GetType().Name})");

                    return false;
                }
            }
        }
    }

    private static string Full(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException)
        {
            return path;
        }
    }

    /// <summary>Starts the pipe listener. Raised on the UI thread for each delivery.</summary>
    /// <remarks>
    /// On a background thread, so a window that nobody is sending anything to
    /// pays nothing for the ability to receive it (Section 6.15).
    /// </remarks>
    internal static void StartListening(Action<IReadOnlyList<string>> onFilesReceived)
    {
        ArgumentNullException.ThrowIfNull(onFilesReceived);

        if (_listener is not null)
        {
            return;
        }

        _stop = new CancellationTokenSource();

        _listener = new Thread(() => Listen(onFilesReceived, _stop.Token))
        {
            IsBackground = true,
            Name = "ScratchpadLite single instance",
        };

        _listener.Start();
    }

    /// <summary>Stops listening and lets go of the mutex.</summary>
    internal static void Release()
    {
        try
        {
            _stop?.Cancel();

            // Connecting to our own pipe is what unblocks WaitForConnection;
            // there is no cancellable overload of it that also works on the
            // synchronous server.
            if (_listener is not null)
            {
                using var nudge = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
                nudge.Connect(100);
            }
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or ObjectDisposedException)
        {
            // Nothing listening any more, which is the state being asked for.
        }

        _mutex?.Dispose();
        _mutex = null;
        _listener = null;
    }

    private static void Listen(Action<IReadOnlyList<string>> onFilesReceived, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                using var server = CreateServer();

                server.WaitForConnection();

                if (token.IsCancellationRequested)
                {
                    return;
                }

                var files = ReadBatch(server);

                // An empty batch is delivered too: it means "come to the
                // front", which is what a second launch with no files on its
                // command line is asking for.
                var dispatcher = System.Windows.Application.Current?.Dispatcher;

                if (dispatcher is null)
                {
                    return;
                }

                dispatcher.BeginInvoke(() => onFilesReceived(files));
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or UnauthorizedAccessException)
            {
                if (token.IsCancellationRequested)
                {
                    return;
                }

                Log.Warn($"the single-instance pipe failed and will be reopened ({ex.GetType().Name})");
            }
            catch (Exception ex)
            {
                // A listener that throws must not take the application with it:
                // this thread is not the UI thread, so an escaping exception
                // ends the process.
                Log.Error("the single-instance listener stopped", ex);

                return;
            }
        }
    }

    /// <summary>
    /// Reads one batch of paths, ending at a blank line or end of stream.
    /// </summary>
    /// <remarks>
    /// What arrives here is data from another process, so it is treated as
    /// such: the count and the length of each line are capped, and the caller
    /// checks that each path is a file that exists before opening it. The pipe
    /// is restricted to this user, which is what makes that enough.
    /// </remarks>
    private static List<string> ReadBatch(Stream stream)
    {
        const int maxFiles = 64;
        const int maxLength = 1024;

        var files = new List<string>();

        using var reader = new StreamReader(
            stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), detectEncodingFromByteOrderMarks: false);

        while (files.Count < maxFiles && reader.ReadLine() is { } line)
        {
            if (line.Length == 0)
            {
                break;
            }

            if (line.Length <= maxLength)
            {
                files.Add(line);
            }
        }

        return files;
    }

    private static NamedPipeServerStream CreateServer()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();

            if (identity.User is { } user)
            {
                var security = new PipeSecurity();

                security.AddAccessRule(
                    new PipeAccessRule(user, PipeAccessRights.FullControl, AccessControlType.Allow));

                return NamedPipeServerStreamAcl.Create(
                    PipeName,
                    PipeDirection.In,
                    maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.None,
                    inBufferSize: 0,
                    outBufferSize: 0,
                    security);
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or PlatformNotSupportedException)
        {
            Log.Warn($"the single-instance pipe could not be restricted to this user ({ex.GetType().Name})");
        }

        // Without an ACL the pipe's name still carries the user's SID, and the
        // reader still only opens files that exist - but say so, because a
        // silently less careful fallback is the kind that is never noticed.
        return new NamedPipeServerStream(PipeName, PipeDirection.In, 1);
    }
}
