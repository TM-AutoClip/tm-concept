using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using TMConcept.Core.DiskSpace;
using TMConcept.Core.Paths;

namespace TMConcept.Core.Documents;

/// <summary>
/// "Belgelerim" of a profile: adding a file (PDF, Word, text, Markdown), a web page or a Wikipedia article, listing and deleting them, and
/// the meaning search's vectors. Adding: the app keeps its OWN copy, unencrypted, in <c>&lt;data&gt;/documents/&lt;profile&gt;/&lt;sha256&gt;.&lt;ext&gt;</c>
/// (the same content is refused a second time in a profile); its text is read with managed code only, cut into passages
/// (<see cref="DocumentChunker"/>) and saved in the encrypted database; when the meaning model is installed the passages' vectors are made
/// right away (on this computer's processor). A copy whose text could not be read is taken away again. Deleting moves the copy to the
/// recycle bin first (<see cref="IFolderDiscard"/>) and only then deletes the record with its passages and vectors. One add, vector run or
/// delete at a time (a delete waits for a running add), and <see cref="Busy"/> tells the storage manager not to clean up meanwhile (a copy
/// being written has no record yet). A copy that could not be finished (canceled, a failed copy) is taken away again.
/// </summary>
public sealed class DocumentLibrary(IAppPaths paths, IDocumentStore store, IEnumerable<IDocumentExtractor> extractors, IEnumerable<IDocumentFetcher> fetchers,
    IDocumentEmbedder embedder, TimeProvider clock, ILogger<DocumentLibrary> logger)
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

    /// <summary>Adds a file to the profile's documents.</summary>
    public async Task<DocumentAddResult> AddFileAsync(Guid profileId, string file, IProgress<DocumentProgress>? progress, CancellationToken cancellationToken)
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

        if (info.Length > DocumentLimits.FileBytesMax)
        {
            throw new DocumentRejectedException("size", $"The file is larger than {DocumentLimits.FileBytesMax >> 20} MB.", name);
        }

        progress?.Report(new DocumentProgress(name, DocumentStage.Reading));
        string sha;
        await using (var stream = File.OpenRead(file))
        {
            sha = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
        }

        var document = new SourceDocument
        {
            Kind = extractor.Kind,
            Title = Path.GetFileNameWithoutExtension(file),
            OriginalName = name,
            Bytes = info.Length,
        };
        return await AddAsync(profileId, sha, extension, (target, token) => CopyAsync(file, target, token), extractor, document, progress, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Adds a web page (<see cref="DocumentKind.Web"/>) or a Wikipedia article (<see cref="DocumentKind.Wikipedia"/>, by address or title in <paramref name="language"/>).</summary>
    public async Task<DocumentAddResult> AddWebAsync(Guid profileId, DocumentKind kind, string address, string language, IProgress<DocumentProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(address);
        var fetcher = _fetchers.FirstOrDefault(f => f.Kind == kind) ?? throw new ArgumentException($"Nothing fetches {kind}.", nameof(kind));
        progress?.Report(new DocumentProgress(address.Trim(), DocumentStage.Reading));
        var fetched = await fetcher.FetchAsync(address.Trim(), language ?? string.Empty, cancellationToken).ConfigureAwait(false);
        var extractor = _extractors.FirstOrDefault(e => e.Extensions.Contains(fetched.Extension, StringComparer.Ordinal))
            ?? throw new DocumentRejectedException("not_text", "The address holds nothing that can be read.", address);
        var document = new SourceDocument
        {
            Kind = kind,
            Title = fetched.Title.Length > 0 ? fetched.Title : fetched.Url,
            Url = fetched.Url,
            Language = fetched.Language,
            Bytes = fetched.Content.Length,
        };
        var sha = Convert.ToHexStringLower(SHA256.HashData(fetched.Content));
        return await AddAsync(profileId, sha, fetched.Extension, (target, token) => File.WriteAllBytesAsync(target, fetched.Content, token), extractor, document,
            progress, cancellationToken).ConfigureAwait(false);
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

    private async Task<DocumentAddResult> AddAsync(Guid profileId, string sha, string extension, Func<string, CancellationToken, Task> write,
        IDocumentExtractor extractor, SourceDocument document, IProgress<DocumentProgress>? progress, CancellationToken cancellationToken)
    {
        var name = document.OriginalName.Length > 0 ? document.OriginalName : document.Title;
        await EnterAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (await store.FindByContentAsync(profileId, sha, cancellationToken).ConfigureAwait(false) is { } existing)
            {
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
            try
            {
                progress?.Report(new DocumentProgress(name, DocumentStage.Extracting));
                ExtractedDocument extracted;
                try
                {
                    extracted = await extractor.ExtractAsync(target, cancellationToken).ConfigureAwait(false);
                }
                catch (InvalidDataException ex)
                {
                    throw new DocumentRejectedException("unreadable", ex.Message, name, ex);
                }

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

            logger.LogInformation("Document {Id} added: {Kind}, {Pages} pages ({NoText} without text), {Characters} characters, {Chunks} passages, {Status}",
                document.Id, document.Kind, document.Pages, document.PagesWithoutText, document.Characters, document.Chunks, document.Status);
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
            return new DocumentAddResult(document, error);
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
