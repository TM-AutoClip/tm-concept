namespace TMConcept.Core.Keys;

/// <summary>API keys (encrypted database; included in a password-encrypted backup only when the user chooses).</summary>
public interface IApiKeyStore
{
    /// <summary>Keys, optionally of one provider.</summary>
    Task<IReadOnlyList<ApiKey>> ListAsync(string? provider, CancellationToken cancellationToken);

    /// <summary>One key.</summary>
    Task<ApiKey?> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Adds or updates.</summary>
    Task SaveAsync(ApiKey key, CancellationToken cancellationToken);

    /// <summary>Deletes.</summary>
    Task DeleteAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Records a successful connection test.</summary>
    Task MarkOkAsync(Guid id, DateTimeOffset atUtc, CancellationToken cancellationToken);
}
