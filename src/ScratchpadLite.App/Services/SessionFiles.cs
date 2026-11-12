using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using ScratchpadLite.Core.Platform;

namespace ScratchpadLite.App.Services;

/// <summary>
/// The list of files that were open when the app was last closed, for the
/// "reopen last session's files" setting (Section 6.12).
/// </summary>
/// <remarks>
/// Deliberately not the recovery file (Section 6.13.1), which is a different
/// thing with a different job: recovery holds the <em>contents</em> of
/// unsaved work after a crash, and this holds only names, after a clean exit.
/// Keeping them apart is what stops a reopened session quietly resurrecting
/// an old copy of a document the user has since changed elsewhere.
/// <para>
/// Only saved files are listed. An untitled document has no name to reopen,
/// and recovering its contents is recovery's job.
/// </para>
/// </remarks>
internal static class SessionFiles
{
    private static string Path => System.IO.Path.Combine(AppPaths.DataDir, "session.json");

    internal static IReadOnlyList<string> Load()
    {
        try
        {
            if (!File.Exists(Path))
            {
                return [];
            }

            using var stream = File.OpenRead(Path);

            return JsonSerializer.Deserialize(stream, SessionFilesJson.Default.OpenFiles)?.Files ?? [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Log.Warn($"session.json could not be read ({ex.GetType().Name})");
            return [];
        }
    }

    internal static void Save(IEnumerable<string> paths)
    {
        try
        {
            var files = new OpenFiles([..paths]);

            FileOps.AtomicWrite(
                Path,
                stream => JsonSerializer.Serialize(stream, files, SessionFilesJson.Default.OpenFiles),
                keepBak: false);
        }
        catch (Exception ex)
        {
            Log.Warn($"session.json could not be written ({ex.GetType().Name})");
        }
    }

    internal sealed record OpenFiles(IReadOnlyList<string> Files);
}

/// <summary>Source-generated JSON: no reflection, and this is read at startup.</summary>
[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(SessionFiles.OpenFiles))]
internal partial class SessionFilesJson : JsonSerializerContext;
