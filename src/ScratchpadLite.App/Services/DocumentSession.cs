using System.IO;
using System.Windows.Documents;
using ScratchpadLite.Core.Model;

namespace ScratchpadLite.App.Services;

/// <summary>
/// One open document: its file path, format, FlowDocument, dirty flag, undo
/// state, recovery file and the warnings shown when it was opened
/// (Section 6.12). A tab owns exactly one of these.
/// </summary>
/// <remarks>
/// <para>
/// The things kept here that are not in the FlowDocument are the point:
/// metadata, page setup, and the file's state when it was opened. A
/// FlowDocument has nowhere to store any of them, and dropping them would mean
/// that opening a .docx and saving it threw away its title, its author and its
/// paper size.
/// </para>
/// </remarks>
internal sealed class DocumentSession
{
    private static int _untitledCount;

    internal DocumentSession() => UntitledNumber = ++_untitledCount;

    /// <summary>
    /// Identifies this document for the whole of its life, including across a
    /// crash: it is the name of its recovery file (Section 6.13.1).
    /// </summary>
    internal Guid Id { get; } = Guid.NewGuid();

    /// <summary>Where it came from, or null for a document never saved.</summary>
    internal string? FilePath { get; set; }

    /// <summary>
    /// The editor this tab's document lives in, or null while the tab has never
    /// been shown.
    /// </summary>
    /// <remarks>
    /// Section 2.1 and Section 6.12: a restored session makes one of these per
    /// tab only as each tab is first clicked. A RichTextBox is not cheap, and a
    /// window that built nine of them before its first frame would not start in
    /// a second.
    /// </remarks>
    internal Editor.EditorView? View { get; set; }

    /// <summary>
    /// The file this tab stands for but has not read yet (Section 6.12). Null
    /// once it is loaded, and for a tab that never had a file.
    /// </summary>
    internal string? PendingPath { get; set; }

    /// <summary>
    /// The format name for the status bar, from the handler the file was read
    /// with (Section 6.18).
    /// </summary>
    internal string FormatName { get; set; } = "TXT";

    /// <summary>When this document was last written to its recovery file (Section 6.13.1).</summary>
    internal DateTime LastRecoveryWriteUtc { get; set; }

    /// <summary>
    /// Whether this document has been edited since its last recovery write
    /// (Section 6.13.1).
    /// </summary>
    /// <remarks>
    /// Not the same as <see cref="IsDirty"/>: a document saved by hand is no
    /// longer dirty but its recovery copy is still out of date until the next
    /// write, and a document edited twice between two ticks only needs writing
    /// once. This flag is what stops the timer walking every open document
    /// every time it fires.
    /// </remarks>
    internal bool EditedSinceRecoveryWrite { get; set; }

    /// <summary>
    /// Whether a <c>.bak</c> has already been made for this document this
    /// session (Section 6.13.2).
    /// </summary>
    /// <remarks>
    /// With AutoSave on, a backup per save is a backup of the version from two
    /// minutes ago, which is worth nothing: the useful one is the version from
    /// when the file was opened. So it is written once and then left alone.
    /// </remarks>
    internal bool BakWritten { get; set; }

    /// <summary>
    /// The fonts this document arrived carrying (Section 7.4). Kept so a
    /// save puts them back: a document that travels with its fonts should
    /// keep doing so, whatever this PC has in its own font folder.
    /// </summary>
    internal IReadOnlyList<ScratchpadLite.Core.Fonts.EmbeddedFont> EmbeddedFonts { get; set; } = [];

    /// <summary>When AutoSave last saved this document (Section 6.13.2).</summary>
    internal DateTime LastAutoSaveUtc { get; set; }

    /// <summary>Which "Untitled N" this is, for a document with no file.</summary>
    internal int UntitledNumber { get; }

    /// <summary>The name for the title bar.</summary>
    internal string Title =>
        FilePath is { } path ? Path.GetFileName(path) : $"Untitled {UntitledNumber}";

    /// <summary>Whether there are changes that have not been saved.</summary>
    internal bool IsDirty { get; set; }

    internal FlowDocument? Document { get; set; }

    /// <summary>The IR as last saved, for the recovery file and the undo of a save.</summary>
    internal SpDocument? LastSavedIr { get; set; }

    /// <summary>
    /// What the file looked like when it was opened, so a save can notice
    /// someone else changed it (Section 6.14).
    /// </summary>
    internal FileOps.Stamp? OpenedStamp { get; set; }

    /// <summary>
    /// Metadata the file carried - title, author - which the FlowDocument has
    /// nowhere to keep.
    /// </summary>
    internal IReadOnlyDictionary<string, string> Meta { get; set; } = new Dictionary<string, string>();

    /// <summary>The paper and margins the file carried.</summary>
    internal PageSetup Page { get; set; } = new();

    /// <summary>Anything worth telling the user about how the file was read.</summary>
    internal IReadOnlyList<string> OpenWarnings { get; set; } = [];

    /// <summary>The fonts the document asks for, from the last walk.</summary>
    internal IReadOnlySet<string> FontsUsed { get; set; } = new HashSet<string>();

    /// <summary>
    /// Formats the user has asked not to be warned about again, per session
    /// (Section 7.1).
    /// </summary>
    internal static HashSet<string> SilencedWarnings { get; } = new(StringComparer.OrdinalIgnoreCase);
}
