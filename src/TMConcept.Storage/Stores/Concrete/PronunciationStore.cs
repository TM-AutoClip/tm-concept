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
        return await database.ReadAsync<IReadOnlyList<PronunciationEntry>>(async db =>
        {
            return await db.Pronunciations.Where(p => p.Language == language).OrderBy(p => p.AddedAtUtc).ToListAsync(cancellationToken).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SaveAsync(PronunciationEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        await database.WriteAsync(async db =>
        {
            var id = entry.Id;
            await db.UpsertAsync(entry, p => p.Id == id, cancellationToken).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        await database.WriteAsync(async db =>
        {
            await db.Pronunciations.Where(p => p.Id == id).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }
}
