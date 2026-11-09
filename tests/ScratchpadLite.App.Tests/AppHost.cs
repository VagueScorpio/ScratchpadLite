using System.Windows;
using System.Windows.Threading;
using ScratchpadLite.Core.Platform;

namespace ScratchpadLite.App.Tests;

/// <summary>
/// One long-lived STA thread carrying a real <see cref="Application"/>, so a
/// test can load the actual windows in the resource environment they get at
/// run time.
/// </summary>
/// <remarks>
/// Why this exists rather than <c>[StaFact]</c> alone: the toolbars reach their
/// button styles with <c>{StaticResource}</c>, and those styles live in
/// <c>App.xaml</c>'s merged dictionaries. A window built on a bare STA thread
/// with no Application therefore throws on a missing resource key, which says
/// nothing about the code under test. Creating the Application here through
/// <c>InitializeComponent</c> loads the same <c>Shared.xaml</c> and
/// <c>Light.xaml</c> the app loads, and no startup code runs because
/// <c>OnStartup</c> fires from <c>Run()</c>, which is never called.
/// <para>
/// WPF objects belong to the thread that made them, and only one Application
/// may exist per process, so every test that touches the window marshals onto
/// this one thread.
/// </para>
/// </remarks>
internal static class AppHost
{
    private static readonly Lazy<Dispatcher> Thread = new(Start, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>Runs <paramref name="work"/> on the Application's thread.</summary>
    internal static void Run(Action work) => Thread.Value.Invoke(work);

    /// <summary>Runs <paramref name="work"/> on the Application's thread and returns its result.</summary>
    internal static T Run<T>(Func<T> work) => Thread.Value.Invoke(work);

    private static Dispatcher Start()
    {
        var ready = new TaskCompletionSource<Dispatcher>(TaskCreationOptions.RunContinuationsAsynchronously);

        var thread = new System.Threading.Thread(() =>
        {
            try
            {
                // The window writes styles.json on construction, so the data
                // folder has to exist first - exactly the order App.xaml.cs
                // uses (Section 4.3).
                AppPaths.Initialize();

                var app = new App();
                app.InitializeComponent();

                // Without this, the first test that closes its window closes
                // the last window, which shuts the Application down for the
                // whole process - and every later test then fails on a null or
                // shutting-down Application rather than on its own subject.
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;

                ready.SetResult(Dispatcher.CurrentDispatcher);
            }
            catch (Exception ex)
            {
                ready.SetException(ex);
                return;
            }

            Dispatcher.Run();
        })
        {
            IsBackground = true,
            Name = "ScratchpadLite test UI",
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        return ready.Task.GetAwaiter().GetResult();
    }
}
