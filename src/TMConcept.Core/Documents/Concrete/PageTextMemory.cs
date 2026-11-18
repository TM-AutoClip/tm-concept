namespace TMConcept.Core.Documents;

/// <summary>
/// <see cref="IPageTextMemory"/> over <see cref="IDocumentPageStore"/> (0.1.25): the kept pages of the asked range are loaded once; text-layer
/// pages are saved in batches of <see cref="Batch"/> (cheap to read again), a page an engine read is saved at once with the batch before it.
/// </summary>
public sealed class PageTextMemory : IPageTextMemory
{
    /// <summary>Text-layer pages saved together.</summary>
    public const int Batch = 64;

    private readonly IDocumentPageStore _store;
    private readonly Guid _profileId;
    private readonly string _fileSha256;
    private readonly TimeProvider _clock;
    private readonly Dictionary<int, DocumentPageText> _kept;
    private readonly List<DocumentPageText> _waiting = [];

    private PageTextMemory(IDocumentPageStore store, Guid profileId, string fileSha256, TimeProvider clock, IEnumerable<DocumentPageText> kept)
    {
        _store = store;
        _profileId = profileId;
        _fileSha256 = fileSha256;
        _clock = clock;
        _kept = kept.ToDictionary(p => p.Page);
    }

    /// <summary>Pages found kept so far (taken without reading them again).</summary>
    public int Reused { get; private set; }

    /// <summary>The memory of pages <paramref name="from"/>–<paramref name="to"/> of the profile's file <paramref name="fileSha256"/>.</summary>
    public static async Task<PageTextMemory> LoadAsync(IDocumentPageStore store, Guid profileId, string fileSha256, int from, int to, TimeProvider clock,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentException.ThrowIfNullOrEmpty(fileSha256);
        var kept = await store.PagesAsync(profileId, fileSha256, from, to, cancellationToken).ConfigureAwait(false);
        return new PageTextMemory(store, profileId, fileSha256, clock ?? TimeProvider.System, kept);
    }

    /// <inheritdoc />
    public DocumentPageText? Find(int page)
    {
        if (!_kept.TryGetValue(page, out var kept))
        {
            return null;
        }

        Reused++;
        return kept;
    }

    /// <inheritdoc />
    public async Task KeepAsync(int page, string text, string source, CancellationToken cancellationToken)
    {
        var kept = new DocumentPageText
        {
            ProfileId = _profileId,
            FileSha256 = _fileSha256,
            Page = page,
            Text = text ?? string.Empty,
            Source = source ?? PageTextSources.Unknown,
            ReadAtUtc = _clock.GetUtcNow(),
        };
        _kept[page] = kept;
        _waiting.Add(kept);
        if (kept.Source != PageTextSources.Layer || _waiting.Count >= Batch)
        {
            await FlushAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task FlushAsync(CancellationToken cancellationToken)
    {
        if (_waiting.Count == 0)
        {
            return;
        }

        var pages = _waiting.ToList();
        await _store.SavePagesAsync(pages, cancellationToken).ConfigureAwait(false);
        _waiting.Clear();
    }
}
