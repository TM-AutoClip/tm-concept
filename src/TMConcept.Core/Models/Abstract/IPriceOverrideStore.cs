namespace TMConcept.Core.Models;

/// <summary>Prices the user entered.</summary>
public interface IPriceOverrideStore
{
    /// <summary>Every override.</summary>
    Task<IReadOnlyList<ModelPriceOverride>> ListAsync(CancellationToken cancellationToken);

    /// <summary>Adds or updates.</summary>
    Task SaveAsync(ModelPriceOverride price, CancellationToken cancellationToken);

    /// <summary>Removes.</summary>
    Task DeleteAsync(string provider, string modelId, CancellationToken cancellationToken);
}
