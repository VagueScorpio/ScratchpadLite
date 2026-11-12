using System.IO;
using System.Windows.Threading;
using ScratchpadLite.Core.Formats;
using ScratchpadLite.Core.Model;
using ScratchpadLite.Core.Platform;

namespace ScratchpadLite.App.Services;

/// <summary>
/// AutoSave writes the real file (Section 6.13.2). It is off by default and
/// has a visible switch, because silently overwriting a file is not
/// something to turn on for someone.
///
/// It only ever saves a document that already has a path and is dirty, in
/// the format it was opened as, through the same atomic write and lossy
/// format check as a manual save. A document that has never been saved is
/// left alone: crash recovery already covers it.
/// </summary>
/// <remarks>
/// <para>
/// This class decides <em>whether</em> and <em>when</em>; the window does the
/// saving, through the same <c>SaveAsync</c> a menu click uses. That split is
/// deliberate: a second save path would be a second place for the atomic
/// write, the format warning and the changed-on-disk check to drift out of
/// step, and the one that nobody clicks is the one that would rot.
/// </para>
/// <para>
/// The decision in <see cref="Check"/> is a pure function of a document's
/// state, which is what makes the Section 6.13.2 skip rules testable without a
/// window or a timer.
/// </para>
/// </remarks>
internal sealed class AutoSave
{
    /// <summary>
    /// How long typing has to have stopped before a save (Section 6.13.2), so
    /// a save never lands mid-word.
    /// </summary>
    internal static readonly TimeSpan IdlePause = TimeSpan.FromSeconds(2);

    /// <summary>
    /// The longer pause used for a document whose walk is slow
    /// (Section 6.13.2).
    /// </summary>
    internal static readonly TimeSpan SlowDocumentPause = TimeSpan.FromSeconds(5);

    /// <summary>A walk longer than this makes a document a slow one.</summary>
    internal const int SlowWalkMilliseconds = 100;

    internal const int MinIntervalSeconds = 15;

    internal const int MaxIntervalSeconds = 30 * 60;

    private readonly HashSet<Guid> _slow = [];
    private readonly Dictionary<Guid, string> _toldAbout = [];

    private DispatcherTimer? _timer;

    internal bool Enabled { get; set; }

    /// <summary>When the user last typed, in any document.</summary>
    /// <remarks>
    /// One clock rather than one per document, because the thing being waited
    /// for is the user pausing, and a user types in one document at a time.
    /// </remarks>
    internal DateTime LastTypingUtc { get; private set; } = DateTime.MinValue;

    /// <summary>Raised when the timer fires and AutoSave is on.</summary>
    internal event EventHandler? DueForSave;

    internal void Start(int intervalSeconds)
    {
        Stop();

        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(Math.Clamp(intervalSeconds, MinIntervalSeconds, MaxIntervalSeconds)),
        };

