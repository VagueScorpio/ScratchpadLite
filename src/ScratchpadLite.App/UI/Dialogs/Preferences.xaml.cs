using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using ScratchpadLite.App.Editor;
using ScratchpadLite.App.Fonts;
using ScratchpadLite.App.Services;
using ScratchpadLite.Core.Model;
using ScratchpadLite.Core.Platform;

namespace ScratchpadLite.App.UI.Dialogs;

/// <summary>
/// Every setting in Section 6.2, including the AutoSave switch and interval,
/// the recovery interval, the theme, and whether font names are previewed in
/// their own font.
/// </summary>
/// <remarks>
/// <para>
/// This dialog reads a <see cref="Settings"/> and gives back a new one. It
/// changes nothing itself and holds no state of its own: the window applies
/// what comes back, which is the only way the theme, the open editors and the
/// settings file can end up agreeing with each other.
/// </para>
/// <para>
/// Every number is clamped rather than refused where a clamp has an obvious
/// right answer, and refused where it does not. An interval of 2 seconds
/// becomes 15; an interval of "soon" has to be typed again.
/// </para>
/// </remarks>
public partial class Preferences : Window
{
    private const long BytesPerMb = 1024 * 1024;

    public Preferences()
    {
        InitializeComponent();

        foreach (var size in FormatCommands.PresetSizesPt)
        {
            SizeBox.Items.Add(size.ToString("0.##", CultureInfo.CurrentCulture));
        }

        Loaded += (_, _) => Problem.Visibility = Visibility.Collapsed;
    }

    /// <summary>The settings as the dialog was left. Only valid on OK.</summary>
    internal Settings Result { get; private set; } = new();

    /// <summary>
    /// Fills the dialog.
    /// </summary>
    /// <param name="settings">The settings as they are now.</param>
    /// <param name="styles">The paragraph styles, for the default-style list.</param>
    /// <param name="languages">
    /// The spell-check languages Windows says it has (Section 6.11). Passed in
    /// rather than read here, because asking Windows is the window's business
    /// and the answer is already cached there.
    /// </param>
    internal void Show(Settings settings, StyleSheet styles, IReadOnlyList<string> languages)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(styles);
        ArgumentNullException.ThrowIfNull(languages);

        Result = settings;

        ThemeLight.IsChecked = settings.Theme == ThemeChoice.Light;
        ThemeDark.IsChecked = settings.Theme == ThemeChoice.Dark;
        ThemeFollow.IsChecked = settings.Theme == ThemeChoice.FollowWindows;

        DarkPaperBox.IsChecked = settings.DarkPaper;

        PageWidthButton.IsChecked = settings.PageView == PageViewMode.PageWidth;
        FullWidthButton.IsChecked = settings.PageView == PageViewMode.FullWidth;

        StatusBarBox.IsChecked = settings.ShowStatusBar;
        Toolbar1Box.IsChecked = settings.ShowToolbarRow1;
        Toolbar2Box.IsChecked = settings.ShowToolbarRow2;

        FillFonts(settings.DefaultFontFamily);

        SizeBox.Text = settings.DefaultFontSizePt.ToString("0.##", CultureInfo.CurrentCulture);

        StyleBox.Items.Clear();
        foreach (var style in styles.All)
        {
            StyleBox.Items.Add(style.Name);
        }

        StyleBox.SelectedItem = styles.Contains(settings.DefaultParagraphStyle)
            ? settings.DefaultParagraphStyle
            : StyleSheet.NormalStyleName;

        PreviewFontsBox.IsChecked = settings.PreviewFontNamesInOwnFont;

        SpellCheckBox.IsChecked = settings.SpellCheckEnabled;

        LanguageBox.Items.Clear();
        foreach (var language in languages)
        {
            LanguageBox.Items.Add(language);
        }

        if (!languages.Contains(settings.SpellCheckLanguage))
        {
            // A language saved on another PC is kept in the list, so opening
            // this dialog cannot silently change it.
            LanguageBox.Items.Add(settings.SpellCheckLanguage);
        }

        LanguageBox.SelectedItem = settings.SpellCheckLanguage;

        SpellLimitBox.Text = settings.SpellCheckMaxCharacters.ToString(CultureInfo.CurrentCulture);

        RecoveryIntervalBox.Text = settings.RecoveryIntervalSeconds.ToString(CultureInfo.CurrentCulture);

        AutoSaveBox.IsChecked = settings.AutoSaveEnabled;
        AutoSaveIntervalBox.Text = settings.AutoSaveIntervalSeconds.ToString(CultureInfo.CurrentCulture);
        AutoSaveOnFocusLossBox.IsChecked = settings.AutoSaveOnFocusLoss;
        KeepBakBox.IsChecked = settings.KeepBakFile;

        RestoreSessionBox.IsChecked = settings.RestoreLastSession;
        RecentCountBox.Text = settings.RecentFilesCount.ToString(CultureInfo.CurrentCulture);
        SingleInstanceBox.IsChecked = settings.SingleInstance;
        TxtBomBox.IsChecked = settings.TxtWriteBom;

