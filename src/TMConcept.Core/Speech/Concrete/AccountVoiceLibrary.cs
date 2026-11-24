using Microsoft.Extensions.Logging;
using TMConcept.Core.Keys;
using TMConcept.Core.Providers;

namespace TMConcept.Core.Speech;

/// <summary>
/// The voices of the user's account at online providers (0.1.47, H4: ElevenLabs), next to the engine's fixed list. Nothing is read at the
/// start (the asynchronous rule): <see cref="LoadAsync"/> reads the saved lists from the database when a voice picker is first shown, and
/// <see cref="RefreshAsync"/> asks the provider again (when a key is saved, when a picker shows the provider and the last read is older
/// than <see cref="RefreshEvery"/>, and on "Refresh voices"). A read that fails keeps what was there (the last saved list, or none: then
/// only the fixed list is offered) and says so as a warning, which also reaches the activity panel.
/// </summary>
public sealed class AccountVoiceLibrary(IAccountVoiceCache cache, IEnumerable<IAccountVoiceSource> sources, IApiKeyStore keys, TimeProvider clock,
    ILogger<AccountVoiceLibrary> logger)
{
    /// <summary>How old the last read may be before a shown picker reads the account again by itself.</summary>
    public static readonly TimeSpan RefreshEvery = TimeSpan.FromMinutes(10);

    private readonly IReadOnlyList<IAccountVoiceSource> _sources = sources.ToList();
    private readonly Dictionary<string, IReadOnlyList<AccountVoice>> _voices = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTimeOffset> _tried = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();

    // Never disposed (both): without AvailableWaitHandle they hold nothing unmanaged (the library lives as long as the app).
    private readonly SemaphoreSlim _reading = new(1, 1);
    private readonly SemaphoreSlim _loading = new(1, 1);
    private volatile bool _loaded;

    /// <summary>Raised (on any thread) with the provider code when its list changed.</summary>
    public event EventHandler<string>? Changed;

    /// <summary>Whether <paramref name="provider"/> lists account voices.</summary>
    public bool Supports(string provider) => _sources.Any(s => s.Provider == provider);

    /// <summary>The account voices of <paramref name="provider"/> as last read (empty before <see cref="LoadAsync"/> or without a list).</summary>
    public IReadOnlyList<AccountVoice> Voices(string provider)
    {
        lock (_gate)
        {
            return _voices.TryGetValue(provider, out var list) ? list : [];
        }
    }

    /// <summary>
    /// Whether a shown picker should read <paramref name="provider"/> again (never tried in this run, or longer ago than
    /// <see cref="RefreshEvery"/>).
    /// </summary>
    public bool IsDue(string provider)
    {
        lock (_gate)
        {
            return !_tried.TryGetValue(provider, out var at) || clock.GetUtcNow() - at >= RefreshEvery;
        }
    }

    /// <summary>Reads the saved lists once per run (database work, off the window's thread by the database layer).</summary>
    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        if (_loaded)
        {
            return;
        }

        await _loading.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_loaded)
            {
                return;
            }

            foreach (var source in _sources)
            {
                // A list read from the account meanwhile is newer than the saved one.
                if (await cache.GetAsync(source.Provider, cancellationToken).ConfigureAwait(false) is { } saved && Voices(source.Provider).Count == 0)
                {
                    Set(source.Provider, saved);
                }
            }

            _loaded = true;
        }
        finally
        {
            _loading.Release();
        }
    }

    /// <summary>
    /// Reads the account voices of <paramref name="provider"/> with its key (the last that passed its test, else the first) and saves them.
    /// Never throws for a provider failure: the result says why and the list stays as it was.
    /// </summary>
    public async Task<AccountVoiceRefresh> RefreshAsync(string provider, CancellationToken cancellationToken)
    {
        if (_sources.FirstOrDefault(s => s.Provider == provider) is not { } source)
        {
            return new AccountVoiceRefresh(false, 0, "provider");
        }

        await _reading.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            lock (_gate)
            {
                _tried[provider] = clock.GetUtcNow();
            }

            var all = await keys.ListAsync(provider, cancellationToken).ConfigureAwait(false);
            var key = all.Where(k => k.LastOkAtUtc is not null).MaxBy(k => k.LastOkAtUtc) ?? all.FirstOrDefault();
            if (key is null)
            {
                logger.LogInformation("Account voices of {Provider}: no API key, only the fixed voices are offered", provider);
                return new AccountVoiceRefresh(false, 0, "key");
            }

            IReadOnlyList<AccountVoice> voices;
            try
            {
                voices = await source.FetchAsync(key, cancellationToken).ConfigureAwait(false);
            }
            catch (ProviderException ex)
            {
                logger.LogWarning("The voices of the {Provider} account could not be read ({Code}); the fixed voices{Saved} are offered", provider, ex.Code,
                    Voices(provider).Count > 0 ? " and the last saved list" : string.Empty);
                return new AccountVoiceRefresh(false, 0, ex.Code);
            }

            await cache.SaveAsync(provider, voices, cancellationToken).ConfigureAwait(false);
            Set(provider, voices);
            logger.LogInformation("Account voices of {Provider} read: {Count}", provider, voices.Count);
            return new AccountVoiceRefresh(true, voices.Count, "ok");
        }
        finally
        {
            _reading.Release();
        }
    }

    private void Set(string provider, IReadOnlyList<AccountVoice> voices)
    {
        lock (_gate)
        {
            _voices[provider] = voices;
        }

        Changed?.Invoke(this, provider);
    }
}