        _timer.Tick += OnTick;
        _timer.Start();
    }

    internal void Stop()
    {
        if (_timer is not null)
        {
            _timer.Tick -= OnTick;
            _timer.Stop();
            _timer = null;
        }
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (Enabled)
        {
            DueForSave?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Called on every edit, to push the idle pause back.</summary>
    internal void NoteTyping() => LastTypingUtc = DateTime.UtcNow;

    /// <summary>
    /// Records how long walking this document took, so a slow one gets the
    /// longer pause (Section 6.13.2).
    /// </summary>
    internal void NoteWalk(DocumentSession session, TimeSpan elapsed)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (elapsed.TotalMilliseconds <= SlowWalkMilliseconds)
        {
            _slow.Remove(session.Id);

            return;
        }

        if (_slow.Add(session.Id))
        {
            Log.Info(
                $"walking {session.Title} for AutoSave took {elapsed.TotalMilliseconds:F0} ms, "
                + $"so it will only be saved after a {SlowDocumentPause.TotalSeconds:F0}-second pause");
        }
    }

    /// <summary>Whether this document is one whose walk was measured as slow.</summary>
    internal bool IsSlow(DocumentSession session) => _slow.Contains(session.Id);

    /// <summary>
    /// Whether typing has paused long enough for this document to be saved.
    /// </summary>
    internal bool IsIdleEnough(DocumentSession session, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(session);

        var wanted = IsSlow(session) ? SlowDocumentPause : IdlePause;

        return nowUtc - LastTypingUtc >= wanted;
    }

    // ------------------------------------------------- the Section 6.13.2 rules --

    internal enum Skip
    {
        /// <summary>Nothing in the way: save it.</summary>
        None,

        /// <summary>Never saved, so there is no file to save to.</summary>
        NeverSaved,

        /// <summary>The format would drop something this document uses.</summary>
        WouldLoseFormatting,

        /// <summary>Read-only, or held by another program.</summary>
        NotWritable,

        /// <summary>Something else changed the file since it was opened.</summary>
        ChangedOnDisk,
    }

    /// <summary>A reason not to AutoSave, in the words the status bar shows.</summary>
    internal sealed record Decision(Skip Reason, string Note)
    {
        internal bool CanSave => Reason == Skip.None;

        internal static readonly Decision Go = new(Skip.None, "");
    }

    /// <summary>
    /// Whether AutoSave may write this document, and what to say when it may
    /// not (Section 6.13.2).
    /// </summary>
    /// <param name="session">The document.</param>
    /// <param name="ir">
    /// The document as walked, for the format check. Null skips that check,
    /// for a caller that has not walked it.
    /// </param>
    /// <remarks>
    /// Every one of these documents still gets its recovery copy. That is the
    /// division of labour: AutoSave declines when writing the user's file would
    /// be presumptuous or lossy, and recovery covers the gap without touching
    /// the file at all.
    /// </remarks>
    internal static Decision Check(DocumentSession session, SpDocument? ir)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (session.FilePath is not { } path)
        {
            return new Decision(
                Skip.NeverSaved,
                "Save this document once to turn on AutoSave for it.");
        }

        var format = FormatRegistry.InfoFor(Path.GetExtension(path));

        if (format is null || !format.CanWrite)
        {
            return new Decision(
                Skip.NeverSaved,
                $"AutoSave is paused: Scratchpad Lite cannot write {Path.GetExtension(path)} files. "
                + "Save as .docx to use AutoSave.");
        }

        // RTF goes through WPF's own converter and keeps everything this app
        // can show, so it is fine (Section 6.13.2).
        if (ir is not null && !format.HandledOnTheFlowDocument)
        {
            var lost = FormatRegistry.WhatWouldBeLost(IrFeatures.Used(ir), format.Capabilities);

            if (lost.Count > 0)
            {
                return new Decision(
                    Skip.WouldLoseFormatting,
                    $"AutoSave is paused: saving as {format.Name} would lose {lost[0].ToLowerInvariant()}. "
                    + "Save as .docx to use AutoSave.");
            }
        }

        if (IsReadOnly(path))
        {
            return new Decision(
                Skip.NotWritable,
                $"AutoSave is paused: {Path.GetFileName(path)} is read-only.");
        }

        if (FileOps.ChangedOnDisk(path, session.OpenedStamp))
        {
            return new Decision(
                Skip.ChangedOnDisk,
                $"AutoSave is paused: {Path.GetFileName(path)} has changed on disk. "
                + "Save it yourself to decide which version wins.");
        }

        return Decision.Go;
    }

    private static bool IsReadOnly(string path)
    {
        try
        {
            return File.Exists(path) && new FileInfo(path).IsReadOnly;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// True the first time a given reason is reported for a document, so the
    /// bar is shown once rather than on every tick (Section 6.13.2).
    /// </summary>
    internal bool IsWorthSaying(DocumentSession session, Decision decision)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(decision);

        if (_toldAbout.TryGetValue(session.Id, out var already) && already == decision.Note)
        {
            return false;
        }

        _toldAbout[session.Id] = decision.Note;

        return true;
    }

    /// <summary>Forgets what was said about a document, after a successful save.</summary>
    internal void Clear(DocumentSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        _toldAbout.Remove(session.Id);
    }

    /// <summary>
    /// The hover note for the status-bar switch: why AutoSave is not saving the
    /// document in front, or what it is doing (Section 6.13.2).
    /// </summary>
    internal string TooltipFor(DocumentSession? session)
    {
        const string whatItIs =
            "AutoSave writes the real file. Crash recovery is always on and is separate.";

        if (!Enabled)
        {
            return $"AutoSave is off. {whatItIs}";
        }

        if (session is null)
        {
            return whatItIs;
        }

        var decision = Check(session, null);

        return decision.CanSave
            ? $"AutoSave is on for {session.Title}. {whatItIs}"
            : $"{decision.Note} {whatItIs}";
    }
}
