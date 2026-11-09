using System.Buffers;
using ScratchpadLite.Core.Formats;
using ScratchpadLite.Core.Platform;

namespace ScratchpadLite.Core.Fonts;

/// <summary>
/// The pipeline one new or changed font file goes through (Section 6.6.2), and
/// the incremental folder check that decides which files need it
/// (Section 6.6.1).
/// </summary>
/// <remarks>
/// Nothing here touches WPF, which is what lets the whole font folder be
/// prepared on a background thread while the window is already up and
/// typeable (Section 2.1). The App side turns the cached files into WPF
/// families afterwards.
/// <para>
/// Every file is wrapped so that one broken font produces one row in the Font
/// Report and nothing else (Section 6.6.7). There is no such thing here as a
/// font that stops the app.
/// </para>
/// </remarks>
public static class FontPreparer
{
    /// <summary>
    /// How long one file gets before it is abandoned (Section 6.6.2). A
    /// conversion that takes longer than this is a font doing something
    /// pathological, not a slow disk.
    /// </summary>
    public static readonly TimeSpan PerFileLimit = TimeSpan.FromSeconds(10);

    /// <summary>The largest font read through the pooled buffer (see CheckFolder).</summary>
    private const int PoolCeiling = 4 * 1024 * 1024;

    /// <summary>What a folder check did, for the status bar and the log.</summary>
    public sealed record Result(int Prepared, int Failed, int Unchanged, int Removed)
    {
        public bool AnythingChanged => Prepared > 0 || Removed > 0;
    }

    /// <summary>Reports progress so the status bar can show "Preparing fonts 12/480".</summary>
    public sealed record Progress(int Done, int Total, string CurrentFile);

    /// <summary>
    /// Compares the font folder against the index and prepares only what
    /// changed. Returns once every new or changed file has been dealt with.
    /// </summary>
    /// <param name="onFamiliesReady">
    /// Called after each file that produced families, so the picker can fill
    /// in as fonts become ready rather than all at the end.
    /// </param>
    public static Result CheckFolder(
        string fontDir,
        FontIndex index,
        Action<Progress>? onProgress,
        Action<FontIndex.Entry>? onFamiliesReady,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(index);

        var found = FontScanner.Scan(fontDir, ct);

        // A .zip is one file on disk but many entries in the index, so the
        // "what is present" list has to be built from the expanded view.
        var present = new List<string>(found.Count);
        var work = new List<FontScanner.Found>();
        var unchanged = 0;

        foreach (var file in found)
        {
            if (file.Kind == FontFileKind.ZipArchive)
            {
                // The archive's own card records the zip; its members are
                // keyed by pack.zip!inner/path and handled with it.
                present.Add(file.RelativePath);

                if (index.IsUnchanged(file))
                {
                    unchanged++;
                    foreach (var entry in index.Entries)
                    {
                        if (entry.SourcePath.StartsWith(
                                file.RelativePath + ZipFontSource.EntrySeparator,
                                StringComparison.OrdinalIgnoreCase))
                        {
                            present.Add(entry.SourcePath);
                        }
                    }

                    continue;
                }

                work.Add(file);
                continue;
            }

            present.Add(file.RelativePath);

            if (index.IsUnchanged(file))
            {
                unchanged++;
                continue;
            }

            work.Add(file);
        }

        var prepared = 0;
        var failed = 0;
        var done = 0;

        // A pool for this pass only, rather than ArrayPool<byte>.Shared.
        // Shared keeps its buffers on purpose - that is what makes it a shared
        // pool - and it cannot hold anything over 1 MB, so the large fonts got
        // no reuse and the small ones were never given back: 70 MB of live
        // buffers were still held after the pass. This pool goes out of scope
        // when the pass ends, so the collection below actually frees it, and
        // one buffer per size class is enough when files are read one at a
        // time.
        // Pooled only up to PoolCeiling. A pool's buckets are powers of two, so
        // letting it go all the way to the 50 MB file cap meant it could hold
        // one buffer of 1, 2, 4, 8, 16, 32 and 64 MB at the same time - about
        // 127 MB of live buffers for reading one file at a time. Capped here,
        // it holds about 8 MB, and the handful of fonts bigger than the cap get
        // a plain array each that becomes garbage immediately.
        var pool = ArrayPool<byte>.Create(maxArrayLength: PoolCeiling, maxArraysPerBucket: 1);

        foreach (var file in work)
        {
            ct.ThrowIfCancellationRequested();

            onProgress?.Invoke(new Progress(done, work.Count, Path.GetFileName(file.Path)));
            done++;

            foreach (var entry in PrepareOne(file, pool, ct))
            {
                index.Put(entry);

                if (entry.SourcePath != file.RelativePath)
                {
                    present.Add(entry.SourcePath);
                }

                if (entry.IsUsable)
                {
                    prepared++;
                    onFamiliesReady?.Invoke(entry);
                }
                else if (entry.Status == FontStatus.Failed)
                {
                    failed++;
                }
            }
        }

        MarkDuplicates(index);

        var released = index.PruneMissing(present);
        foreach (var cacheFile in released)
        {
            FontCache.Remove(cacheFile);
        }

        if (work.Count > 0)
        {
            onProgress?.Invoke(new Progress(work.Count, work.Count, ""));
        }

        if (prepared > 0)
        {
            ReleaseMemoryAfterBurst();
        }

        var result = new Result(prepared, failed, unchanged, released.Count);

        Log.Info(
            $"font folder check: {result.Prepared} prepared, {result.Failed} failed, "
            + $"{result.Unchanged} unchanged, {result.Removed} cache files released");

        return result;
    }

