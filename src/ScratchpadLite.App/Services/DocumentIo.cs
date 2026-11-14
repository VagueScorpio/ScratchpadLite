using System.IO;
using System.Windows.Documents;
using ScratchpadLite.App.Bridge;
using ScratchpadLite.Core.Fonts;
using ScratchpadLite.Core.Formats;
using ScratchpadLite.Core.Model;
using ScratchpadLite.Core.Platform;

namespace ScratchpadLite.App.Services;

/// <summary>
/// Opening and saving a document, following the flow in Section 4.4.
/// </summary>
/// <remarks>
/// The division of labour is what keeps the window responsive. Reading or
/// writing a file is work on a background thread and produces or consumes IR
/// only; building a FlowDocument from that IR, or walking one into IR, happens
/// on the UI thread because a FlowDocument belongs to the thread that owns the
/// window.
/// <para>
/// So the slow part - parsing a large .docx, zipping one up - never blocks
/// typing, and the part that must be on the UI thread is a tree copy with no
/// I/O in it.
/// </para>
/// </remarks>
internal static class DocumentIo
{
    /// <summary>What came back from reading a file.</summary>
    internal sealed record Opened(
        SpDocument Document,
        IReadOnlyList<string> Warnings,
        FileOps.Stamp? Stamp)
    {
        /// <summary>The fonts the file carried with it (Section 7.4).</summary>
        public IReadOnlyList<EmbeddedFont> EmbeddedFonts { get; init; } = [];
    }

    /// <summary>Why a file could not be opened, in words meant for a person.</summary>
    internal sealed class OpenException(string message, Exception? inner = null)
        : Exception(message, inner);

    /// <summary>
    /// Reads a file into IR, off the UI thread.
    /// </summary>
    internal static async Task<Opened> ReadAsync(string path, StyleSheet styles, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        if (FormatRegistry.IsLegacyWord(path))
        {
            throw new OpenException(FormatRegistry.LegacyWordMessage);
        }

        var extension = Path.GetExtension(path);

        // RTF is read on the UI thread, by WPF, straight onto a FlowDocument -
        // so it cannot go down this path at all (Section 7.2). The caller
        // handles it; this is here so a mistake is loud rather than silent.
        if (FormatRegistry.InfoFor(extension) is { HandledOnTheFlowDocument: true })
        {
            throw new InvalidOperationException(
                $"{extension} is read on the UI thread; use the flow-document path instead.");
        }

        var handler = FormatRegistry.For(extension);

        if (handler is null)
        {
            throw new OpenException(
                $"Scratchpad Lite cannot open {extension} files. "
                + $"It opens {string.Join(", ", FormatRegistry.All.Where(f => f.CanRead).SelectMany(f => f.Extensions))}.");
        }

        if (handler is Core.Formats.Docx.DocxFormat docx)
        {
            docx.Styles = styles;
        }

        return await Task.Run(
            () =>
            {
                var stamp = FileOps.Stamp.Of(path);

                try
                {
                    Limits.CheckFileSize(path);

                    using var stream = File.OpenRead(path);
                    var result = handler.Read(stream, path, ct);

                    Log.Info(
                        $"opened {Path.GetFileName(path)} ({handler.Name}, "
                        + $"{result.Document.Blocks.Count} blocks, {result.Warnings.Count} warnings)");

                    return new Opened(result.Document, result.Warnings, stamp)
                    {
                        EmbeddedFonts = result.EmbeddedFonts,
                    };
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Log.Error($"{Path.GetFileName(path)} could not be opened", ex);
                    throw Explain(path, handler.Name, ex);
                }
            },
            ct)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Writes IR to a file, off the UI thread, through the safe-save path.
    /// </summary>
    internal static async Task WriteAsync(
        SpDocument document,
        string path,
        StyleSheet styles,
        bool keepBak,
        WriteOptions? options,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrEmpty(path);

        var extension = Path.GetExtension(path);
        var handler = FormatRegistry.For(extension);

        if (handler is null || !handler.CanWrite)
        {
            throw new FileOps.SaveException(
                $"Scratchpad Lite cannot save {extension} files.");
        }

        if (handler is Core.Formats.Docx.DocxFormat docx)
        {
            docx.Styles = styles;
        }

        await Task.Run(
            () =>
            {
                // The handler writes into the temporary file FileOps makes, so
                // the real file is only replaced once the whole thing is on
                // disk (Section 6.14).
                FileOps.AtomicWrite(
                    path,
                    stream => handler.Write(document, stream, path, ct, options),
                    keepBak);

                Log.Info($"saved {Path.GetFileName(path)} ({handler.Name})");
            },
            ct)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Builds a FlowDocument from IR. UI thread.
    /// </summary>
    internal static FlowDocument ToFlowDocument(
        SpDocument document, StyleSheet styles, Guid? documentId = null) =>
        FlowBuilder.Build(document, styles, documentId);

    /// <summary>
    /// Walks a FlowDocument into IR, keeping what the file carried that the
    /// FlowDocument cannot. UI thread.
    /// </summary>
    internal static SpDocument ToIr(FlowDocument flow, DocumentSession session, StyleSheet styles) =>
        FlowWalker.Walk(flow, styles, session.Meta, session.Page);

    private static OpenException Explain(string path, string format, Exception ex)
    {
        var name = Path.GetFileName(path);

        return ex switch
        {
            FileNotFoundException => new OpenException($"{name} is not there any more."),

            DirectoryNotFoundException => new OpenException(
                $"The folder for {name} is not there any more. "
                + "If it is on a network drive or a USB stick, check it is still connected."),

            UnauthorizedAccessException => new OpenException(
                $"Windows would not let this open {name}."),

            FileTooLargeException => new OpenException(ex.Message),

            IOException io when io.HResult == unchecked((int)0x80070020) => new OpenException(
                $"{name} is open in another program and cannot be read right now."),

            // A file that is not what its extension says, which is the common
            // case: a .docx that is really a .doc, or a truncated download.
            InvalidDataException or FileFormatException => new OpenException(
                $"{name} is not a valid {format} file. It may be damaged, or it may be "
                + "a different format with the wrong name."),

            _ => new OpenException($"{name} could not be opened.", ex),
        };
    }
}
