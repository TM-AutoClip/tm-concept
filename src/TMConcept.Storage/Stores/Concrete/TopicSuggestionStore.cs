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
}
