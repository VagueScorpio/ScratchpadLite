using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using ScratchpadLite.App.Fonts;
using ScratchpadLite.Core.Fonts;
using ScratchpadLite.Core.Platform;

namespace ScratchpadLite.App.UI.Dialogs;

/// <summary>
/// What happened to every file in the font folder (Section 6.6): loaded
/// directly, converted, a duplicate of another file, or unsupported with the
/// reason why. Created the first time it is opened, never at startup (Section
/// 2.1).
/// </summary>
/// <remarks>
/// Every row comes out of the font index, which the background pass has
/// already loaded, so this window opens instantly whether the folder holds
/// five fonts or five hundred. It reads the index and never changes it.
/// </remarks>
public partial class FontReport : Window
{
    private List<Row> _rows = [];

    public FontReport()
    {
        InitializeComponent();
        Load();
    }

    /// <summary>One line of the report.</summary>
    internal sealed record Row(
        string File,
        string Type,
        string Status,
        string Family,
        string Embedding,
        string Message)
    {
        /// <summary>Something the user may want to act on.</summary>
        public bool IsProblem { get; init; }

        /// <summary>Correct, but not interesting - a duplicate or a licence file.</summary>
        public bool IsIgnorable { get; init; }

        public string ToolTip { get; init; } = "";
    }

    private void Load()
    {
        var index = FontRegistry.Index;

        _rows = index.Entries
            .OrderBy(e => e.SourcePath, StringComparer.OrdinalIgnoreCase)
            .Select(ToRow)
            .ToList();

        var usable = _rows.Count(r => r.Status is "Loaded" or "Converted");
        var problems = _rows.Count(r => r.IsProblem);

        Summary.Text = _rows.Count == 0
            ? "The font folder is empty. Drop font files into it and click Reload Fonts."
            : $"{_rows.Count} files, {usable} usable, {problems} with something to report.";

        Hint.Text = AppPaths.FontDir;

        Apply();
    }

    private static Row ToRow(FontIndex.Entry entry)
    {
        var problem = entry.Status is FontStatus.Failed or FontStatus.Unsupported;
        var ignorable = entry.Status is FontStatus.Duplicate or FontStatus.Skipped;

        var tooltip = new StringBuilder(entry.SourcePath);

        if (entry.Families.Count > 1)
        {
            tooltip.Append("\nFamilies: ").Append(string.Join(", ", entry.Families));
        }

        if (entry.GlyphCount > 0)
        {
            tooltip.Append("\nGlyphs: ").Append(entry.GlyphCount);
        }

        if (entry.IsVariable)
        {
            tooltip.Append("\nVariable font");
        }

        if (!string.IsNullOrEmpty(entry.Copyright))
        {
            tooltip.Append('\n').Append(entry.Copyright);
        }

        return new Row(
            File: entry.SourcePath,
            Type: Describe(entry.Kind),
            Status: entry.Status.ToString(),
            Family: entry.Families.Count > 0 ? entry.Families[0] : "",
            Embedding: entry.IsUsable ? Rights(entry.FsType) : "",
            Message: entry.Message)
        {
            IsProblem = problem,
            IsIgnorable = ignorable,
            ToolTip = tooltip.ToString(),
        };
    }

    private static string Describe(FontFileKind kind) => kind switch
    {
        FontFileKind.Direct => "OpenType",
        FontFileKind.Woff => "WOFF",
        FontFileKind.Woff2 => "WOFF2",
        FontFileKind.Type1 => "Type 1",
        FontFileKind.Type1Metrics => "Type 1 metrics",
        FontFileKind.MacResource => "Mac container",
        FontFileKind.Eot => "EOT",
        FontFileKind.Type42 => "Type 42",
        FontFileKind.ZipArchive => "Zip",
        FontFileKind.LicenseText => "Licence text",
        FontFileKind.Unsupported => "Unsupported",
        _ => kind.ToString(),
    };

    /// <summary>
    /// fsType as a phrase. Bit 9 is the one that decides Section 7.4, and it
    /// wins over the others whatever else is set.
    /// </summary>
    private static string Rights(ushort fsType)
    {
        if ((fsType & 0x0200) != 0)
        {
            return "Restricted";
        }

        if ((fsType & 0x0008) != 0)
        {
            return "Editable";
        }

        if ((fsType & 0x0004) != 0)
        {
            return "Preview and print";
        }

        return fsType == 0 ? "Installable" : $"0x{fsType:X4}";
    }

    private void Apply()
    {
        Rows.ItemsSource = ProblemsOnly.IsChecked == true
            ? _rows.Where(r => r.IsProblem).ToList()
            : _rows;
    }

    private void OnFilterChanged(object sender, RoutedEventArgs e) => Apply();

    private void OnCopy(object sender, RoutedEventArgs e)
    {
        var text = new StringBuilder();
        text.AppendLine("Scratchpad Lite - Font Report");
        text.AppendLine(AppPaths.FontDir);
        text.AppendLine();

        foreach (var row in (IEnumerable<Row>)Rows.ItemsSource)
        {
            text.Append(row.File).Append('\t')
                .Append(row.Type).Append('\t')
                .Append(row.Status).Append('\t')
                .Append(row.Family).Append('\t')
                .Append(row.Embedding).Append('\t')
                .AppendLine(row.Message);
        }

        try
        {
            Clipboard.SetText(text.ToString());
            Hint.Text = "Copied.";
        }
        catch (System.Runtime.InteropServices.ExternalException ex)
        {
            // Another process can hold the clipboard open; nothing to do but
            // say so (Section 9).
            Log.Warn($"the font report could not be copied: {ex.Message}");
            Hint.Text = "The clipboard is in use by another program. Try again.";
        }
    }

    private void OnOpenFolder(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.FontDir);
            Process.Start(new ProcessStartInfo(AppPaths.FontDir) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception)
        {
            Log.Warn($"the font folder could not be opened: {ex.Message}");
            Hint.Text = "The font folder could not be opened.";
        }
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
