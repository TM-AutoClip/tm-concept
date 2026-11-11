using Microsoft.EntityFrameworkCore;
using TMConcept.Core.Concepts;
using TMConcept.Storage.Database;

namespace TMConcept.Storage.Stores;

/// <summary><see cref="ITopicSuggestionStore"/>: the table <c>topic_suggestions</c>.</summary>
public sealed class TopicSuggestionStore(ConceptDatabase database) : ITopicSuggestionStore
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<TopicSuggestion>> ListAsync(Guid profileId, int max, CancellationToken cancellationToken)
    {
        await using var db = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await db.TopicSuggestions.Where(t => t.ProfileId == profileId).OrderByDescending(t => t.CreatedAtUtc).Take(max)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TopicSuggestion>> ListAsync(Guid profileId, TopicStatus status, CancellationToken cancellationToken)
    {
        await using var db = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await db.TopicSuggestions.Where(t => t.ProfileId == profileId && t.Status == status).OrderBy(t => t.CreatedAtUtc).ThenBy(t => t.Id)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task AddAsync(IReadOnlyList<TopicSuggestion> suggestions, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(suggestions);
        if (suggestions.Count == 0)
        {
            return;
        }

        await using var db = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        db.TopicSuggestions.AddRange(suggestions);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SetStatusAsync(IReadOnlyCollection<Guid> ids, TopicStatus status, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ids);
        if (ids.Count == 0)
        {
            return;
        }

        await using var db = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await db.TopicSuggestions.Where(t => ids.Contains(t.Id)).ExecuteUpdateAsync(s => s.SetProperty(t => t.Status, status), cancellationToken)
            .ConfigureAwait(false);
    }
}
