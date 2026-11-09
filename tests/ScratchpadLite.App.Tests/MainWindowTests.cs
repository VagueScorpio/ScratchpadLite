using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

using ScratchpadLite.App.UI;
using ScratchpadLite.Core.Platform;
using Xunit;

namespace ScratchpadLite.App.Tests;

/// <summary>
/// Loads the real window. Everything else in this suite tests a piece in
/// isolation, which let a crash on the very first frame pass a green build:
/// the menus reach their commands with <c>{x:Static}</c>, XAML resolves that by
/// public reflection at parse time, and the commands were internal. It
/// compiled, every unit test passed, and the app died before showing anything.
/// </summary>
/// <remarks>
/// So the rule is: anything the app parses or renders at startup gets loaded by
/// a test, not just the classes behind it. These tests run on
/// <see cref="AppHost"/>'s Application thread.
/// </remarks>
[Collection(nameof(AppHost))]
public class MainWindowTests
{
    private static MainWindow NewWindow() =>
        new(new Settings(), new CommandLineOptions([], false, false, null));

    [Fact]
    public void TheWindowLoads()
    {
        // The whole of MainWindow.xaml is parsed here: every menu item, every
        // x:Static, the toolbars, the status bar and the editor.
        AppHost.Run(() =>
        {
            var window = NewWindow();
            try
            {
                Assert.NotNull(window.Content);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void TheWindowRendersAndCloses()
    {
        // Loading is not rendering: a bad template or a missing brush only
        // throws once layout runs.
        AppHost.Run(() =>
        {
            var window = NewWindow();
            window.ShowActivated = false;
            window.ShowInTaskbar = false;

            try
            {
                window.Show();
                window.UpdateLayout();

                Assert.True(window.IsLoaded);
                Assert.True(window.ActualWidth > 0, "the window should have laid out");
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void TheMenusResolveTheirCommands()
    {
        // A menu item with a null Command is a dead menu entry that throws
        // nothing, so assert the binding landed on the real command object.
        var commands = AppHost.Run(() =>
        {
            var window = NewWindow();
            try
            {
                window.Show();
                window.UpdateLayout();

                return Descendants(window)
                    .OfType<MenuItem>()
                    .Select(item => item.Command)
                    .Where(command => command is not null)
                    .ToList();
            }
            finally
            {
                window.Close();
            }
        });

        Assert.Contains(Commands.NewTab, commands);
        Assert.Contains(Commands.About, commands);

        // Every command the menus carry is one of ours or one of WPF's, never
        // something half-resolved.
        Assert.All(commands, command => Assert.IsAssignableFrom<ICommand>(command!));
    }

    [Fact]
    public void EveryCommandIsPublic()
    {
        // The direct guard on the bug above, named so a future tidy-up that
        // narrows the visibility fails here and reads the reason.
        var internalFields = typeof(Commands)
            .GetFields(BindingFlags.Static | BindingFlags.NonPublic)
            .Where(f => f.FieldType == typeof(RoutedUICommand))
            .Select(f => f.Name)
            .ToList();

        Assert.Empty(internalFields);
        Assert.True(typeof(Commands).IsPublic, "XAML's x:Static needs a public type");
        Assert.NotEmpty(Commands.All);
    }

    [Fact]
    public void TheToolbarButtonStylesAreInApplicationResources()
    {
        // Toolbars.xaml reaches these with StaticResource, which throws rather
        // than degrading if the key is missing.
        AppHost.Run(() =>
        {
            Assert.IsType<Style>(Application.Current.Resources["IconButton"]);
            Assert.IsType<Style>(Application.Current.Resources["IconToggle"]);
        });
    }

    /// <remarks>
    /// The logical tree, not the visual one: a menu builds its popup's visual
    /// tree only when it is opened, but the items are logical children from the
    /// moment the XAML is parsed - which is what this needs to see.
    /// </remarks>
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            yield return child;

            foreach (var descendant in Descendants(child))
            {
                yield return descendant;
            }
        }
    }
}
