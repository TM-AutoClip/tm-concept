using Microsoft.EntityFrameworkCore;
using TMConcept.Core.Speech;
using TMConcept.Storage.Database;

namespace TMConcept.Storage.Stores;

/// <summary><see cref="IVoiceSampleStore"/>: the table <c>voice_samples</c> (the sound as a blob, encrypted with the whole file).</summary>
public sealed class VoiceSampleStore(ConceptDatabase database) : IVoiceSampleStore
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<VoiceSampleInfo>> ListAsync(CancellationToken cancellationToken)
    {
        await using var db = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        var rows = await db.VoiceSamples.OrderBy(v => v.AddedAtUtc).Select(v => new { v.Id, v.Name, v.DurationMs, v.AddedAtUtc })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.Select(r => new VoiceSampleInfo(r.Id, r.Name, r.DurationMs, r.AddedAtUtc)).ToList();
    }

    /// <inheritdoc />
    public async Task<VoiceSample?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var db = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await db.VoiceSamples.FirstOrDefaultAsync(v => v.Id == id, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> HasAsync(string sha256, CancellationToken cancellationToken)
    {
        await using var db = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await db.VoiceSamples.AnyAsync(v => v.Sha256 == sha256, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task AddAsync(VoiceSample sample, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sample);
        await using var db = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        db.VoiceSamples.Add(sample);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var db = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await db.VoiceSamples.Where(v => v.Id == id).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
    }
}
