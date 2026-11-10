namespace TMConcept.Core.Concepts;

/// <summary>The AI-edit instructions of the profiles (only the newest <see cref="PromptMemoryLimits.WishesKept"/> of a profile are kept).</summary>
public interface IConceptWishStore
{
    /// <summary>The newest <paramref name="max"/> wishes of a profile, newest first.</summary>
    Task<IReadOnlyList<ConceptWish>> ListAsync(Guid profileId, int max, CancellationToken cancellationToken);

    /// <summary>Keeps a wish (the same text again only moves it to the front) and drops the profile's older ones beyond the kept count.</summary>
    Task AddAsync(ConceptWish wish, CancellationToken cancellationToken);
}
