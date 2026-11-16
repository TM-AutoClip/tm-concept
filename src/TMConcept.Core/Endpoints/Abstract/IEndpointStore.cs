namespace TMConcept.Core.Endpoints;

/// <summary>The saved local servers (encrypted database, settings rows: no table of their own).</summary>
public interface IEndpointStore
{
    /// <summary>Every saved endpoint with its token, oldest first.</summary>
    Task<IReadOnlyList<LocalEndpoint>> ListAsync(CancellationToken cancellationToken);

    /// <summary>Adds or replaces (by id).</summary>
    Task SaveAsync(LocalEndpoint endpoint, CancellationToken cancellationToken);

    /// <summary>Deletes (with its token).</summary>
    Task DeleteAsync(Guid id, CancellationToken cancellationToken);
}
