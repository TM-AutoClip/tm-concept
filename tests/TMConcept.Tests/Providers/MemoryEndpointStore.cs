using TMConcept.Core.Endpoints;

namespace TMConcept.Tests.Providers;

/// <summary>Saved local servers in memory (provider tests need no database).</summary>
internal sealed class MemoryEndpointStore : IEndpointStore
{
    private readonly List<LocalEndpoint> _all = [];

    public Task<IReadOnlyList<LocalEndpoint>> ListAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<LocalEndpoint>>([.. _all]);

    public Task SaveAsync(LocalEndpoint endpoint, CancellationToken cancellationToken)
    {
        _all.RemoveAll(e => e.Id == endpoint.Id);
        _all.Add(endpoint);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        _all.RemoveAll(e => e.Id == id);
        return Task.CompletedTask;
    }
}
