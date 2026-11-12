using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using ScratchpadLite.App.Services;
using ScratchpadLite.Core.Platform;

namespace ScratchpadLite.App.UI.Dialogs;

/// <summary>
/// Version, where the data lives, and the last startup time.
/// </summary>
/// <remarks>
/// The startup time is here rather than only in the log because Section 2.1
/// makes it a feature: a number the user can read is a number that stays
/// honest, and "it got slower" becomes a thing to check rather than a feeling.
/// </remarks>
public partial class About : Window
{
    public About()
    {
        InitializeComponent();

        NameText.Text = "Scratchpad Lite";
        VersionText.Text = $"Version {Version}";

        Add("Last startup", StartupTimer.ElapsedMs is { } ms ? $"{ms:F0} ms" : "not measured");
        Add("Portable", AppPaths.IsPortable ? "yes" : $"no - {AppPaths.NonPortableNotice}");
        Add("Data folder", AppPaths.DataDir);
        Add("Font folder", AppPaths.FontDir);
        Add("Program folder", AppPaths.ProgramDir);
    }

    internal static string Version =>
        Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
        ?? "unknown";

    private void Add(string label, string value)
    {
        var row = Facts.RowDefinitions.Count;

        Facts.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var name = new TextBlock
        {
            Text = label,
            Opacity = 0.75,
            Margin = new Thickness(0, 0, 14, 6),
        };

        Grid.SetRow(name, row);
        Grid.SetColumn(name, 0);
        Facts.Children.Add(name);

        // Selectable, which is the whole reason this is a window and not a
        // MessageBox: a path you cannot copy is a path you have to retype.
        var text = new TextBox
        {
            Text = value,
            IsReadOnly = true,
            BorderThickness = new Thickness(0),
            Background = System.Windows.Media.Brushes.Transparent,
            Padding = new Thickness(0),
            Margin = new Thickness(0, 0, 0, 6),
            TextWrapping = TextWrapping.Wrap,
        };

        text.SetResourceReference(ForegroundProperty, "WindowForeground");

        Grid.SetRow(text, row);
        Grid.SetColumn(text, 1);
        Facts.Children.Add(text);
    }

    private void OnOpenDataFolder(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(AppPaths.DataDir) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or System.IO.IOException)
        {
            Log.Warn($"the data folder could not be opened ({ex.Message})");
        }
    }
}
