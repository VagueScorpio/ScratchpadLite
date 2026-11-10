using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using ScratchpadLite.Core.Platform;

namespace ScratchpadLite.App.Services;

/// <summary>
/// The File, Open Recent list, kept in the data folder
/// (Section 5.2). Entries that no longer exist are shown greyed and removed
/// when clicked.
/// </summary>
/// <remarks>
/// Written on every open and save, so it survives a crash: a list that is only
/// written at exit is empty exactly when it would have been most useful.
/// Failures are logged and swallowed - a recent-files list that cannot be
/// written is not a reason to stop someone saving their work.
/// </remarks>
internal static class RecentFiles
{
    private const int Keep = 20;

    internal static IReadOnlyList<string> Load(int max)
    {
        try
        {
            if (!File.Exists(AppPaths.RecentFilesFile))
            {
                return [];
            }

            using var stream = File.OpenRead(AppPaths.RecentFilesFile);
            var list = JsonSerializer.Deserialize(stream, RecentFilesJson.Default.ListString);

            return list is null ? [] : list.Take(max).ToList();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            Log.Warn($"the recent files list could not be read: {ex.Message}");
            return [];
        }
    }

    internal static void Add(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            var full = Path.GetFullPath(path);
            var list = Load(Keep).ToList();

            // Moved to the front rather than added again, so opening the same
            // file twice does not fill the list with one name.
            list.RemoveAll(p => string.Equals(p, full, StringComparison.OrdinalIgnoreCase));
            list.Insert(0, full);

            if (list.Count > Keep)
            {
                list.RemoveRange(Keep, list.Count - Keep);
            }

            Directory.CreateDirectory(AppPaths.DataDir);

            var json = JsonSerializer.Serialize(list, RecentFilesJson.Default.ListString);
            var temp = $"{AppPaths.RecentFilesFile}.{Guid.NewGuid():N}.tmp";

            File.WriteAllText(temp, json);
            File.Move(temp, AppPaths.RecentFilesFile, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Log.Warn($"the recent files list could not be written: {ex.Message}");
        }
    }

    internal static void Remove(string path)
    {
        try
        {
            var list = Load(Keep).ToList();

            if (list.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase)) == 0)
            {
                return;
            }

            var json = JsonSerializer.Serialize(list, RecentFilesJson.Default.ListString);
            File.WriteAllText(AppPaths.RecentFilesFile, json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"the recent files list could not be updated: {ex.Message}");
        }
    }
}

/// <summary>
/// Source-generated JSON, so reading the list loads no reflection machinery
/// (Section 2.1).
/// </summary>
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(List<string>))]
internal sealed partial class RecentFilesJson : JsonSerializerContext;
