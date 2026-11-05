using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TMConcept.Core.Settings;
using TMConcept.Storage.Database;

namespace TMConcept.Storage.Stores;

/// <summary><see cref="ISettingsStore"/>: one JSON document.</summary>
public sealed class SettingsStore(IDbContextFactory<ConceptDbContext> factory) : ISettingsStore
{
    private const string Key = "app";

    /// <inheritdoc />
    public async Task<AppSettings> GetAsync(CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var row = await db.Settings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == Key, cancellationToken).ConfigureAwait(false);
        return row is null ? new AppSettings() : JsonSerializer.Deserialize(row.Value, StorageJsonContext.Default.AppSettings) ?? new AppSettings();
    }

    /// <inheritdoc />
    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var value = JsonSerializer.Serialize(settings, StorageJsonContext.Default.AppSettings);
        await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var row = await db.Settings.FirstOrDefaultAsync(s => s.Key == Key, cancellationToken).ConfigureAwait(false);
        if (row is null)
        {
            db.Settings.Add(new SettingRow { Key = Key, Value = value });
        }
        else
        {
            row.Value = value;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
