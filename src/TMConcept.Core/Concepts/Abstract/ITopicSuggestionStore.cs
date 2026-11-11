namespace TMConcept.Core.Concepts;

/// <summary>The topics "Suggest topics" brought, per profile (deleted with the profile; a backup holds them with the concepts).</summary>
public interface ITopicSuggestionStore
{
    /// <summary>The newest <paramref name="max"/> suggestions of a profile, newest first.</summary>
    Task<IReadOnlyList<TopicSuggestion>> ListAsync(Guid profileId, int max, CancellationToken cancellationToken);

    /// <summary>The suggestions of a profile in <paramref name="status"/>, oldest first (the order of the selected list).</summary>
    Task<IReadOnlyList<TopicSuggestion>> ListAsync(Guid profileId, TopicStatus status, CancellationToken cancellationToken);

    /// <summary>Keeps new suggestions.</summary>
    Task AddAsync(IReadOnlyList<TopicSuggestion> suggestions, CancellationToken cancellationToken);

    /// <summary>Sets the status of these suggestions (an unknown id is skipped).</summary>
    Task SetStatusAsync(IReadOnlyCollection<Guid> ids, TopicStatus status, CancellationToken cancellationToken);
}
