using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TMConcept.Core.Render;
using TMConcept.Core.Styles;
using TMConcept.Storage.Database;

namespace TMConcept.Storage.Stores;

/// <summary>
/// <see cref="IVideoStyleStore"/>: the table <c>video_styles</c> (one JSON document per template). The limit of
/// <see cref="VideoStyleLimits.Max"/> templates is checked in the same transaction as the insert.
/// </summary>
public sealed class VideoStyleStore(ConceptDatabase database) : IVideoStyleStore
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<VideoStyle>> ListAsync(CancellationToken cancellationToken)
    {
        await using var db = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        var rows = await db.VideoStyles.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.Select(Read).OrderBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(s => s.CreatedAtUtc).ToList();
    }

    /// <inheritdoc />
    public async Task<VideoStyle?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var db = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        var row = await db.VideoStyles.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, cancellationToken).ConfigureAwait(false);
        return row is null ? null : Read(row);
    }

    /// <inheritdoc />
    public async Task SaveAsync(VideoStyle style, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(style);
        var problems = VideoStyleRules.Problems(style);
        if (problems.Count > 0)
        {
            throw new ArgumentException("The template cannot be saved: " + string.Join(", ", problems) + ".", nameof(style));
        }

        style.Name = style.Name.Trim();
        var row = new VideoStyleRow
        {
            Id = style.Id,
            Name = style.Name,
            Json = JsonSerializer.Serialize(style, CoreJsonContext.Default.VideoStyle),
            CreatedAtUtc = style.CreatedAtUtc,
            UpdatedAtUtc = style.UpdatedAtUtc,
        };
        await using var db = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var id = style.Id;
        if (await db.VideoStyles.AnyAsync(s => s.Id == id, cancellationToken).ConfigureAwait(false))
        {
            db.VideoStyles.Update(row);
        }
        else
        {
            if (await db.VideoStyles.CountAsync(cancellationToken).ConfigureAwait(false) >= VideoStyleLimits.Max)
            {
                throw new VideoStyleLimitException();
            }

            db.VideoStyles.Add(row);
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var db = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await db.VideoStyles.Where(s => s.Id == id).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The template of a row: its JSON, with the id and name of the columns (the columns are the truth).</summary>
    private static VideoStyle Read(VideoStyleRow row)
    {
        var style = JsonSerializer.Deserialize(row.Json, CoreJsonContext.Default.VideoStyle)
            ?? throw new InvalidDataException($"The template {row.Id} is stored empty.");
        style.Id = row.Id;
        style.Name = row.Name;
        return style;
    }
}
