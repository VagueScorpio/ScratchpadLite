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
/// Phase 7 gives each tab one of these. Until then the window has exactly one,
/// which is the same thing with the collection left out.
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

    /// <summary>Where it came from, or null for a document never saved.</summary>
    internal string? FilePath { get; set; }

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
