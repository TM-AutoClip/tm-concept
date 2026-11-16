using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TMConcept.Core.Endpoints;
using TMConcept.Storage.Database;

namespace TMConcept.Storage.Stores;

/// <summary>
/// <see cref="IEndpointStore"/> in the settings table (no table of its own, so no schema change): the endpoints as one JSON list under
/// <see cref="EndpointsSetting"/> (without tokens), their tokens apart under <see cref="TokensSetting"/> (endpoint id → token). A backup
/// holds the list with the settings and the tokens only when the API keys are ticked (<see cref="Backup.BackupService"/> removes that row
/// otherwise), so an endpoint restored without keys works again once its token is typed in, and a token never travels in a keyless backup.
/// </summary>
public sealed class EndpointStore(ConceptDatabase database) : IEndpointStore
{
    /// <summary>Settings key of the endpoint list.</summary>
    public const string EndpointsSetting = "endpoints";

    /// <summary>Settings key of the endpoint tokens.</summary>
    public const string TokensSetting = "endpoint-tokens";

    /// <inheritdoc />
    public async Task<IReadOnlyList<LocalEndpoint>> ListAsync(CancellationToken cancellationToken)
    {
        await using var db = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        var (list, tokens) = await ReadAsync(db, cancellationToken).ConfigureAwait(false);
        foreach (var endpoint in list)
        {
            endpoint.Token = tokens.GetValueOrDefault(Key(endpoint.Id)) ?? string.Empty;
        }

        return list.OrderBy(e => e.CreatedAtUtc).ToList();
    }

    /// <inheritdoc />
    public async Task SaveAsync(LocalEndpoint endpoint, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        await using var db = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        var (list, tokens) = await ReadAsync(db, cancellationToken).ConfigureAwait(false);
        list.RemoveAll(e => e.Id == endpoint.Id);
        list.Add(endpoint);
        tokens.Remove(Key(endpoint.Id));
        if (endpoint.Token.Length > 0)
        {
            tokens[Key(endpoint.Id)] = endpoint.Token;
        }

        await WriteAsync(db, list, tokens, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var db = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
        var (list, tokens) = await ReadAsync(db, cancellationToken).ConfigureAwait(false);
        list.RemoveAll(e => e.Id == id);
        tokens.Remove(Key(id));
        await WriteAsync(db, list, tokens, cancellationToken).ConfigureAwait(false);
    }

    private static string Key(Guid id) => id.ToString("N", System.Globalization.CultureInfo.InvariantCulture);

    private static async Task<(List<LocalEndpoint> List, Dictionary<string, string> Tokens)> ReadAsync(ConceptDbContext db, CancellationToken cancellationToken)
    {
        var rows = await db.Settings.Where(s => s.Key == EndpointsSetting || s.Key == TokensSetting).ToListAsync(cancellationToken).ConfigureAwait(false);
        var list = rows.FirstOrDefault(r => r.Key == EndpointsSetting) is { } listRow
            ? JsonSerializer.Deserialize(listRow.Value, StorageJsonContext.Default.ListLocalEndpoint) ?? []
            : [];
        var tokens = rows.FirstOrDefault(r => r.Key == TokensSetting) is { } tokenRow
            ? JsonSerializer.Deserialize(tokenRow.Value, StorageJsonContext.Default.DictionaryStringString) ?? []
            : [];
        return (list, tokens);
    }

    private static async Task WriteAsync(ConceptDbContext db, List<LocalEndpoint> list, Dictionary<string, string> tokens, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await db.Settings.Where(s => s.Key == EndpointsSetting || s.Key == TokensSetting).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        if (list.Count > 0)
        {
            db.Settings.Add(new SettingRow { Key = EndpointsSetting, Value = JsonSerializer.Serialize(list, StorageJsonContext.Default.ListLocalEndpoint) });
        }

        if (tokens.Count > 0)
        {
            db.Settings.Add(new SettingRow { Key = TokensSetting, Value = JsonSerializer.Serialize(tokens, StorageJsonContext.Default.DictionaryStringString) });
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }
}
