using Microsoft.EntityFrameworkCore;
using TMConcept.Core.Pronunciation;
using TMConcept.Storage.Database;

namespace TMConcept.Storage.Stores;

/// <summary><see cref="IPronunciationStore"/>: the table <c>pronunciations</c>.</summary>
public sealed class PronunciationStore(ConceptDatabase database) : IPronunciationStore
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<PronunciationEntry>> ListAsync(string language, CancellationToken cancellationToken)
    {
        await using var db = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await db.Pronunciations.Where(p => p.Language == language).OrderBy(p => p.AddedAtUtc).ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SaveAsync(PronunciationEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        await using var db = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        var id = entry.Id;
        await db.UpsertAsync(entry, p => p.Id == id, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var db = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await db.Pronunciations.Where(p => p.Id == id).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
    }
}
