using System.Windows.Input;
using ScratchpadLite.App.UI;
using Xunit;

namespace ScratchpadLite.App.Tests;

/// <summary>
/// Section 8.3 requires that no two commands share a gesture. This is the test
/// that enforces it, rather than it being something to remember while adding a
/// command.
/// </summary>
public class CommandsTests
{
    [Fact]
    public void NoTwoCommandsShareAGesture()
    {
        var clashes = Commands.All
            .SelectMany(c => c.InputGestures
                .OfType<KeyGesture>()
                .Select(g => (Command: c.Name, Gesture: Describe(g))))
            .GroupBy(x => x.Gesture)
            .Where(g => g.Count() > 1)
            .Select(g => $"{g.Key} is on {string.Join(" and ", g.Select(x => x.Command))}")
            .ToList();

        Assert.Empty(clashes);
    }

    [Fact]
    public void EveryCommandHasAName()
    {
        Assert.All(Commands.All, c => Assert.False(string.IsNullOrWhiteSpace(c.Name)));
        Assert.All(Commands.All, c => Assert.False(string.IsNullOrWhiteSpace(c.Text)));
    }

    [Fact]
    public void AllIsDiscoveredByReflectionAndIsNotEmpty()
    {
        // Commands.All is built by reflecting over the class's own fields, so a
        // command added without touching a list still shows up. If that ever
        // breaks, every other test here would pass vacuously.
        Assert.True(Commands.All.Count > 40, $"expected the full command set, found {Commands.All.Count}");
    }

    [Theory]
    [InlineData("NewTab", Key.N, ModifierKeys.Control)]
    [InlineData("Save", Key.S, ModifierKeys.Control)]
    [InlineData("SaveAs", Key.S, ModifierKeys.Control | ModifierKeys.Shift)]
    [InlineData("PastePlainText", Key.V, ModifierKeys.Control | ModifierKeys.Shift)]
    [InlineData("SetUnderline", Key.U, ModifierKeys.Control)]
    [InlineData("Strikethrough", Key.D5, ModifierKeys.Alt | ModifierKeys.Shift)]
    [InlineData("Superscript", Key.OemPlus, ModifierKeys.Control | ModifierKeys.Shift)]
    [InlineData("Subscript", Key.OemPlus, ModifierKeys.Control)]
    [InlineData("StyleNormal", Key.D0, ModifierKeys.Control | ModifierKeys.Alt)]
    [InlineData("StyleHeading1", Key.D1, ModifierKeys.Control | ModifierKeys.Alt)]
    [InlineData("StyleHeading6", Key.D6, ModifierKeys.Control | ModifierKeys.Alt)]
    [InlineData("ClearFormatting", Key.Space, ModifierKeys.Control)]
    [InlineData("InsertLink", Key.K, ModifierKeys.Control)]
    [InlineData("ToggleSpellCheck", Key.F7, ModifierKeys.None)]
    [InlineData("ResetZoom", Key.D0, ModifierKeys.Control)]
    public void ShortcutMatchesTheBlueprint(string commandName, Key key, ModifierKeys modifiers)
    {
        var command = Commands.All.Single(c => c.Name == commandName);

        Assert.Contains(
            command.InputGestures.OfType<KeyGesture>(),
            g => g.Key == key && g.Modifiers == modifiers);
    }

    private static string Describe(KeyGesture gesture) => $"{gesture.Modifiers}+{gesture.Key}";
}