    /// <summary>
    /// Runs one file through the pipeline. A zip yields one card per font
    /// inside it plus one for the archive, which is why this returns a list.
    /// </summary>
    private static IReadOnlyList<FontIndex.Entry> PrepareOne(
        FontScanner.Found file,
        ArrayPool<byte> pool,
        CancellationToken ct)
    {
        using var perFile = CancellationTokenSource.CreateLinkedTokenSource(ct);
        perFile.CancelAfter(PerFileLimit);

        try
        {
            if (file.Kind == FontFileKind.ZipArchive)
            {
                return PrepareArchive(file, perFile.Token);
            }

            try
            {
                Limits.CheckFileSize(file.Path, Limits.MaxFontFileBytes);
            }
            catch (FileTooLargeException ex)
            {
                return [Card(file, FontStatus.Skipped, ex.Message)];
            }

            // Read into a pooled buffer rather than a fresh array per file.
            // A font is almost always over the 85 KB large-object threshold, so
            // File.ReadAllBytes here left one large-heap allocation per file
            // behind: preparing a 400 MB font folder held on to about 390 MB
            // afterwards, because the large object heap is not compacted and
            // the pages are not given back. Renting caps it at roughly the
            // largest single font instead.
            var pooled = file.Length <= PoolCeiling;
            var buffer = pooled ? pool.Rent((int)file.Length) : new byte[file.Length];

            try
            {
                int read;
                using (var stream = File.OpenRead(file.Path))
                {
                    read = stream.ReadAtLeast(
                        buffer.AsSpan(0, (int)file.Length),
                        (int)file.Length,
                        throwOnEndOfStream: false);
                }

                return
                [
                    PrepareBytes(
                        file.RelativePath,
                        file.Kind,
                        new ArraySegment<byte>(buffer, 0, read),
                        file.Length,
                        file.ModifiedUtc)
                ];
            }
            finally
            {
                if (pooled)
                {
                    pool.Return(buffer);
                }
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return [Card(file, FontStatus.Failed, $"gave up after {PerFileLimit.TotalSeconds:F0} seconds")];
        }
        catch (Exception ex)
        {
            // The catch-all the rules ask for: whatever a font file does, it
            // becomes a row in the report.
            Log.Warn($"{file.RelativePath} could not be prepared: {ex.Message}");
            return [Card(file, FontStatus.Failed, Explain(ex))];
        }
    }

    private static IReadOnlyList<FontIndex.Entry> PrepareArchive(FontScanner.Found file, CancellationToken ct)
    {
        var cards = new List<FontIndex.Entry>();
        var fonts = 0;

        try
        {
            // One member at a time, so a large archive is never held whole.
            ZipFontSource.ForEachMember(file.Path, ct, member =>
            {
                ct.ThrowIfCancellationRequested();

                if (member.Kind is not FontFileKind.LicenseText)
                {
                    fonts++;
                }

                var path = ZipFontSource.PathFor(file.RelativePath, member.EntryName);

                try
                {
                    cards.Add(PrepareBytes(
                        path, member.Kind, member.Bytes, member.Bytes.Length, file.ModifiedUtc));
                }
                catch (Exception ex)
                {
                    cards.Add(
                        new FontIndex.Entry(
                            path, member.Bytes.Length, file.ModifiedUtc, "", "", [], Explain(ex))
                        {
                            Kind = member.Kind,
                            Status = FontStatus.Failed,
                            Message = Explain(ex),
                        });
                }
            });
        }
        catch (Exception ex) when (ex is FileTooLargeException or InvalidDataException)
        {
            return [Card(file, FontStatus.Skipped, ex.Message)];
        }

        // The archive's own card: it produces no family itself, and exists so
        // an unchanged zip can be skipped by size and timestamp next launch.
        cards.Insert(
            0,
            Card(file, FontStatus.Loaded, $"{fonts} fonts inside") with { CachedFile = "", Families = [] });

        return cards;
    }

    /// <summary>
    /// Steps 2 to 6 of the pipeline, on bytes rather than a path, so a font
    /// inside a zip takes exactly the same route as one on disk.
    /// </summary>
    private static FontIndex.Entry PrepareBytes(
        string indexPath,
        FontFileKind kind,
        ArraySegment<byte> bytes,
        long sourceLength,
        DateTime modifiedUtc)
    {
        FontIndex.Entry Fail(FontStatus status, string message) =>
            new(indexPath, sourceLength, modifiedUtc, "", "", [], status == FontStatus.Failed ? message : null)
            {
                Kind = kind,
                Status = status,
                Message = message,
            };

        switch (kind)
        {
            case FontFileKind.Unsupported:
                return Fail(
                    FontStatus.Unsupported,
                    FontTypeDetector.RefusalFor(indexPath) ?? "This file is not a font WPF can use.");

            case FontFileKind.LicenseText:
                return Fail(FontStatus.Skipped, "Licence or readme text, read for FONT_LICENSES.txt.");

            case FontFileKind.Type1Metrics:
                return Fail(
                    FontStatus.Unsupported,
                    "Metrics file only. The outline file (.pfb) is missing.");

            // Phase 3 formats. They are detected and named now so the Font
            // Report tells the truth about them rather than staying silent.
            case FontFileKind.Type1:
                return Fail(FontStatus.Unsupported, "Adobe Type 1 fonts cannot be used yet.");

            case FontFileKind.MacResource:
                return Fail(FontStatus.Unsupported, "Mac font containers cannot be used yet.");

            case FontFileKind.Eot:
                return Fail(FontStatus.Unsupported, "EOT web fonts cannot be used yet.");

            case FontFileKind.Type42:
                return Fail(FontStatus.Unsupported, "Type 42 fonts cannot be used yet.");
        }

        ReadOnlyMemory<byte> sfnt;
        var converted = false;

        try
        {
            switch (kind)
            {
                case FontFileKind.Woff:
                    using (var input = AsStream(bytes))
                    {
                        sfnt = WoffDecoder.ToSfnt(input);
                    }

                    converted = true;
                    break;

                case FontFileKind.Woff2:
                    using (var input = AsStream(bytes))
                    {
                        sfnt = Woff2Decoder.ToSfnt(input);
                    }

                    converted = true;
                    break;

                default:
                    // No conversion: the font goes to the cache exactly as it
                    // is, straight out of the pooled buffer.
                    sfnt = bytes;
                    break;
            }
        }
        catch (Exception ex) when (ex is FontFormatException or FileTooLargeException)
        {
            return Fail(FontStatus.Unsupported, ex.Message);
        }

        FontInspector.Info info;
        try
        {
            // Step 4: validation. FontInspector refuses a font with no head,
            // name or cmap, and no outlines, which is exactly the check the
            // pipeline calls for.
            info = FontInspector.Read(sfnt.Span);
        }
        catch (Exception ex) when (ex is FontFormatException or FileTooLargeException)
        {
            return Fail(FontStatus.Failed, ex.Message);
        }

        if (info.Families.Count == 0)
        {
            return Fail(FontStatus.Failed, "the font has no family name, so nothing could show it");
        }

        var extension = converted
            ? Sfnt.ExtensionFor(info.IsCff ? Sfnt.CffFlavor : Sfnt.TrueTypeFlavor)
            : Path.GetExtension(indexPath).ToLowerInvariant() switch
            {
                ".otf" or ".otc" or ".ttc" or ".ttf" => Path.GetExtension(indexPath).ToLowerInvariant(),
                _ => Sfnt.ExtensionFor(info.IsCff ? Sfnt.CffFlavor : Sfnt.TrueTypeFlavor),
            };

        var cachedFile = FontCache.Store(sfnt.Span, extension);

        return new FontIndex.Entry(
            SourcePath: indexPath,
            Length: sourceLength,
            ModifiedUtc: modifiedUtc,
            Sha1: Path.GetFileNameWithoutExtension(cachedFile),
            CachedFile: cachedFile,
            Families: info.Families,
            FailureReason: null)
        {
            Kind = kind,
            Status = converted ? FontStatus.Converted : FontStatus.Loaded,
            Faces = info.Faces,
            FsType = info.FsType,
            Copyright = info.Copyright,
            LicenseDescription = info.LicenseDescription,
            LicenseUrl = info.LicenseUrl,
            HasLatinLetters = info.HasLatinLetters,
            IsVariable = info.IsVariable,
            GlyphCount = info.GlyphCount,
            Message = converted
                ? $"Converted ({(kind == FontFileKind.Woff ? "WOFF" : "WOFF2")} → OpenType)"
                : "Loaded",
        };
    }

    /// <summary>
    /// Marks the second and later copies of the same family and face as
    /// duplicates (Section 6.6.7), keeping the one in the most directly usable
    /// format.
    /// </summary>
    /// <remarks>
    /// A font bundle downloaded from the web usually holds the .otf, the .woff
    /// and the .woff2 of every font. Without this, the picker shows one name
    /// three times and the cache holds three copies.
    /// </remarks>
    private static void MarkDuplicates(FontIndex index)
    {
        static int Preference(FontFileKind kind) => kind switch
        {
            FontFileKind.Direct => 0,
            FontFileKind.Woff2 => 1,
            FontFileKind.Woff => 2,
            _ => 3,
        };

        var groups = index.Entries
            .Where(e => e.Status is FontStatus.Loaded or FontStatus.Converted or FontStatus.Duplicate)
            .Where(e => e.Families.Count > 0)
            .GroupBy(
                e => (Family: e.Families[0], Face: e.Faces.Count > 0 ? e.Faces[0] : ""),
                comparer: null);

        foreach (var group in groups)
        {
            var ordered = group
                .OrderBy(e => Preference(e.Kind))
                .ThenBy(e => e.SourcePath, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var keeper = ordered[0];

            if (keeper.Status == FontStatus.Duplicate)
            {
                index.Put(keeper with
                {
                    Status = keeper.Kind == FontFileKind.Direct ? FontStatus.Loaded : FontStatus.Converted,
                    Message = "Loaded",
                });
            }

            foreach (var duplicate in ordered.Skip(1))
            {
                index.Put(duplicate with
                {
                    Status = FontStatus.Duplicate,
                    Message = $"Duplicate of {keeper.SourcePath}",
                });
            }
        }
    }

    /// <summary>
    /// Gives the large object heap back after a preparation pass.
    /// </summary>
    /// <remarks>
    /// Normally forcing a collection is the wrong instinct. This is the case
    /// the knob exists for: a single burst that has just read every font in the
    /// folder, is over, and will not happen again this launch. The large object
    /// heap is not compacted by default, so without this a 400 MB font folder
    /// left the app sitting at around 450 MB for the rest of the session -
    /// three times the Section 2.1 budget, for memory holding nothing.
    /// <para>
    /// It runs on the preparation thread, which is below normal priority and
    /// has no deadline, so the pause costs the user nothing.
    /// </para>
    /// </remarks>
    private static void ReleaseMemoryAfterBurst()
    {
        System.Runtime.GCSettings.LargeObjectHeapCompactionMode =
            System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;

        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);

        // Logged because the two numbers mean different things and the
        // difference is the whole question: managed is what is still referenced,
        // committed is what the runtime has not handed back to Windows. A big
        // gap is the runtime keeping empty segments, not a leak.
        var info = GC.GetGCMemoryInfo();

        Log.Info(
            $"after preparing fonts: {GC.GetTotalMemory(false) / (1024 * 1024)} MB managed, "
            + $"{info.TotalCommittedBytes / (1024 * 1024)} MB committed, "
            + $"{info.HeapSizeBytes / (1024 * 1024)} MB heap");
    }

    /// <summary>A stream over a pooled segment, without copying it.</summary>
    private static MemoryStream AsStream(ArraySegment<byte> bytes) =>
        new(bytes.Array!, bytes.Offset, bytes.Count, writable: false);

    private static FontIndex.Entry Card(FontScanner.Found file, FontStatus status, string message) =>
        new(file.RelativePath, file.Length, file.ModifiedUtc, "", "", [],
            status == FontStatus.Failed ? message : null)
        {
            Kind = file.Kind,
            Status = status,
            Message = message,
        };

    private static string Explain(Exception ex) => ex switch
    {
        FontFormatException => ex.Message,
        FileTooLargeException => ex.Message,
        IOException => $"the file could not be read ({ex.Message})",
        UnauthorizedAccessException => "the file could not be opened",
        _ => $"{ex.GetType().Name}: {ex.Message}",
    };
}