        MaxOpenMbBox.Text = (settings.MaxOpenFileBytes / BytesPerMb).ToString(CultureInfo.CurrentCulture);
    }

    /// <summary>
    /// Fills the font list, from the index rather than from Windows.
    /// </summary>
    /// <remarks>
    /// <see cref="FontRegistry.AllFamilies"/> is the installed fonts and the
    /// <c>fonts/</c> folder together, which is the only list where a portable
    /// font can be chosen as the default.
    /// </remarks>
    private void FillFonts(string current)
    {
        FontBox.Items.Clear();

        foreach (var family in FontRegistry.AllFamilies)
        {
            FontBox.Items.Add(family);
        }

        FontBox.Text = current;
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        if (Commit())
        {
            DialogResult = true;
        }
    }

    /// <summary>
    /// Reads every control into <see cref="Result"/>.
    /// </summary>
    /// <returns>
    /// False when something was wrong, in which case the dialog stays open with
    /// the problem shown.
    /// </returns>
    /// <remarks>
    /// Separate from the OK handler so a test can exercise it. Setting
    /// <c>DialogResult</c> on a window that was not shown with
    /// <c>ShowDialog</c> throws, so a test that clicked OK would be testing
    /// WPF's dialog rules rather than this dialog's reading and clamping.
    /// </remarks>
    internal bool Commit()
    {
        if (!TryReadSeconds(RecoveryIntervalBox, "recovery", Recovery.MinIntervalSeconds, Recovery.MaxIntervalSeconds, out var recovery)
            || !TryReadSeconds(AutoSaveIntervalBox, "AutoSave", AutoSave.MinIntervalSeconds, AutoSave.MaxIntervalSeconds, out var autoSave)
            || !TryReadInt(SpellLimitBox, "spell check limit", 0, int.MaxValue, out var spellLimit)
            || !TryReadInt(RecentCountBox, "recent files", 0, 40, out var recentCount)
            || !TryReadInt(MaxOpenMbBox, "file size limit", 1, 2048, out var maxOpenMb)
            || !TryReadSize(out var size))
        {
            return false;
        }

        var family = FontBox.Text.Trim();

        if (family.Length == 0)
        {
            Complain("Give a font name for new documents.");
            FontBox.Focus();

            return false;
        }

        Result = Result with
        {
            Theme = ThemeDark.IsChecked == true
                ? ThemeChoice.Dark
                : ThemeLight.IsChecked == true
                    ? ThemeChoice.Light
                    : ThemeChoice.FollowWindows,

            DarkPaper = DarkPaperBox.IsChecked == true,
            PageView = FullWidthButton.IsChecked == true ? PageViewMode.FullWidth : PageViewMode.PageWidth,
            ShowStatusBar = StatusBarBox.IsChecked == true,
            ShowToolbarRow1 = Toolbar1Box.IsChecked == true,
            ShowToolbarRow2 = Toolbar2Box.IsChecked == true,

            DefaultFontFamily = family,
            DefaultFontSizePt = size,
            DefaultParagraphStyle = StyleBox.SelectedItem as string ?? StyleSheet.NormalStyleName,
            PreviewFontNamesInOwnFont = PreviewFontsBox.IsChecked == true,

            SpellCheckEnabled = SpellCheckBox.IsChecked == true,
            SpellCheckLanguage = LanguageBox.SelectedItem as string ?? Result.SpellCheckLanguage,
            SpellCheckMaxCharacters = spellLimit,

            RecoveryIntervalSeconds = recovery,
            AutoSaveEnabled = AutoSaveBox.IsChecked == true,
            AutoSaveIntervalSeconds = autoSave,
            AutoSaveOnFocusLoss = AutoSaveOnFocusLossBox.IsChecked == true,
            KeepBakFile = KeepBakBox.IsChecked == true,

            RestoreLastSession = RestoreSessionBox.IsChecked == true,
            RecentFilesCount = recentCount,
            SingleInstance = SingleInstanceBox.IsChecked == true,
            TxtWriteBom = TxtBomBox.IsChecked == true,
            MaxOpenFileBytes = maxOpenMb * BytesPerMb,
        };

        return true;
    }

    private bool TryReadSize(out double size)
    {
        if (!double.TryParse(SizeBox.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out size)
            || size is < 1 or > 1638)
        {
            Complain("Give a font size between 1 and 1638 points.");
            SizeBox.Focus();

            return false;
        }

        return true;
    }

    /// <summary>
    /// Reads an interval, clamping it into range rather than refusing it.
    /// </summary>
    /// <remarks>
    /// Clamped because a number out of range is still an answer to the
    /// question - someone typing 5 wants it as often as possible - while a
    /// dialog that says "between 15 and 1800" and waits makes them work out
    /// which end they wanted.
    /// </remarks>
    private bool TryReadSeconds(TextBox box, string what, int min, int max, out int seconds)
    {
        if (!int.TryParse(box.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out seconds))
        {
            Complain($"Give the {what} interval as a number of seconds.");
            box.Focus();
            box.SelectAll();

            return false;
        }

        var clamped = Math.Clamp(seconds, min, max);

        if (clamped != seconds)
        {
            seconds = clamped;
            box.Text = clamped.ToString(CultureInfo.CurrentCulture);
        }

        return true;
    }

    private bool TryReadInt(TextBox box, string what, int min, int max, out int value)
    {
        if (!int.TryParse(box.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out value))
        {
            Complain($"Give the {what} as a whole number.");
            box.Focus();
            box.SelectAll();

            return false;
        }

        var clamped = Math.Clamp(value, min, max);

        if (clamped != value)
        {
            value = clamped;
            box.Text = clamped.ToString(CultureInfo.CurrentCulture);
        }

        return true;
    }

    private void Complain(string message)
    {
        Problem.Text = message;
        Problem.Visibility = Visibility.Visible;
    }
}
