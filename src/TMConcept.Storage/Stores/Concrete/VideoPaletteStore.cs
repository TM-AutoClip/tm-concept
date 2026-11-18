using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TMConcept.Core.Render;
using TMConcept.Core.Styles;
using TMConcept.Storage.Database;

namespace TMConcept.Storage.Stores;

/// <summary>
/// <see cref="IVideoPaletteStore"/>: one settings document (<c>settings</c> row "video-palettes", a JSON list), so the saved palettes need no
/// schema step and travel with the encrypted database and its backups.
/// </summary>
public sealed class VideoPaletteStore(ConceptDatabase database) : IVideoPaletteStore
{
    private const string Key = "video-palettes";

    /// <inheritdoc />
    public async Task<IReadOnlyList<SavedPalette>> ListAsync(CancellationToken cancellationToken)
    {
        await using var db = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        return (await ReadAsync(db, cancellationToken).ConfigureAwait(false))
            .OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(p => p.CreatedAtUtc).ToList();
    }

    /// <inheritdoc />
    public async Task SaveAsync(SavedPalette palette, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(palette);
        var name = palette.Name?.Trim() ?? string.Empty;
        if (name.Length == 0 || name.Length > VideoStyleLimits.NameMax)
        {
            throw new ArgumentException("A palette needs a name of 1 to " + VideoStyleLimits.NameMax + " characters.", nameof(palette));
        }

        var check = PaletteDesigner.Check(palette.Colors);
        if (!check.Ok)
        {
            throw new ArgumentException("The palette cannot be saved: " + string.Join(", ", check.Errors) + ".", nameof(palette));
        }

        palette.Name = name;
        palette.Colors = palette.Colors with { Id = VideoPalettes.CustomId };
        await using var db = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var all = await ReadAsync(db, cancellationToken).ConfigureAwait(false);
        var index = all.FindIndex(p => p.Id == palette.Id);
        if (index >= 0)
        {
            all[index] = palette;
        }
        else
        {
            if (all.Count >= VideoStyleLimits.PalettesMax)
            {
                throw new VideoStyleLimitException($"There are already {VideoStyleLimits.PalettesMax} saved palettes; delete one first.");
            }

            all.Add(palette);
        }

        await WriteAsync(db, all, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var db = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var all = await ReadAsync(db, cancellationToken).ConfigureAwait(false);
        if (all.RemoveAll(p => p.Id == id) > 0)
        {
            await WriteAsync(db, all, cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<List<SavedPalette>> ReadAsync(ConceptDbContext db, CancellationToken cancellationToken)
    {
        var row = await db.Settings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == Key, cancellationToken).ConfigureAwait(false);
        return row is null
            ? []
            : JsonSerializer.Deserialize(row.Value, CoreJsonContext.Default.ListSavedPalette)
                ?? throw new InvalidDataException("The saved palettes are stored empty.");
    }

    private static async Task WriteAsync(ConceptDbContext db, List<SavedPalette> all, CancellationToken cancellationToken)
    {
        var value = JsonSerializer.Serialize(all, CoreJsonContext.Default.ListSavedPalette);
        await db.UpsertAsync(new SettingRow { Key = Key, Value = value }, s => s.Key == Key, cancellationToken).ConfigureAwait(false);
    }
}
