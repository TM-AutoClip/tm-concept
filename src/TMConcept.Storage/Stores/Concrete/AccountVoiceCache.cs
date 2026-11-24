using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TMConcept.Core.Speech;
using TMConcept.Storage.Database;

namespace TMConcept.Storage.Stores;

/// <summary>
/// <see cref="IAccountVoiceCache"/> in the settings table (no schema change): one row per provider, <c>voices.&lt;provider&gt;</c>, with the
/// last list read from the account as JSON. It travels with the settings in a backup (voice ids and names, no key).
/// </summary>
public sealed class AccountVoiceCache(ConceptDatabase database) : IAccountVoiceCache
{
    /// <summary>Settings key prefix of the lists.</summary>
    public const string Prefix = "voices.";

    /// <inheritdoc />
    public async Task<IReadOnlyList<AccountVoice>?> GetAsync(string provider, CancellationToken cancellationToken)
    {
        var name = Prefix + provider;
        return await database.ReadAsync<IReadOnlyList<AccountVoice>?>(async db =>
        {
            var row = await db.Settings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == name, cancellationToken).ConfigureAwait(false);
            if (row is null)
            {
                return null;
            }

            try
            {
                return JsonSerializer.Deserialize(row.Value, StorageJsonContext.Default.ListAccountVoice);
            }
            catch (JsonException)
            {
                // Only a cache: a damaged list is read again from the account.
                return null;
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SaveAsync(string provider, IReadOnlyList<AccountVoice> voices, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(voices);
        var name = Prefix + provider;
        var value = JsonSerializer.Serialize(voices.ToList(), StorageJsonContext.Default.ListAccountVoice);
        await database.WriteAsync(async db =>
        {
            await db.UpsertAsync(new SettingRow { Key = name, Value = value }, s => s.Key == name, cancellationToken).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }
}
