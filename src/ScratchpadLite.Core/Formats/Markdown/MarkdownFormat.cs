using ScratchpadLite.Core.Model;
using ScratchpadLite.Core.Platform;

namespace ScratchpadLite.Core.Formats.Markdown;

/// <summary>
/// Markdown (Section 7.2). Read with Markdig, written by hand. Created only
/// by the lazy FormatRegistry, so Markdig is not loaded at startup
/// (Section 2.1).
/// </summary>
/// <remarks>
/// The only handler that writes more than one file: images go to a sidecar
/// folder beside the document, because Markdown has no way to carry them. That
/// means a save can leave files behind that a later save does not overwrite, so
/// the folder is cleared of the images this app wrote before the new ones go
/// in - and nothing else in it is touched, in case the user put something
/// there.
/// </remarks>
public sealed class MarkdownFormat : IFormatHandler
{
    public StyleSheet Styles { get; set; } = StyleSheet.Default;

    public string Name => "Markdown";

    public IReadOnlyList<string> Extensions => [".md", ".markdown"];

    public bool CanRead => true;

    public bool CanWrite => true;

    /// <summary>
    /// What survives: bold, italic, strikethrough, headings, lists, tables,
    /// images and links. Not fonts, sizes, colours, underline, super- and
    /// subscript, or page setup (Section 7.3).
    /// </summary>
    public Capabilities Capabilities =>
        Capabilities.CharFormat | Capabilities.Headings | Capabilities.Lists
        | Capabilities.Tables | Capabilities.Images | Capabilities.Links;

    public ReadResult Read(Stream input, string sourcePath, CancellationToken ct) =>
        MarkdownReader.Read(input, sourcePath, ct);

    public void Write(
        SpDocument doc,
        Stream output,
        string targetPath,
        CancellationToken ct,
        WriteOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentNullException.ThrowIfNull(output);

        var (text, images) = MarkdownWriter.Write(doc, targetPath, Styles, ct);

        using (var writer = new StreamWriter(output, new System.Text.UTF8Encoding(false), leaveOpen: true))
        {
            writer.Write(text);
            writer.Flush();
        }

        WriteSidecarImages(images, targetPath);
    }

    /// <summary>
    /// Writes the images beside the document (Section 7.2).
    /// </summary>
    /// <remarks>
    /// Not written through FileOps: these are not the document, and a failure
    /// to write one must not fail the save of the text itself. The Markdown
    /// file is what the user asked to save; a missing picture is a warning.
    /// </remarks>
    private static void WriteSidecarImages(
        IReadOnlyList<MarkdownWriter.Sidecar> images,
        string targetPath)
    {
        if (images.Count == 0)
        {
            return;
        }

        var folder = Path.GetDirectoryName(Path.GetFullPath(targetPath)) ?? "";
        var imageFolder = Path.Combine(folder, MarkdownWriter.ImageFolderFor(targetPath));

        try
        {
            Directory.CreateDirectory(imageFolder);

            // Only the files this app's own naming produces are cleared, so a
            // README or a picture the user put in the folder survives a save.
            foreach (var old in Directory.EnumerateFiles(imageFolder, "image*.*"))
            {
                try
                {
                    File.Delete(old);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Log.Debug($"an old sidecar image could not be removed: {ex.Message}");
                }
            }

            foreach (var image in images)
            {
                var path = Path.Combine(folder, image.RelativePath.Replace('/', Path.DirectorySeparatorChar));

                File.WriteAllBytes(path, image.Data);
            }

            Log.Info($"wrote {images.Count} sidecar images to {imageFolder}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"the sidecar images could not be written: {ex.Message}");
        }
    }
}
