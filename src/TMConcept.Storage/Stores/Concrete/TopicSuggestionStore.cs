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

    /// <inheritdoc />
    public async Task MarkUsedAsync(Guid profileId, string text, CancellationToken cancellationToken)
    {
        await using var db = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        var rows = await db.TopicSuggestions.AsTracking().Where(t => t.ProfileId == profileId && t.Text == text).ToListAsync(cancellationToken).ConfigureAwait(false);
        // The flag is filtered here, not in the query: it is stored as 0/1 through a value converter.
        foreach (var row in rows.Where(r => !r.Used))
        {
            row.Used = true;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
