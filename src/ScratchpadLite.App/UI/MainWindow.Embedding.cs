using System.IO;
using ScratchpadLite.App.Fonts;
using ScratchpadLite.App.Services;
using ScratchpadLite.Core.Fonts;
using ScratchpadLite.Core.Formats;
using ScratchpadLite.Core.Model;
using ScratchpadLite.Core.Platform;

namespace ScratchpadLite.App.UI;

/// <summary>
/// Fonts travelling inside documents (Section 7.4): deciding what goes into
/// a save, and loading what came in with an opened file.
/// </summary>
/// <remarks>Phase 10.</remarks>
public partial class MainWindow
{
    /// <summary>The formats that can carry a font. The others ignore the setting.</summary>
    private static bool CanEmbed(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".docx" or ".odt";

    /// <summary>
    /// What this save should put inside the file.
    /// </summary>
    /// <remarks>
    /// <para>Two sources, and they answer different questions. A font the
    /// document arrived with stays in it, because stripping it out would be
    /// this program deciding on the user's behalf that a document should stop
    /// looking the way it looks everywhere else - and they would find out on
    /// somebody else's PC. The setting decides something narrower: whether a
    /// font from this PC's own font folder is added to a document that did
    /// not have it.</para>
    ///
    /// <para>Either way, only families the document still uses travel. A font
    /// for text that has been deleted is weight in the file and nothing
    /// else.</para>
    /// </remarks>
    private WriteOptions FontsToEmbed(
        DocumentSession session, SpDocument document, string path, bool fromAutoSave)
    {
        if (!CanEmbed(path))
        {
            return WriteOptions.None;
        }

        var plan = FontEmbedding.ForSave(
            session.EmbeddedFonts,
            document.FontsUsed,
            _settings.EmbedFontsOnSave,
            FontRegistry.Index,
            FontCache.TryRead);

        ReportFontsLeftBehind(session, plan.Left, fromAutoSave);

        if (plan.Fonts.Count > 0)
        {
            Log.Info($"embedding {plan.Fonts.Count} font faces in {Path.GetFileName(path)}");
        }

        return new WriteOptions(plan.Fonts);
    }

    /// <summary>
    /// Says which fonts are not travelling, once per family per session.
    /// </summary>
    /// <remarks>
    /// Section 7.4 asks for this in a save warning. It goes in the status bar
    /// instead, where the AutoSave skip reasons already go, because the save
    /// warning is a modal dialog and this is a condition that does not change
    /// between saves: a licence that forbids embedding will forbid it again
    /// in two minutes, and a dialog every time would train the user to click
    /// it away. Saying it once a session is saying it.
    /// </remarks>
    private void ReportFontsLeftBehind(
        DocumentSession session, IReadOnlyList<FontEmbedding.Skipped> left, bool fromAutoSave)
    {
        foreach (var skipped in left)
        {
            Log.Info($"{skipped.Family} was not embedded: {skipped.Reason}");
        }

        var fresh = left
            .Where(s => _saidAboutFont.Add($"{session.Id}:{s.Family}"))
            .ToList();

        if (fresh.Count == 0 || fromAutoSave)
        {
            return;
        }

        var first = fresh[0];

        Status.ShowMessage(
            fresh.Count == 1
                ? $"{first.Family} was not embedded: {first.Reason}."
                : $"{fresh.Count} fonts were not embedded, starting with {first.Family}: {first.Reason}.",
            transient: false);
    }

    /// <summary>
    /// Unpacks the fonts an opened document brought with it and makes them
    /// resolvable for that tab alone.
    /// </summary>
    private void LoadEmbeddedFonts(DocumentSession session, IReadOnlyList<EmbeddedFont> fonts)
    {
        session.EmbeddedFonts = fonts;

        if (fonts.Count == 0)
        {
            return;
        }

        var unpacked = FontEmbedding.Unpack(fonts, session.Id);
        var loaded = FontRegistry.UseEmbedded(session.Id, unpacked);

        Log.Info($"{session.Title}: {loaded} of {fonts.Count} embedded font faces loaded");

        if (loaded < fonts.Count)
        {
            Status.ShowMessage(
                "Some of the fonts inside this document could not be loaded; "
                + "it may look different here.",
                transient: false);
        }
    }

    /// <summary>
    /// Forgets a closed document's embedded fonts.
    /// </summary>
    private static void ReleaseEmbeddedFonts(DocumentSession session)
    {
        if (session.EmbeddedFonts.Count > 0)
        {
            FontRegistry.ForgetEmbedded(session.Id);
        }
    }
}
