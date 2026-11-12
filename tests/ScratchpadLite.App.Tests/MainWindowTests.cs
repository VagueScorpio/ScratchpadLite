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
    /// <summary>
    /// A window that renders and does nothing else.
    /// </summary>
    /// <remarks>
    /// <c>MeasureStartup</c> is on, which is what the hidden measurement flag
    /// is for: it renders one frame and starts none of the work that lives
    /// past it. Without it a shown test window would prepare the font folder,
    /// switch on spell check, claim a crash-recovery folder in the real data
    /// directory and open the single-instance pipe - against the user's own
    /// installation. These tests are about what the window parses and lays
    /// out, so none of that belongs in them.
    /// </remarks>
    private static MainWindow NewWindow() =>
        new(
            new Settings(),
            new CommandLineOptions([], MeasureStartup: true, Debug: false, FontDirOverride: null));

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

    /// <summary>
    /// Every menu item Phase 8 was supposed to switch on is switched on.
    /// </summary>
    /// <remarks>
    /// Items whose phase has not arrived carry <c>IsEnabled="False"</c> in the
    /// XAML, so a feature can be built and still be unreachable: the code
    /// works, the tests pass, and the menu entry stays grey. That is a failure
    /// nothing else here would notice.
    /// </remarks>
    [Theory]
    [InlineData("Page Setup...")]
    [InlineData("Print...")]
    [InlineData("Export as PDF...")]
    [InlineData("Recover Documents...")]
    [InlineData("Preferences...")]
    [InlineData("AutoSave")]
    public void TheMenuItemPhaseEightBuiltIsReachable(string header)
    {
        var enabled = AppHost.Run(() =>
        {
            var window = NewWindow();
            try
            {
                window.Show();
                window.UpdateLayout();

                var item = Descendants(window)
                    .OfType<MenuItem>()
                    .FirstOrDefault(m => (m.Header as string) == header);

                Assert.NotNull(item);

                return item.IsEnabled;
            }
            finally
            {
                window.Close();
            }
        });

        Assert.True(enabled, $"\"{header}\" is still greyed out");
    }

    [Fact]
    public void TheAutoSaveSwitchIsOnTheStatusBarAndSaysWhetherItIsOn()
    {
        // Section 6.13.2 puts it in two places, and the status bar one is the
        // one somebody glances at to find out whether their file is being
        // written. A tick with no word beside it does not answer that.
        var label = AppHost.Run(() =>
        {
            var window = NewWindow();
            try
            {
                window.Show();
                window.UpdateLayout();

                var box = Descendants(window)
                    .OfType<CheckBox>()
                    .FirstOrDefault(c => (c.Content as string)?.StartsWith("AutoSave") == true);

                Assert.NotNull(box);
                Assert.True(box.IsEnabled, "the AutoSave switch is still disabled");
                Assert.False(box.IsChecked, "AutoSave is off by default");

                return (string)box.Content;
            }
            finally
            {
                window.Close();
            }
        });

        Assert.Equal("AutoSave: Off", label);
    }

    [Fact]
    public void TheToolbarsFileButtonsAreWiredToCommands()
    {
        // These four sat in the XAML with no Command at all from Phase 1: they
        // looked like buttons, they highlighted on hover, and clicking them did
        // nothing. Print is the one Phase 8 needed, and the other three were
        // beside it.
        var commands = AppHost.Run(() =>
        {
            var window = NewWindow();
            try
            {
                window.Show();
                window.UpdateLayout();

                return Descendants(window)
                    .OfType<Button>()
                    .Where(b => b.Name is "NewButton" or "OpenButton" or "SaveButton" or "PrintButton")
                    .Select(b => (b.Name, b.Command))
                    .ToList();
            }
            finally
            {
                window.Close();
            }
        });

        Assert.Equal(4, commands.Count);
        Assert.All(commands, pair => Assert.NotNull(pair.Command));
        Assert.Contains((("PrintButton"), (ICommand)Commands.Print), commands);
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
