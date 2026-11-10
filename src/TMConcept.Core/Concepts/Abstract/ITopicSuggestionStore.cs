namespace TMConcept.Core.Concepts;

/// <summary>The topics "Suggest topics" brought, per profile (deleted with the profile; a backup holds them with the concepts).</summary>
public interface ITopicSuggestionStore
{
    /// <summary>The newest <paramref name="max"/> suggestions of a profile, newest first.</summary>
    Task<IReadOnlyList<TopicSuggestion>> ListAsync(Guid profileId, int max, CancellationToken cancellationToken);

    /// <summary>Keeps new suggestions.</summary>
    Task AddAsync(IReadOnlyList<TopicSuggestion> suggestions, CancellationToken cancellationToken);

    /// <summary>Marks the suggestions of a profile with exactly this text as used (clicked into the extra request).</summary>
    Task MarkUsedAsync(Guid profileId, string text, CancellationToken cancellationToken);
}
