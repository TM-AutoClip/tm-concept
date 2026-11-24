using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TMConcept.Core.Keys;
using TMConcept.Storage.Database;

namespace TMConcept.Storage.Stores;

/// <summary>
/// <see cref="ISignInStore"/> in the settings table (no table of its own, so no schema change): one row per provider,
/// <c>sign-in.&lt;provider&gt;</c>, holding the <see cref="SignInRecord"/> as JSON. It is a key: a backup holds it only when the API keys
/// are ticked (<see cref="Backup.BackupService"/> removes the rows starting with <see cref="Prefix"/> otherwise), like the tokens of the
/// local servers.
/// </summary>
public sealed class SignInStore(ConceptDatabase database) : ISignInStore
{
    /// <summary>Settings key prefix of the sign-ins.</summary>
    public const string Prefix = "sign-in.";

    /// <inheritdoc />
    public async Task<SignInRecord?> GetAsync(string provider, CancellationToken cancellationToken)
    {
        var name = Prefix + provider;
        return await database.ReadAsync<SignInRecord?>(async db =>
        {
            var row = await db.Settings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == name, cancellationToken).ConfigureAwait(false);
            return row is null ? null : JsonSerializer.Deserialize(row.Value, StorageJsonContext.Default.SignInRecord);
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SaveAsync(SignInRecord record, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentException.ThrowIfNullOrEmpty(record.Provider);
        var name = Prefix + record.Provider;
        var value = JsonSerializer.Serialize(record, StorageJsonContext.Default.SignInRecord);
        await database.WriteAsync(async db =>
        {
            await db.UpsertAsync(new SettingRow { Key = name, Value = value }, s => s.Key == name, cancellationToken).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string provider, CancellationToken cancellationToken)
    {
        var name = Prefix + provider;
        await database.WriteAsync(async db =>
        {
            await db.Settings.Where(s => s.Key == name).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }
}
