namespace ScratchpadLite.Core.Platform;

/// <summary>
/// The command line the app understands. Parsed before the first frame
/// (Section 4.3), so it stays allocation-light and does no I/O.
/// </summary>
/// <param name="Files">Files to open, in order. The first one becomes the active tab.</param>
/// <param name="MeasureStartup">
/// <c>--measure-startup</c>: render once, print the time to stdout and the log, exit
/// (Section 2.1). <c>build.ps1</c> runs this five times and reports the median.
/// </param>
/// <param name="Debug"><c>--debug</c>: log at Debug level.</param>
/// <param name="FontDirOverride"><c>--fonts &lt;path&gt;</c>, or null.</param>
public sealed record CommandLineOptions(
    IReadOnlyList<string> Files,
    bool MeasureStartup,
    bool Debug,
    string? FontDirOverride)
{
    /// <summary>
    /// <c>--font-licenses &lt;path&gt;</c>: write FONT_LICENSES.txt from the
    /// font folder and exit (Section 6.6.6).
    /// </summary>
    /// <remarks>
    /// An init property rather than another constructor parameter, so adding
    /// it does not change every call site that builds one of these.
    /// <para>
    /// It exists because the licences have to be read out of the fonts' own
    /// name tables, and build.ps1 is PowerShell - this is how the build script
    /// borrows the reader rather than growing a second one.
    /// </para>
    /// </remarks>
    public string? FontLicensesOutput { get; init; }

    public static CommandLineOptions Parse(string[] args)
    {
        var files = new List<string>();
        var measure = false;
        var debug = false;
        string? fontDir = null;
        string? licenses = null;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--measure-startup":
                    measure = true;
                    break;
                case "--debug":
                    debug = true;
                    break;
                case "--fonts" when i + 1 < args.Length:
                    fontDir = args[++i];
                    break;
                case "--font-licenses" when i + 1 < args.Length:
                    licenses = args[++i];
                    break;
                default:
                    if (!args[i].StartsWith("--", StringComparison.Ordinal))
                    {
                        files.Add(args[i]);
                    }

                    break;
            }
        }

        return new CommandLineOptions(files, measure, debug, fontDir)
        {
            FontLicensesOutput = licenses,
        };
    }
}
