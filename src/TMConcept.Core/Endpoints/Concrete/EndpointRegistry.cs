using Microsoft.Extensions.Logging;

namespace TMConcept.Core.Endpoints;

/// <summary>
/// The saved local servers in memory (the model catalog reads them synchronously) over <see cref="IEndpointStore"/>: loaded after unlock,
/// replaced in one step on every save or delete, and <see cref="Changed"/> is raised so the catalog rebuilds and the model pickers show the
/// new models when a page is opened next. Never logs a token.
/// </summary>
public sealed class EndpointRegistry(IEndpointStore store, ILogger<EndpointRegistry> logger)
{
    private volatile IReadOnlyList<LocalEndpoint> _all = [];

    /// <summary>Raised after the list changed (loaded, saved, deleted).</summary>
    public event EventHandler? Changed;

    /// <summary>Every saved endpoint (empty until <see cref="LoadAsync"/>).</summary>
    public IReadOnlyList<LocalEndpoint> All => _all;

    /// <summary>An endpoint by id; null when unknown (deleted).</summary>
    public LocalEndpoint? Find(Guid id) => _all.FirstOrDefault(e => e.Id == id);

    /// <summary>Reads the saved endpoints (after unlock, after a restore).</summary>
    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        _all = await store.ListAsync(cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Local servers: {Count} saved ({Models} models)", _all.Count, _all.Sum(e => e.Models.Count));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Adds or replaces an endpoint.</summary>
    public async Task SaveAsync(LocalEndpoint endpoint, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        await store.SaveAsync(endpoint, cancellationToken).ConfigureAwait(false);
        _all = [.. _all.Where(e => e.Id != endpoint.Id), endpoint];
        logger.LogInformation("Local server saved: {Name} with {Models} model(s), token {Token}", endpoint.Name, endpoint.Models.Count,
            endpoint.Token.Length > 0 ? "set" : "none");
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Deletes an endpoint; step choices of its models show as no longer offered.</summary>
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        await store.DeleteAsync(id, cancellationToken).ConfigureAwait(false);
        _all = [.. _all.Where(e => e.Id != id)];
        logger.LogInformation("Local server deleted");
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
