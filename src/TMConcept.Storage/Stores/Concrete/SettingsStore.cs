using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TMConcept.Core.Settings;
using TMConcept.Storage.Database;

namespace TMConcept.Storage.Stores;

/// <summary><see cref="ISettingsStore"/>: one JSON document.</summary>
public sealed class SettingsStore(ConceptDatabase database) : ISettingsStore
{
    private const string Key = "app";

    /// <inheritdoc />
    public async Task<AppSettings> GetAsync(CancellationToken cancellationToken)
    {
        return await database.ReadAsync<AppSettings>(async db =>
        {
            var row = await db.Settings.FirstOrDefaultAsync(s => s.Key == Key, cancellationToken).ConfigureAwait(false);
            return row is null ? new AppSettings() : JsonSerializer.Deserialize(row.Value, StorageJsonContext.Default.AppSettings) ?? new AppSettings();
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var value = JsonSerializer.Serialize(settings, StorageJsonContext.Default.AppSettings);
        await database.WriteAsync(async db =>
        {
            await db.UpsertAsync(new SettingRow { Key = Key, Value = value }, s => s.Key == Key, cancellationToken).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }
}
