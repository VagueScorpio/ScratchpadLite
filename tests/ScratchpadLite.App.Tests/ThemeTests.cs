using System.Windows;
using System.Windows.Media;
using ScratchpadLite.App.UI.Themes;
using ScratchpadLite.Core.Platform;
using Xunit;

namespace ScratchpadLite.App.Tests;

/// <summary>
/// The theme dictionaries (Section 6.16).
/// </summary>
/// <remarks>
/// Light and Dark are written by hand because Application.ThemeMode is still
/// experimental in .NET 10: it raises WPF0001, and this project builds with
/// warnings as errors, so using it would mean suppressing a diagnostic that
/// says the API may change.
/// <para>
/// Two hand-written dictionaries have one failure mode worth a test. A key in
/// one and not the other costs nothing at build time and nothing at startup;
/// it shows up as an unpainted control in one theme only, which is easy to
/// ship and hard to notice.
/// </para>
/// </remarks>
[Collection(nameof(AppHost))]
public class ThemeTests
{
    // Loaded through ThemeManager's own URI, so the test exercises the path the
    // app uses rather than a second spelling of it.
    private static ResourceDictionary Load(bool dark) =>
        new() { Source = ThemeManager.DictionaryUri(dark) };

    [Fact]
    public void LightAndDarkDefineTheSameKeys()
    {
        var (light, dark) = AppHost.Run(() => (Keys(Load(false)), Keys(Load(true))));

        Assert.Empty(light.Except(dark));
        Assert.Empty(dark.Except(light));
        Assert.NotEmpty(light);
    }

    [Fact]
    public void EveryThemedValueIsABrush()
    {
        // A themed key that is not a brush cannot be used by the control styles,
        // which all bind these to Background, Foreground or BorderBrush.
        AppHost.Run(() =>
        {
            foreach (var dark in new[] { false, true })
            {
                var dictionary = Load(dark);

                foreach (var key in Keys(dictionary))
                {
                    Assert.IsAssignableFrom<Brush>(dictionary[key]);
                }
            }
        });
    }

    [Fact]
    public void ApplySwapsTheThemeWithoutLosingTheSharedDictionary()
    {
        AppHost.Run(() =>
        {
            var resources = Application.Current.Resources;

            try
            {
                ThemeManager.Apply(ThemeChoice.Dark);

                Assert.True(ThemeManager.IsDark);
                // Shared.xaml holds the control styles and is never swapped, so
                // it has to survive a theme change.
                Assert.IsType<Style>(resources["IconButton"]);
                var darkBackground = resources["WindowBackground"];

                ThemeManager.Apply(ThemeChoice.Light);

                Assert.False(ThemeManager.IsDark);
                Assert.IsType<Style>(resources["IconButton"]);
                Assert.NotEqual(darkBackground, resources["WindowBackground"]);
            }
            finally
            {
                ThemeManager.Apply(ThemeChoice.Light);
            }
        });
    }

    private static IReadOnlyList<string> Keys(ResourceDictionary dictionary) =>
        dictionary.Keys.OfType<string>().OrderBy(key => key, StringComparer.Ordinal).ToList();
}
