using System.Text.Json;
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
        await using var db = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        var row = await SettingTable.Instance.FirstAsync(db, "\"Key\" = $key", cancellationToken, null, ("$key", Key)).ConfigureAwait(false);
        return row is null ? new AppSettings() : JsonSerializer.Deserialize(row.Value, StorageJsonContext.Default.AppSettings) ?? new AppSettings();
    }

    /// <inheritdoc />
    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var value = JsonSerializer.Serialize(settings, StorageJsonContext.Default.AppSettings);
        await using var db = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        await SettingTable.Instance.UpsertAsync(db, new SettingRow { Key = Key, Value = value }, cancellationToken).ConfigureAwait(false);
    }
}
