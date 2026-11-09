using System.Windows;
using Microsoft.Win32;
using ScratchpadLite.Core.Platform;

namespace ScratchpadLite.App.UI.Themes;

/// <summary>
/// Light, dark and follow-Windows themes (Section 6.16).
/// </summary>
/// <remarks>
/// Switching swaps one merged dictionary, so the cost at startup is loading the
/// single theme in use and nothing more (Section 2.1).
/// <para>
/// Hand-made dictionaries rather than WPF's Fluent theme
/// (<c>Application.ThemeMode</c>): in the .NET 10 SDK that property is still
/// marked experimental and using it needs the WPF0001 diagnostic suppressed.
/// This project builds with warnings as errors, and suppressing a
/// "this API may change" warning in order to theme an app is a poor trade when
/// two resource dictionaries do the job. Worth revisiting when it ships
/// unmarked.
/// </para>
/// </remarks>
internal static class ThemeManager
{
    private const string PersonalizeKey =
        @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    private const string AppsUseLightTheme = "AppsUseLightTheme";

    /// <summary>
    /// Index 1 of the application's merged dictionaries: App.xaml loads Shared
    /// at index 0 and a theme at index 1, and this replaces index 1 in place.
    /// </summary>
    private const int ThemeSlot = 1;

    private static ThemeChoice _choice = ThemeChoice.FollowWindows;
    private static bool _listening;

    /// <summary>
    /// The pack URI of one of the two theme dictionaries.
    /// </summary>
    /// <remarks>
    /// Spelled out in full, with this assembly named, rather than as the
    /// relative "UI/Themes/Light.xaml" it is tempting to write. A relative
    /// pack URI resolves against the <em>entry</em> assembly, which is this one
    /// only when the .exe is what started the process. Under the test runner
    /// the entry assembly is the runner, and a relative URI then fails with
    /// "Cannot locate resource" - so the form that works everywhere is the one
    /// that names the component.
    /// </remarks>
    internal static Uri DictionaryUri(bool dark) =>
        new($"pack://application:,,,/{AssemblyName};component/UI/Themes/{(dark ? "Dark" : "Light")}.xaml");

    private static readonly string AssemblyName =
        typeof(ThemeManager).Assembly.GetName().Name!;

    /// <summary>Raised after a theme is applied, so the window can match its title bar.</summary>
    internal static event EventHandler<bool>? ThemeChanged;

    /// <summary>True when the theme now showing is the dark one.</summary>
    internal static bool IsDark { get; private set; }

    internal static void Apply(ThemeChoice choice)
    {
        _choice = choice;

        var dark = choice switch
        {
            ThemeChoice.Dark => true,
            ThemeChoice.Light => false,
            _ => WindowsPrefersDark(),
        };

        SwapDictionary(dark);

        // Only listen while actually following Windows: a registry watcher that
        // can never change anything is pure overhead.
        if (choice == ThemeChoice.FollowWindows)
        {
            StartListening();
        }
        else
        {
            StopListening();
        }
    }

    /// <summary>
    /// True when Windows is set to a dark app theme. A missing or unreadable
    /// value means light, which is the Windows default.
    /// </summary>
    internal static bool WindowsPrefersDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            return key?.GetValue(AppsUseLightTheme) is int value && value == 0;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void SwapDictionary(bool dark)
    {
        if (Application.Current is not { } app)
        {
            return;
        }

        var dictionaries = app.Resources.MergedDictionaries;
        var replacement = new ResourceDictionary { Source = DictionaryUri(dark) };

        if (dictionaries.Count > ThemeSlot)
        {
            dictionaries[ThemeSlot] = replacement;
        }
        else
        {
            dictionaries.Add(replacement);
        }

        IsDark = dark;
        Log.Debug($"theme applied: {(dark ? "dark" : "light")}");
        ThemeChanged?.Invoke(null, dark);
    }

    private static void StartListening()
    {
        if (_listening)
        {
            return;
        }

        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        _listening = true;
    }

    private static void StopListening()
    {
        if (!_listening)
        {
            return;
        }

        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        _listening = false;
    }

    private static void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category != UserPreferenceCategory.General || _choice != ThemeChoice.FollowWindows)
        {
            return;
        }

        var dark = WindowsPrefersDark();
        if (dark != IsDark)
        {
            SwapDictionary(dark);
        }
    }
}
