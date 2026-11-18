using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using TMConcept.Core.DiskSpace;
using TMConcept.Core.Paths;

namespace TMConcept.Core.Documents;

/// <summary>
/// "Belgelerim" of a profile: adding a file (PDF, Word, text, Markdown), a web page or a Wikipedia article (or what research fetched,
/// <see cref="AddFetchedAsync"/>), listing and deleting them, and
/// the meaning search's vectors. Adding: the app keeps its OWN copy, unencrypted, in <c>&lt;data&gt;/documents/&lt;profile&gt;/&lt;sha256&gt;.&lt;ext&gt;</c>
/// (the same content is refused a second time in a profile); its text is read with managed code only, cut into passages
/// (<see cref="DocumentChunker"/>) and saved in the encrypted database; when the meaning model is installed the passages' vectors are made
/// right away (on this computer's processor). A copy whose text could not be read is taken away again. Deleting moves the copy to the
/// recycle bin first (<see cref="IFolderDiscard"/>) and only then deletes the record with its passages and vectors. One add, vector run or
/// delete at a time (a delete waits for a running add), and <see cref="Busy"/> tells the storage manager not to clean up meanwhile (a copy
/// being written has no record yet). A copy that could not be finished (canceled, a failed copy) is taken away again.
/// 0.1.25: the text of every page of a PDF (its text layer, or what the local OCR or a vision model read) is kept with the file
/// (<see cref="IDocumentPageStore"/>, under the whole file's SHA-256) as it is read; another page range of the same file, an add again after a
/// cancel and <see cref="ReadMissingAsync"/> take the kept pages instead of reading them again; <see cref="PageStatesAsync"/> tells which pages
/// of each document are ready or still to read. The kept pages go with the last document of their file, or with the profile.
/// </summary>
public sealed class DocumentLibrary(IAppPaths paths, IDocumentStore store, IEnumerable<IDocumentExtractor> extractors, IEnumerable<IDocumentFetcher> fetchers,
    IDocumentEmbedder embedder, TimeProvider clock, ILogger<DocumentLibrary> logger, IDocumentPageStore pageStore)
{
    /// <summary>Passages embedded per model call.</summary>
    public const int EmbedBatch = 16;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private int _busy;
    private readonly IReadOnlyList<IDocumentExtractor> _extractors = [.. extractors];
    private readonly IReadOnlyList<IDocumentFetcher> _fetchers = [.. fetchers];

    /// <summary>The folder of the copies.</summary>
    public string Folder => Path.Combine(paths.DataDirectory, "documents");

    /// <summary>The file extensions that can be added (".docx", ".md", ".pdf", ".txt").</summary>
    public IReadOnlyList<string> Extensions => _extractors.SelectMany(e => e.Extensions).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();

    /// <summary>The meaning model.</summary>
    public IDocumentEmbedder Embedder => embedder;

    /// <summary>Whether an add, a vector run or a delete is going on (the storage manager then removes nothing).</summary>
    public bool Busy => Volatile.Read(ref _busy) > 0;

    /// <summary>The documents of a profile, the newest first.</summary>
    public Task<IReadOnlyList<SourceDocument>> ListAsync(Guid profileId, CancellationToken cancellationToken) => store.ListAsync(profileId, cancellationToken);

    /// <summary>The full path of a document's copy (never outside <see cref="Folder"/>).</summary>
    public string FileOf(SourceDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return LibraryPaths.Resolve(Folder, document.StoredFile);
    }

    /// <summary>Adds a file to the profile's documents (every page, no text extraction).</summary>
    public Task<DocumentAddResult> AddFileAsync(Guid profileId, string file, IProgress<DocumentProgress>? progress, CancellationToken cancellationToken) =>
        AddFileAsync(profileId, file, DocumentReadOptions.All, progress, cancellationToken);

    /// <summary>
    /// Adds a file to the profile's documents as <paramref name="read"/> says (0.1.24): only a page range of a PDF, and the text of its scanned
    /// pages read by a text-extraction model. A page range is a document of its own: its title names the pages and it may be added next to
    /// the whole file or another range of it (the same range twice is refused like the same file twice).
    /// </summary>
    public async Task<DocumentAddResult> AddFileAsync(Guid profileId, string file, DocumentReadOptions read, IProgress<DocumentProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(read);
        var extractor = Extractor(file);
        var name = Path.GetFileName(file);
        var extension = Path.GetExtension(file).ToLowerInvariant();
        var info = new FileInfo(file);
        progress?.Report(new DocumentProgress(name, DocumentStage.Reading));
        string sha;
        await using (var stream = File.OpenRead(file))
        {
            sha = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
        }

        var title = Path.GetFileNameWithoutExtension(file);
        var fileSha = sha;
        if (read.Pages is { } pages)
        {
            // The range's own key: the copy is named by it, and the same range of the same file is found again.
            sha = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(sha + ":pages:" + pages)));
            title = DocumentTitle(title + " (p. " + pages.From.ToString(CultureInfo.InvariantCulture) + "–" + pages.To.ToString(CultureInfo.InvariantCulture) + ")");
        }

        var document = new SourceDocument
        {
            Kind = extractor.Kind,
            Title = title,
            OriginalName = name,
            Bytes = info.Length,
            FileSha256 = extractor.Kind == DocumentKind.Pdf ? fileSha : string.Empty,
            FirstPage = extractor.Kind == DocumentKind.Pdf ? read.Pages?.From ?? 1 : 0,
        };
        if (extractor.Kind == DocumentKind.Pdf && read.Memory is null)
        {
            // The pages this file had read before (another range, an add that was canceled) are taken, the new ones kept.
            read = read with
            {
                Memory = await PageTextMemory.LoadAsync(pageStore, profileId, fileSha, read.Pages?.From ?? 1, read.Pages?.To ?? int.MaxValue, clock, cancellationToken)
                    .ConfigureAwait(false),
            };
        }

        return await AddAsync(profileId, sha, extension, (target, token) => CopyAsync(file, target, token), extractor, document, progress, cancellationToken,
            read: read).ConfigureAwait(false);
    }

    /// <summary>
    /// A quick look at a file before it is added (0.1.24): its pages and how many of a few sampled pages have no text. The same refusals as an
    /// add (<see cref="DocumentRejectedException"/>: <c>type</c>, <c>size</c>, <c>unreadable</c>).
    /// </summary>
    public async Task<DocumentProbe> ProbeAsync(string file, CancellationToken cancellationToken)
    {
        var extractor = Extractor(file);
        try
        {
            return await extractor.ProbeAsync(file, cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidDataException ex)
        {
            throw new DocumentRejectedException("unreadable", ex.Message, Path.GetFileName(file), ex);
        }
    }

    /// <summary>Adds a web page (<see cref="DocumentKind.Web"/>) or a Wikipedia article (<see cref="DocumentKind.Wikipedia"/>, by address or title in <paramref name="language"/>).</summary>
    public async Task<DocumentAddResult> AddWebAsync(Guid profileId, DocumentKind kind, string address, string language, IProgress<DocumentProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(address);
        var fetcher = _fetchers.FirstOrDefault(f => f.Kind == kind) ?? throw new ArgumentException($"Nothing fetches {kind}.", nameof(kind));
        progress?.Report(new DocumentProgress(address.Trim(), DocumentStage.Reading));
        var fetched = await fetcher.FetchAsync(address.Trim(), language ?? string.Empty, cancellationToken).ConfigureAwait(false);
        return await AddFetchedAsync(profileId, fetched with { Kind = kind }, reuse: false, progress, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Adds what was already fetched (a research page, 0.1.22; a web page or article of <see cref="AddWebAsync"/>). With
    /// <paramref name="reuse"/> the profile's document of the same content is given back instead of being refused
    /// (<see cref="DocumentAddResult.Existed"/>), so research finds again what it saved before.
    /// </summary>
    public async Task<DocumentAddResult> AddFetchedAsync(Guid profileId, FetchedDocument fetched, bool reuse, IProgress<DocumentProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(fetched);
        var extractor = _extractors.FirstOrDefault(e => e.Extensions.Contains(fetched.Extension, StringComparer.Ordinal))
            ?? throw new DocumentRejectedException("not_text", "The address holds nothing that can be read.", fetched.Url);
        var document = new SourceDocument
        {
            Kind = fetched.Kind,
            Title = DocumentTitle(fetched.Title.Length > 0 ? fetched.Title : fetched.Url),
            Url = fetched.Url,
            Language = fetched.Language,
            Bytes = fetched.Content.Length,
        };
        var sha = Convert.ToHexStringLower(SHA256.HashData(fetched.Content));
        return await AddAsync(profileId, sha, fetched.Extension, (target, token) => File.WriteAllBytesAsync(target, fetched.Content, token), extractor, document,
            progress, cancellationToken, reuse).ConfigureAwait(false);
    }

    /// <summary>
    /// Deletes a document: its copy goes through <paramref name="discard"/> (the recycle bin) first, and only when that worked are its record,
    /// passages and vectors deleted. A copy that is already gone is nothing to do.
    /// </summary>
    public async Task DeleteAsync(SourceDocument document, IFolderDiscard discard, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(discard);
        var file = document.StoredFile.Length > 0 ? FileOf(document) : string.Empty;
        await EnterAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (file.Length > 0 && File.Exists(file))
            {
                await discard.DiscardAsync(file, cancellationToken).ConfigureAwait(false);
            }

            await store.DeleteAsync(document.Id, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Exit();
        }

        logger.LogInformation("Document {Id} ({Kind}, {Chunks} passages) deleted{Bin}", document.Id, document.Kind, document.Chunks,
            discard.Permanent ? " for good" : ", its copy went to the recycle bin");
    }

    /// <summary>
    /// Sends the copies of a deleted profile's documents (its folder) through <paramref name="discard"/> (the recycle bin): the database
    /// deletes their records and passages with the profile, and the plain-text copies of a deleted channel should not stay readable on disk.
    /// Nothing to do when the profile had none. Waits for a running add or vector run.
    /// </summary>
    public async Task DiscardProfileAsync(Guid profileId, IFolderDiscard discard, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(discard);
        var folder = LibraryPaths.Resolve(Folder, profileId.ToString("N"));
        await EnterAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!Directory.Exists(folder))
            {
                return;
            }

            await discard.DiscardAsync(folder, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Exit();
        }

        logger.LogInformation("The document copies of a deleted profile were {Where}", discard.Permanent ? "deleted for good" : "moved to the recycle bin");
    }

    /// <summary>
    /// Makes the meaning search's vectors of every passage that has none yet (after the meaning model was installed). Nothing without the
    /// model. Returns how many were made; a failure goes up (the vectors made before it stay).
    /// </summary>
    public async Task<int> EmbedMissingAsync(IProgress<DocumentProgress>? progress, CancellationToken cancellationToken)
    {
        if (!embedder.Ready)
        {
            return 0;
        }

        await EnterAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var missing = await store.ChunksWithoutVectorAsync(embedder.ModelId, cancellationToken).ConfigureAwait(false);
            await EmbedAsync(missing, string.Empty, progress, cancellationToken).ConfigureAwait(false);
            if (missing.Count > 0)
            {
                logger.LogInformation("Documents: {Count} passages got their meaning-search vectors", missing.Count);
            }

            return missing.Count;
        }
        finally
        {
            Exit();
        }
    }

    /// <summary>
    /// Which pages of each PDF document of the profile (added since 0.1.25) are ready (kept: a text layer or an engine's reading) and which are
    /// still to read; <see cref="DocumentPageState.Unknown"/> for the others. Reads no text, only which pages are kept.
    /// </summary>
    public async Task<IReadOnlyDictionary<Guid, DocumentPageState>> PageStatesAsync(Guid profileId, IReadOnlyList<SourceDocument> documents,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(documents);
        var marks = (await pageStore.MarksAsync(profileId, cancellationToken).ConfigureAwait(false))
            .GroupBy(m => m.FileSha256, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToDictionary(m => m.Page), StringComparer.Ordinal);
        var states = new Dictionary<Guid, DocumentPageState>();
        foreach (var document in documents)
        {
            if (document.Kind != DocumentKind.Pdf || document.FileSha256.Length == 0 || document.FirstPage <= 0 || document.Pages <= 0)
            {
                states[document.Id] = DocumentPageState.Unknown;
                continue;
            }

            var kept = marks.GetValueOrDefault(document.FileSha256) ?? [];
            var pages = Enumerable.Range(document.FirstPage, document.Pages).ToList();
            var ready = pages.Where(kept.ContainsKey).Select(p => kept[p]).ToList();
            states[document.Id] = new DocumentPageState(document.Pages, ready.Count, ready.Count(m => m.Source != PageTextSources.Layer),
                pages.Where(p => !kept.ContainsKey(p)).ToList(), ready.Sum(m => (long)m.Characters));
        }

        return states;
    }

    /// <summary>
    /// Reads the pages of a PDF document that are still to read (0.1.25: scanned pages never read, or left out by a cancel or a stop) with
    /// <paramref name="read"/>'s engine; the pages read before are taken as kept, so only the missing ones are read (and paid for). The
    /// document's passages are then cut again from all its pages and replace the old ones (their vectors made again when the meaning model is
    /// installed). <see cref="DocumentRejectedException"/> (<c>unreadable</c>) when its copy is gone or it is not such a document.
    /// </summary>
    public async Task<DocumentAddResult> ReadMissingAsync(SourceDocument document, DocumentReadOptions read, IProgress<DocumentProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(read);
        var name = document.Title;
        var file = document.StoredFile.Length > 0 ? FileOf(document) : string.Empty;
        var extractor = _extractors.FirstOrDefault(e => e.Kind == document.Kind);
        if (document.Kind != DocumentKind.Pdf || document.FileSha256.Length == 0 || document.FirstPage <= 0 || extractor is null || !File.Exists(file))
        {
            throw new DocumentRejectedException("unreadable", "Its copy is gone, or its pages were not kept (added before 0.1.25).", name);
        }

        await EnterAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var range = new DocumentPageRange(document.FirstPage, document.FirstPage + Math.Max(1, document.Pages) - 1);
            var memory = await PageTextMemory.LoadAsync(pageStore, document.ProfileId, document.FileSha256, range.From, range.To, clock, cancellationToken)
                .ConfigureAwait(false);
            progress?.Report(new DocumentProgress(name, DocumentStage.Extracting));
            ExtractedDocument extracted;
            try
            {
                extracted = await extractor.ExtractAsync(file, read with { Pages = range, Memory = memory, Progress = progress, Name = name }, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (InvalidDataException ex)
            {
                throw new DocumentRejectedException("unreadable", ex.Message, name, ex);
            }

            progress?.Report(new DocumentProgress(name, DocumentStage.Chunking));
            var chunks = DocumentChunker.Chunk(extracted);
            foreach (var chunk in chunks)
            {
                chunk.DocumentId = document.Id;
                chunk.ProfileId = document.ProfileId;
            }

            document.PagesWithoutText = extracted.PagesWithoutText;
            document.Characters = extracted.Characters;
            document.Chunks = chunks.Count;
            document.Status = chunks.Count == 0 ? DocumentStatus.NoText : DocumentStatus.Ready;
            document.StatusDetail = chunks.Count == 0 ? "no_text" : DocumentExtractorBase.IsCut(extracted) ? "cut" : string.Empty;
            document.ChunkerVersion = DocumentLimits.ChunkerVersion;
            await pageStore.ReplaceAsync(document, chunks, cancellationToken).ConfigureAwait(false);
            logger.LogInformation("Document {Id}: missing pages read ({Recognized} read by a text-extraction model, {Unread} unread, {Reused} kept from before), "
                + "{Chunks} passages now", document.Id, extracted.PagesRecognized, extracted.PagesUnread, extracted.PagesReused, chunks.Count);
            var error = string.Empty;
            try
            {
                await EmbedAsync(chunks, name, progress, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or InvalidDataException or UnauthorizedAccessException)
            {
                logger.LogError(ex, "Document {Id}: the meaning-search vectors could not be made", document.Id);
                error = ex.Message;
            }

            progress?.Report(new DocumentProgress(name, DocumentStage.Done));
            return new DocumentAddResult(document, error)
            {
                PagesRecognized = extracted.PagesRecognized, PagesUnread = extracted.PagesUnread, RecognitionStopped = extracted.RecognitionStopped,
                PagesReused = extracted.PagesReused, PagesUnsupported = extracted.PagesUnsupported,
            };
        }
        finally
        {
            Exit();
        }
    }

    private static string DocumentTitle(string title) => title.Length <= DocumentLimits.TitleMax ? title : title[..DocumentLimits.TitleMax].TrimEnd();

    /// <summary>The reader of <paramref name="file"/>'s kind; refused when there is none, the file is not there or it is larger than the kind allows.</summary>
    private IDocumentExtractor Extractor(string file)
    {
        ArgumentException.ThrowIfNullOrEmpty(file);
        var name = Path.GetFileName(file);
        var extension = Path.GetExtension(file).ToLowerInvariant();
        var extractor = _extractors.FirstOrDefault(e => e.Extensions.Contains(extension, StringComparer.Ordinal))
            ?? throw new DocumentRejectedException("type", "Only PDF, Word (.docx), text and Markdown files can be added.", name);
        var info = new FileInfo(file);
        if (!info.Exists)
        {
            throw new FileNotFoundException("The file is not there.", file);
        }

        if (info.Length > extractor.MaxBytes)
        {
            throw new DocumentRejectedException("size", $"The file is larger than {extractor.MaxBytes >> 20} MB.", name);
        }

        return extractor;
    }

    private async Task<DocumentAddResult> AddAsync(Guid profileId, string sha, string extension, Func<string, CancellationToken, Task> write,
        IDocumentExtractor extractor, SourceDocument document, IProgress<DocumentProgress>? progress, CancellationToken cancellationToken, bool reuse = false,
        DocumentReadOptions? read = null)
    {
        var name = document.OriginalName.Length > 0 ? document.OriginalName : document.Title;
        await EnterAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (await store.FindByContentAsync(profileId, sha, cancellationToken).ConfigureAwait(false) is { } existing)
            {
                if (reuse)
                {
                    progress?.Report(new DocumentProgress(name, DocumentStage.Done));
                    return new DocumentAddResult(existing, string.Empty) { Existed = true };
                }

                throw new DocumentRejectedException("duplicate", "The profile has this document already.", existing.Title);
            }

            var relative = profileId.ToString("N") + "/" + sha + extension;
            var target = LibraryPaths.Resolve(Folder, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            var temporary = target + ".part";
            try
            {
                await write(temporary, cancellationToken).ConfigureAwait(false);
                File.Move(temporary, target, overwrite: true);
            }
            catch
            {
                // A canceled or failed copy leaves no plain-text part behind (the error itself goes up).
                File.Delete(temporary);
                throw;
            }

            IReadOnlyList<DocumentChunk> chunks;
            var recognized = 0;
            var unread = 0;
            var reused = 0;
            var unsupported = 0;
            var halted = string.Empty;
            try
            {
                progress?.Report(new DocumentProgress(name, DocumentStage.Extracting));
                ExtractedDocument extracted;
                try
                {
                    extracted = await extractor.ExtractAsync(target, (read ?? DocumentReadOptions.All) with { Progress = progress, Name = name }, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (InvalidDataException ex)
                {
                    throw new DocumentRejectedException("unreadable", ex.Message, name, ex);
                }

                recognized = extracted.PagesRecognized;
                unread = extracted.PagesUnread;
                reused = extracted.PagesReused;
                unsupported = extracted.PagesUnsupported;
                halted = extracted.RecognitionStopped;
                progress?.Report(new DocumentProgress(name, DocumentStage.Chunking));
                chunks = DocumentChunker.Chunk(extracted);
                document.ProfileId = profileId;
                document.StoredFile = relative;
                document.Sha256 = sha;
                document.Pages = extracted.Pages;
                document.PagesWithoutText = extracted.PagesWithoutText;
                document.Characters = extracted.Characters;
                document.Chunks = chunks.Count;
                document.Status = chunks.Count == 0 ? DocumentStatus.NoText : DocumentStatus.Ready;
                document.StatusDetail = chunks.Count == 0 ? "no_text" : DocumentExtractorBase.IsCut(extracted) ? "cut" : string.Empty;
                document.ChunkerVersion = DocumentLimits.ChunkerVersion;
                document.AddedAtUtc = clock.GetUtcNow();
                foreach (var chunk in chunks)
                {
                    chunk.DocumentId = document.Id;
                    chunk.ProfileId = profileId;
                }

                await store.AddAsync(document, chunks, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // Nothing of a document that could not be added stays behind (the error itself goes up).
                File.Delete(target);
                throw;
            }

            logger.LogInformation("Document {Id} added: {Kind}, {Pages} pages ({NoText} without text, {Recognized} read by a text-extraction model, {Unread} unread, "
                + "{Reused} kept from before), {Characters} characters, {Chunks} passages, {Status}", document.Id, document.Kind, document.Pages, document.PagesWithoutText,
                recognized, unread, reused, document.Characters, document.Chunks, document.Status);
            var error = string.Empty;
            try
            {
                await EmbedAsync(chunks, name, progress, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or InvalidDataException or UnauthorizedAccessException)
            {
                // The document is saved and searchable by its words; the user is told why the meaning search does not cover it yet.
                logger.LogError(ex, "Document {Id}: the meaning-search vectors could not be made", document.Id);
                error = ex.Message;
            }

            progress?.Report(new DocumentProgress(name, DocumentStage.Done));
            return new DocumentAddResult(document, error)
            {
                PagesRecognized = recognized, PagesUnread = unread, RecognitionStopped = halted, PagesReused = reused, PagesUnsupported = unsupported,
            };
        }
        finally
        {
            Exit();
        }
    }

    private async Task EnterAsync(CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _busy);
        try
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            Interlocked.Decrement(ref _busy);
            throw;
        }
    }

    private void Exit()
    {
        _gate.Release();
        Interlocked.Decrement(ref _busy);
    }

    /// <summary>Embeds <paramref name="chunks"/> in batches and saves each batch at once (a cancel keeps what was made).</summary>
    private async Task EmbedAsync(IReadOnlyList<DocumentChunk> chunks, string name, IProgress<DocumentProgress>? progress, CancellationToken cancellationToken)
    {
        if (!embedder.Ready || chunks.Count == 0)
        {
            return;
        }

        for (var at = 0; at < chunks.Count; at += EmbedBatch)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new DocumentProgress(name, DocumentStage.Embedding, at, chunks.Count));
            var batch = chunks.Skip(at).Take(EmbedBatch).ToList();
            var vectors = await embedder.EmbedPassagesAsync(batch.Select(c => (c.Heading.Length > 0 ? c.Heading + "\n" : string.Empty) + c.Text).ToList(),
                cancellationToken).ConfigureAwait(false);
            if (vectors.Count != batch.Count)
            {
                throw new InvalidOperationException("The meaning model gave a different number of vectors than passages.");
            }

            await store.SaveVectorsAsync(batch.Select((c, i) => new DocumentChunkVector
            {
                ChunkId = c.Id,
                Model = embedder.ModelId,
                ProfileId = c.ProfileId,
                Dimensions = vectors[i].Length,
                Vector = DocumentVectors.ToBytes(vectors[i]),
            }).ToList(), CancellationToken.None).ConfigureAwait(false);
        }

        progress?.Report(new DocumentProgress(name, DocumentStage.Embedding, chunks.Count, chunks.Count));
    }

    private static async Task CopyAsync(string source, string target, CancellationToken cancellationToken)
    {
        await using var input = File.OpenRead(source);
        await using var output = File.Create(target);
        await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
    }
}
